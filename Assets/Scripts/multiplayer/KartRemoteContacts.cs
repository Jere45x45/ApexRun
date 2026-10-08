using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Choques entre karts de distintas computadoras.
/// Cada computadora simula solo sus karts; los de los demás son copias que no
/// se mueven con los golpes. Chocar contra una copia sería como chocar contra
/// una pared: uno rebota el doble y el otro ni se entera.
/// Por eso el kart propio no choca físicamente con las copias. En cambio:
/// - Cada paso de física busca contacto con las copias, puestas donde está
///   el otro kart ahora (lo último que mandó su dueño, adelantado por la
///   latencia), no donde se lo ve.
/// - Si hay contacto, al kart propio le aplica su parte del golpe, como entre
///   dos karts de igual masa, y lo saca de la mitad de la superposición.
/// La computadora del otro hace lo mismo con el suyo, así los dos salen
/// empujados. Entre karts que corren en la misma computadora (el host y los
/// bots) sigue chocando la física de siempre.
/// </summary>
[RequireComponent(typeof(KartBehaviour))]
public class KartRemoteContacts : MonoBehaviour
{
    [SerializeField] private KartBehaviour kart;

    [SerializeField] private Rigidbody body;

    [Tooltip("Cajas del chasis (colliders que no son trigger).")]
    [SerializeField] private Collider[] hull = new Collider[0];

    [Header("Golpe")]
    [Tooltip("Rebote: 0 = los karts no rebotan (como las cajas, que no tienen rebote).")]
    [SerializeField, Range(0f, 1f)] private float restitution = 0f;

    [Tooltip("Parte de la superposición que corrige este kart en cada paso. La otra mitad la corrige el otro kart en su computadora.")]
    [SerializeField, Range(0f, 1f)] private float overlapCorrection = 0.5f;

    [Tooltip("Superposición que se tolera sin corregir (m).")]
    [SerializeField, Min(0f)] private float overlapSlop = 0.01f;

    [Tooltip("Lo máximo que se corre el kart por paso de física para salir de la superposición (m).")]
    [SerializeField, Min(0.001f)] private float maxCorrectionPerStep = 0.05f;

    [Header("Latencia")]
    [Tooltip("Máximo que se adelanta la copia para estimar dónde está el otro kart ahora (s).")]
    [SerializeField, Min(0f)] private float maxPrediction = 0.25f;

    // Más allá de esta distancia entre karts no se buscan contactos (m).
    private const float CheckDistance = 5f;

    // Lo máximo que se recuerda el golpe dado al otro kart (s), por si nunca
    // llega la confirmación de su computadora.
    private const float MaxHitMemory = 0.5f;

    private class Remote
    {
        public NetworkKart Kart;
        public Rigidbody Body;
        public KartNetworkTransform Network;
        public Collider[] Hull;

        // Golpe que se le dio al otro kart y que su computadora todavía no
        // confirmó: cambio de velocidad que le toca, velocidad que mandaba su
        // dueño en ese momento, y hasta cuándo se recuerda.
        public Vector3 HitVelocity;
        public Vector3 VelocityAtHit;
        public float HitUntil;
    }

    private readonly List<Remote> remotes = new List<Remote>();

    private void Reset()
    {
        kart = GetComponent<KartBehaviour>();
        body = GetComponent<Rigidbody>();
        hull = FindHull(gameObject);
    }

    private void OnEnable()
    {
        NetworkKart.Spawned += HandleKartSpawned;
        NetworkKart.Despawned += HandleKartDespawned;
    }

    private void OnDisable()
    {
        NetworkKart.Spawned -= HandleKartSpawned;
        NetworkKart.Despawned -= HandleKartDespawned;

        remotes.Clear();
    }

    private void Start()
    {
        // Karts que aparecieron antes que este componente.
        foreach (NetworkKart other in FindObjectsByType<NetworkKart>(FindObjectsSortMode.None))
        {
            if (other.IsSpawned)
                HandleKartSpawned(other);
        }
    }

    private void HandleKartSpawned(NetworkKart spawned)
    {
        if (spawned == null)
            return;

        // Apareció este kart: recién ahora se sabe si corre acá.
        if (spawned.Kart == kart)
        {
            foreach (NetworkKart other in FindObjectsByType<NetworkKart>(FindObjectsSortMode.None))
            {
                if (other.IsSpawned && other.Kart != kart)
                    AddRemote(other);
            }

            return;
        }

        AddRemote(spawned);
    }

