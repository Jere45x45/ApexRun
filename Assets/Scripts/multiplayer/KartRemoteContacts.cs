using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Choques entre karts de distintas computadoras.
/// Cada computadora simula solo sus karts; los de los demás son copias que no
/// se mueven con los golpes. Chocar contra una copia sería como chocar contra
/// una pared: uno rebota el doble y el otro ni se entera. Por eso el kart
/// propio no choca físicamente con las copias. En cambio:
/// - Cada paso de física busca contacto con las copias, puestas donde está el
///   otro kart ahora (lo último que mandó su dueño, adelantado por la
///   latencia). Es lo mismo que se dibuja (KartNetworkTransform), así que el
///   choque pasa donde se ve.
/// - De cada par de karts resuelve el choque uno solo, siempre el mismo (el de
///   NetworkObjectId más bajo): calcula el golpe entre los dos con las
///   velocidades de antes del choque, se aplica su parte y le manda al otro
///   la suya por red (NetworkKart.SendHit, que valida el servidor). Si lo
///   resolvieran los dos, el segundo usaría la velocidad que el primero ya
///   cambió y el golpe quedaría mal repartido.
/// - Los dos corrigen su mitad de la superposición.
/// Entre karts que corren en la misma computadora (el host y los bots) sigue
/// chocando la física de siempre.
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

    // Más allá de esta distancia entre karts no se buscan contactos (m).
    private const float CheckDistance = 5f;

    // Lo máximo que se recuerda el golpe dado al otro kart (s), por si nunca
    // llega la confirmación de su computadora.
    private const float MaxHitMemory = 0.5f;

    /// <summary>Caja del chasis y su posición respecto del kart (fija).</summary>
    private struct HullPart
    {
        public Collider Collider;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Center;
    }

    private class Remote
    {
        public NetworkKart Kart;
        public Rigidbody Body;
        public KartNetworkTransform Network;
        public HullPart[] Hull;

        // Golpe que se le mandó al otro kart y que todavía no se ve en lo que
        // manda su dueño: cambio de velocidad que le toca, velocidad que
        // mandaba en ese momento, y hasta cuándo se recuerda.
        public Vector3 HitVelocity;
        public Vector3 VelocityAtHit;
        public float HitTime;
        public float HitUntil;
    }

    private readonly List<Remote> remotes = new List<Remote>();

    private NetworkKart networkKart;

    private HullPart[] ownHull;

    private void Reset()
    {
        kart = GetComponent<KartBehaviour>();
        body = GetComponent<Rigidbody>();
        hull = FindHull(gameObject);
    }

    private void Awake()
    {
        networkKart = GetComponent<NetworkKart>();
        ownHull = ToParts(transform, hull);
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

    /// <summary>
    /// Golpe que manda la computadora del otro kart (lo trae NetworkKart):
    /// impulso en el mundo y punto en coordenadas de este kart.
    /// </summary>
    public void ReceiveHit(Vector3 impulse, Vector3 localPoint)
    {
        if (!kart.IsSimulated)
            return;

        Vector3 point = body.position + body.rotation * localPoint;
        Vector3 up = body.transform.up;

        // A la altura del centro de masa, igual que el golpe propio.
        point -= up * Vector3.Dot(point - body.worldCenterOfMass, up);

        body.AddForceAtPosition(impulse, point, ForceMode.Impulse);
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
            Hull = ToParts(other.transform, FindHull(other.gameObject))
        };

        if (remote.Body == null || remote.Network == null)
            return;

        // El kart propio deja de chocar físicamente con la copia.
        foreach (Collider own in hull)
        {
            foreach (HullPart theirs in remote.Hull)
            {
                if (own != null && theirs.Collider != null)
                    Physics.IgnoreCollision(own, theirs.Collider, true);
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
        float prediction = remote.Network.PredictionTime;
        Vector3 pendingHit = GetPendingHit(remote);
        Vector3 remoteVelocity = remote.Kart.OwnerVelocity + pendingHit;
        Vector3 predictedPosition = remote.Network.OwnerPosition + remoteVelocity * prediction;
        Quaternion predictedRotation = remote.Network.PredictRotation(remote.Kart.OwnerAngularVelocity, prediction);

        // Desde el golpe, el otro kart ya se viene moviendo con la velocidad
        // que se le dio, aunque su dueño todavía no lo haya mandado.
        if (pendingHit != Vector3.zero)
            predictedPosition += pendingHit * (Time.time - remote.HitTime);

        if ((predictedPosition - body.position).sqrMagnitude > CheckDistance * CheckDistance)
            return;

        // Las cajas se ubican con la pose del Rigidbody (la de este paso de
        // física) y no con la del Transform: con la interpolación prendida, el
        // Transform tiene la pose dibujada, que va un paso atrasada.
        Vector3 ownPosition = body.position;
        Quaternion ownRotation = body.rotation;

        float deepest = 0f;
        Vector3 normal = Vector3.zero;
        Vector3 point = Vector3.zero;

        foreach (HullPart own in ownHull)
        {
            if (own.Collider == null || !own.Collider.enabled)
                continue;

            Vector3 ownPartPosition = ownPosition + ownRotation * own.Position;
            Quaternion ownPartRotation = ownRotation * own.Rotation;

            foreach (HullPart theirs in remote.Hull)
            {
                if (theirs.Collider == null || !theirs.Collider.enabled)
                    continue;

                Vector3 theirPartPosition = predictedPosition + predictedRotation * theirs.Position;
                Quaternion theirPartRotation = predictedRotation * theirs.Rotation;

                if (!Physics.ComputePenetration(
                        own.Collider, ownPartPosition, ownPartRotation,
                        theirs.Collider, theirPartPosition, theirPartRotation,
                        out Vector3 direction, out float distance))
                {
                    continue;
                }

                if (distance > deepest)
                {
                    deepest = distance;
                    normal = direction;
                    point = Physics.ClosestPoint(
                        predictedPosition + predictedRotation * theirs.Center,
                        own.Collider, ownPartPosition, ownPartRotation);
                }
            }
        }

        if (deepest <= 0f)
            return;

        // Los karts se empujan de costado o de frente, no hacia arriba: las
        // cajas son bajas y la salida más corta muchas veces sería vertical,
        // y eso subiría un kart arriba del otro.
        Vector3 up = ownRotation * Vector3.up;
        normal = Vector3.ProjectOnPlane(normal, up);

        if (normal.sqrMagnitude < 0.01f)
            normal = Vector3.ProjectOnPlane(body.position - predictedPosition, up);

        if (normal.sqrMagnitude < 1e-6f)
            return;

        normal.Normalize();

        // Con mucha superposición, la salida más corta puede quedar del otro
        // lado: el golpe siempre separa, del otro kart hacia el propio.
        if (Vector3.Dot(normal, body.worldCenterOfMass - predictedPosition) < 0f)
            normal = -normal;

        // El golpe se aplica a la altura del centro de masa: hace girar al kart
        // (un toque de costado lo desvía) pero no lo levanta ni lo vuelca.
        point -= up * Vector3.Dot(point - body.worldCenterOfMass, up);

        if (IsResolver(remote))
        {
            float impulse = ApplyImpulse(normal, point, remoteVelocity, remote.Body.mass);

            if (impulse > 0f)
            {
                // Al otro le toca el golpe contrario: se lo manda a su
                // computadora, con el punto en coordenadas de su kart.
                Vector3 localPoint = Quaternion.Inverse(predictedRotation) * (point - predictedPosition);
                remote.Kart.SendHit(-normal * impulse, localPoint);

                // Acá recién se va a ver cuando llegue su próxima velocidad.
                // Hasta entonces se lo considera ya golpeado: si no, cada
                // paso lo vería igual que antes y se lo volvería a golpear.
                if (Time.time >= remote.HitUntil)
                {
                    remote.HitVelocity = Vector3.zero;
                    remote.VelocityAtHit = remote.Kart.OwnerVelocity;
                    remote.HitTime = Time.time;
                }

                remote.HitVelocity -= normal * impulse / Mathf.Max(1f, remote.Body.mass);
                remote.HitUntil = Time.time + MaxHitMemory;
            }
        }

        float correction = Mathf.Min((deepest - overlapSlop) * overlapCorrection, maxCorrectionPerStep);

        if (correction > 0f)
            body.position += normal * correction;
    }

    /// <summary>De cada par de karts resuelve el choque siempre el mismo: el de NetworkObjectId más bajo.</summary>
    private bool IsResolver(Remote remote)
    {
        return networkKart != null && networkKart.NetworkObjectId < remote.Kart.NetworkObjectId;
    }

    /// <summary>
    /// Golpe entre los dos karts (la rotación del otro la resuelve su
    /// computadora): aplica la parte del kart propio y devuelve el impulso.
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
    /// Golpe mandado al otro kart que todavía no se ve en lo que manda su
    /// dueño. Se olvida cuando su velocidad ya cambió en esa dirección o
    /// después de MaxHitMemory.
    /// </summary>
    private static Vector3 GetPendingHit(Remote remote)
    {
        if (Time.time >= remote.HitUntil)
            return Vector3.zero;

        float expected = remote.HitVelocity.magnitude;

        if (expected < 0.01f)
            return Vector3.zero;

        Vector3 direction = remote.HitVelocity / expected;
        float received = Vector3.Dot(remote.Kart.OwnerVelocity - remote.VelocityAtHit, direction);

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

    /// <summary>
    /// Posición y rotación de cada caja respecto del kart (las cajas no se
    /// mueven dentro del kart), y su centro respecto del kart.
    /// </summary>
    private static HullPart[] ToParts(Transform root, Collider[] colliders)
    {
        HullPart[] parts = new HullPart[colliders.Length];
        Quaternion inverseRoot = Quaternion.Inverse(root.rotation);

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];

            if (collider == null)
                continue;

            Transform t = collider.transform;
            Vector3 localCenter = Vector3.zero;

            if (collider is BoxCollider box)
                localCenter = box.center;
            else if (collider is SphereCollider sphere)
                localCenter = sphere.center;
            else if (collider is CapsuleCollider capsule)
                localCenter = capsule.center;

            parts[i] = new HullPart
            {
                Collider = collider,
                Position = inverseRoot * (t.position - root.position),
                Rotation = inverseRoot * t.rotation,
                Center = inverseRoot * (t.TransformPoint(localCenter) - root.position)
            };
        }

        return parts;
    }
}