    private void AddRemote(NetworkKart other)
    {
        // Solo el kart que corre acá, contra copias de karts que corren en otra computadora.
        if (!kart.IsSimulated || other.Kart == null || other.Kart.IsSimulated)
            return;

        foreach (Remote existing in remotes)
        {
            if (existing.Kart == other)
                return;
        }

        Remote remote = new Remote
        {
            Kart = other,
            Body = other.GetComponent<Rigidbody>(),
            Network = other.GetComponent<KartNetworkTransform>(),
            Hull = FindHull(other.gameObject)
        };

        if (remote.Body == null || remote.Network == null)
            return;

        // El kart propio deja de chocar físicamente con la copia.
        foreach (Collider own in hull)
        {
            foreach (Collider theirs in remote.Hull)
            {
                if (own != null && theirs != null)
                    Physics.IgnoreCollision(own, theirs, true);
            }
        }

        remotes.Add(remote);
    }

    private void HandleKartDespawned(NetworkKart other)
    {
        remotes.RemoveAll(remote => remote.Kart == other);
    }

    private void FixedUpdate()
    {
        if (remotes.Count == 0 || !kart.IsSimulated)
            return;

        for (int i = remotes.Count - 1; i >= 0; i--)
        {
            Remote remote = remotes[i];

            if (remote.Kart == null || remote.Body == null)
            {
                remotes.RemoveAt(i);
                continue;
            }

            if (remote.Network.HasOwnerState)
                ResolveContact(remote);
        }
    }

    /// <summary>Busca contacto con la copia puesta donde está el otro kart ahora y, si hay, lo resuelve.</summary>
    private void ResolveContact(Remote remote)
    {
        Vector3 remoteVelocity = remote.Network.OwnerVelocity + GetPendingHit(remote);
        Vector3 predictedPosition = remote.Network.OwnerPosition + remoteVelocity * GetPrediction(remote);

        if ((predictedPosition - body.position).sqrMagnitude > CheckDistance * CheckDistance)
            return;

        // Corrimiento de la copia: de donde se la ve a donde está ahora.
        Vector3 shift = predictedPosition - remote.Body.position;

        float deepest = 0f;
        Vector3 normal = Vector3.zero;
        Vector3 point = Vector3.zero;

        foreach (Collider own in hull)
        {
            if (own == null || !own.enabled)
                continue;

            foreach (Collider theirs in remote.Hull)
            {
                if (theirs == null || !theirs.enabled)
                    continue;

                Vector3 theirPosition = theirs.transform.position + shift;

                if (!Physics.ComputePenetration(
                        own, own.transform.position, own.transform.rotation,
                        theirs, theirPosition, theirs.transform.rotation,
                        out Vector3 direction, out float distance))
                {
                    continue;
                }

                if (distance > deepest)
                {
                    deepest = distance;
                    normal = direction;
                    point = own.ClosestPoint(theirs.bounds.center + shift);
                }
            }
        }

        if (deepest <= 0f)
            return;

        // Los karts se empujan de costado o de frente, no hacia arriba: las
        // cajas son bajas y la salida más corta muchas veces sería vertical,
        // y eso subiría un kart arriba del otro.
        Vector3 up = body.transform.up;
        normal = Vector3.ProjectOnPlane(normal, up);

        if (normal.sqrMagnitude < 0.01f)
            normal = Vector3.ProjectOnPlane(body.position - predictedPosition, up);

        if (normal.sqrMagnitude < 1e-6f)
            return;

        normal.Normalize();

        // El golpe se aplica a la altura del centro de masa: hace girar al kart
        // (un toque de costado lo desvía) pero no lo levanta ni lo vuelca.
        point -= up * Vector3.Dot(point - body.worldCenterOfMass, up);

        float impulse = ApplyImpulse(normal, point, remoteVelocity, remote.Body.mass);

        // El otro kart recibe el golpe contrario en su computadora, pero acá
        // recién se va a ver cuando llegue su próxima velocidad. Hasta
        // entonces se lo considera ya golpeado: si no, cada paso lo vería
        // quieto y el kart propio se seguiría frenando contra él.
        if (impulse > 0f)
        {
            if (Time.time >= remote.HitUntil)
            {
                remote.HitVelocity = Vector3.zero;
                remote.VelocityAtHit = remote.Network.OwnerVelocity;
            }

            remote.HitVelocity -= normal * impulse / Mathf.Max(1f, remote.Body.mass);
            remote.HitUntil = Time.time + MaxHitMemory;
        }

        float correction = Mathf.Min((deepest - overlapSlop) * overlapCorrection, maxCorrectionPerStep);

        if (correction > 0f)
            body.position += normal * correction;
    }

    /// <summary>
    /// Golpe entre dos cuerpos de la masa de cada uno, sin la rotación del
    /// otro (esa la resuelve su computadora): al kart propio le toca su parte.
    /// </summary>
    private float ApplyImpulse(Vector3 normal, Vector3 point, Vector3 remoteVelocity, float remoteMass)
    {
        Vector3 relativeVelocity = body.GetPointVelocity(point) - remoteVelocity;
        float approachSpeed = Vector3.Dot(relativeVelocity, normal);

        // Ya se están separando.
        if (approachSpeed >= 0f)
            return 0f;

        Vector3 arm = point - body.worldCenterOfMass;
        Vector3 angular = InverseInertia(Vector3.Cross(arm, normal));

        float effectiveMass =
            1f / body.mass +
            Vector3.Dot(normal, Vector3.Cross(angular, arm)) +
            1f / Mathf.Max(1f, remoteMass);

        float impulse = -(1f + restitution) * approachSpeed / effectiveMass;

        body.AddForceAtPosition(normal * impulse, point, ForceMode.Impulse);

        return impulse;
    }

    /// <summary>
    /// Golpe dado al otro kart que todavía no se ve en lo que manda su dueño.
    /// Se olvida cuando su velocidad ya cambió en esa dirección (llegó la
    /// confirmación) o después de MaxHitMemory.
    /// </summary>
    private static Vector3 GetPendingHit(Remote remote)
    {
        if (Time.time >= remote.HitUntil)
            return Vector3.zero;

        float expected = remote.HitVelocity.magnitude;

        if (expected < 0.01f)
            return Vector3.zero;

        Vector3 direction = remote.HitVelocity / expected;
        float received = Vector3.Dot(remote.Network.OwnerVelocity - remote.VelocityAtHit, direction);

        if (received >= expected * 0.7f)
        {
            remote.HitUntil = 0f;
            return Vector3.zero;
        }

        return direction * (expected - Mathf.Max(0f, received));
    }

    private Vector3 InverseInertia(Vector3 torque)
    {
        Quaternion rotation = body.rotation * body.inertiaTensorRotation;
        Vector3 local = Quaternion.Inverse(rotation) * torque;
        Vector3 inertia = body.inertiaTensor;

        local.x = inertia.x > 0f ? local.x / inertia.x : 0f;
        local.y = inertia.y > 0f ? local.y / inertia.y : 0f;
        local.z = inertia.z > 0f ? local.z / inertia.z : 0f;

        return rotation * local;
    }

    /// <summary>
    /// Cuánto adelantar lo último que mandó el dueño para estimar dónde está
    /// ahora: lo que tardó en llegar (latencia del dueño al servidor y del
    /// servidor acá) más el tiempo desde que llegó.
    /// </summary>
    private float GetPrediction(Remote remote)
    {
        NetworkManager manager = NetworkManager.Singleton;
        float travel = 0f;

        if (manager != null && manager.IsListening)
        {
            NetworkTransport transport = manager.NetworkConfig.NetworkTransport;
            ulong owner = remote.Kart.OwnerClientId;

            if (manager.IsServer)
            {
                // Del dueño al servidor: media ida y vuelta.
                travel = transport.GetCurrentRtt(owner) * 0.0005f;
            }
            else
            {
                float mine = transport.GetCurrentRtt(NetworkManager.ServerClientId) * 0.0005f;

                // Si el dueño es otro cliente, su tramo hasta el servidor se
                // estima igual al propio.
                travel = owner == NetworkManager.ServerClientId ? mine : mine * 2f;
            }
        }

        return Mathf.Min(travel + remote.Network.OwnerStateAge, maxPrediction);
    }

    private static Collider[] FindHull(GameObject root)
    {
        List<Collider> found = new List<Collider>();

        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            if (!collider.isTrigger)
                found.Add(collider);
        }

        return found.ToArray();
    }
}
