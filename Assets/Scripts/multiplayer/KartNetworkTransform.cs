using System;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// NetworkTransform del kart: el de Netcode, más lo último que mandó el dueño
/// sin interpolar (posición, rotación y cuándo llegó), en las computadoras que
/// ven una copia del kart.
/// - El anti-trampa (KartMovementValidator, en el servidor) mide con eso y no
///   con la copia interpolada: después de una traba de red, la copia se queda
///   quieta y se pone al día de golpe, y parecería que el kart saltó.
/// - Las copias se dibujan donde está el kart ahora y no donde estaba hace un
///   rato: lo último que mandó el dueño, adelantado por la latencia con la
///   velocidad que mandó (como hacen los juegos de carreras en red). La copia
///   interpolada de Netcode va 0,1–0,3 s atrasada: a 100 km/h son 3 a 8 m, y
///   el que iba adelante recibía el golpe de un kart que en su pantalla
///   estaba lejos, atrás.
/// - Los choques en red (KartRemoteContacts) usan esa misma estimación, así
///   que el choque pasa donde se ve.
/// </summary>
public class KartNetworkTransform : NetworkTransform
{
    [Header("Copias: dónde se dibujan")]
    [Tooltip("Dibujar las copias donde está el kart ahora (lo último que mandó el dueño, adelantado por " +
             "la latencia) en vez de donde estaba hace un rato. Apagado = interpolado de Netcode.")]
    [SerializeField] private bool predictCopies = true;

    [Tooltip("Máximo que se adelanta lo último que mandó el dueño (s). Con más latencia que esto, la copia se ve atrasada.")]
    [SerializeField, Min(0f)] private float maxPrediction = 0.25f;

    [Tooltip("Qué tan rápido se corrige la diferencia cuando llega un estado nuevo (1/s). " +
             "Más alto = más pegado a lo que manda el dueño, pero tiembla más.")]
    [SerializeField, Min(0.1f)] private float correctionSharpness = 8f;

    [Tooltip("Si la copia queda más lejos que esto de donde debería estar (m), salta directo (respawn, choque fuerte).")]
    [SerializeField, Min(0.1f)] private float snapDistance = 4f;

    /// <summary>
    /// Solo en el servidor, para karts que mueve otra computadora: posición
    /// del dueño, tiempo de red en que la simuló (s) y si fue un teleport.
    /// </summary>
    public event Action<KartNetworkTransform, Vector3, double, bool> OwnerStateReceived;

    // Llegan solo los ejes que cambiaron: se arma la posición completa acá.
    private Vector3 ownerPosition;
    private Quaternion ownerRotation = Quaternion.identity;
    private float receivedAt;

    private NetworkKart networkKart;
    private Rigidbody body;

    // Dónde se dibuja la copia.
    private Vector3 drawnPosition;
    private Quaternion drawnRotation = Quaternion.identity;
    private bool hasDrawnPose;
    private bool snapNext;

    /// <summary>True si ya llegó algún estado del dueño (solo en las copias).</summary>
    public bool HasOwnerState { get; private set; }

    /// <summary>Última posición que mandó el dueño, sin interpolar.</summary>
    public Vector3 OwnerPosition => ownerPosition;

    /// <summary>Última rotación que mandó el dueño, sin interpolar.</summary>
    public Quaternion OwnerRotation => ownerRotation;

    /// <summary>Hace cuánto llegó el último estado del dueño (s).</summary>
    public float OwnerStateAge => Time.time - receivedAt;

    /// <summary>
    /// Cuánto adelantar lo último que mandó el dueño para estimar dónde está
    /// ahora (s): lo que tardó en llegar (del dueño al servidor y del servidor
    /// acá) más el tiempo desde que llegó.
    /// </summary>
    public float PredictionTime
    {
        get
        {
            NetworkManager manager = NetworkManager;
            float travel = 0f;

            if (manager != null && manager.IsListening)
            {
                NetworkTransport transport = manager.NetworkConfig.NetworkTransport;

                if (manager.IsServer)
                {
                    // Del dueño al servidor: media ida y vuelta.
                    travel = transport.GetCurrentRtt(OwnerClientId) * 0.0005f;
                }
                else
                {
                    float mine = transport.GetCurrentRtt(NetworkManager.ServerClientId) * 0.0005f;

                    // Si el dueño es otro cliente, su tramo hasta el servidor se
                    // estima igual al propio.
                    travel = OwnerClientId == NetworkManager.ServerClientId ? mine : mine * 2f;
                }
            }

            return Mathf.Min(travel + OwnerStateAge, maxPrediction);
        }
    }

    /// <summary>Rotación del dueño adelantada un tiempo con una velocidad de giro (rad/s, en el mundo).</summary>
    public Quaternion PredictRotation(Vector3 angularVelocity, float time)
    {
        return Rotate(ownerRotation, angularVelocity, time);
    }

    protected override void Awake()
    {
        base.Awake();

        networkKart = GetComponent<NetworkKart>();
        body = GetComponent<Rigidbody>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        ownerPosition = transform.position;
        ownerRotation = transform.rotation;
        HasOwnerState = false;
        hasDrawnPose = false;
    }

    protected override void OnNetworkTransformStateUpdated(ref NetworkTransformState oldState, ref NetworkTransformState newState)
    {
        base.OnNetworkTransformStateUpdated(ref oldState, ref newState);

        if (CanCommitToTransform)
            return;

        bool teleport = newState.IsTeleportingNextFrame;

        // La rotación llega entera (sincronización por quaternion).
        if (newState.HasRotAngleChange && newState.QuaternionSync)
            ownerRotation = newState.GetRotation();

        if (teleport)
            snapNext = true;

        if (!teleport && !newState.HasPositionChange)
            return;

        Vector3 received = newState.GetPosition();

        if (teleport || newState.HasPositionX)
            ownerPosition.x = received.x;

        if (teleport || newState.HasPositionY)
            ownerPosition.y = received.y;

        if (teleport || newState.HasPositionZ)
            ownerPosition.z = received.z;

        double time = newState.GetNetworkTick() / (double)NetworkManager.NetworkConfig.TickRate;
        receivedAt = Time.time;
        HasOwnerState = true;

        if (IsServer)
            OwnerStateReceived?.Invoke(this, ownerPosition, time, teleport);
    }

    /// <summary>
    /// En las copias, Netcode mueve el Rigidbody (cinemático) a la posición
    /// interpolada. Después de eso se lo mueve a donde está el kart ahora.
    /// </summary>
    public override void OnFixedUpdate()
    {
        base.OnFixedUpdate();

        if (!predictCopies || !IsSpawned || CanCommitToTransform || !HasOwnerState ||
            networkKart == null || body == null)
        {
            return;
        }

        float deltaTime = Time.fixedDeltaTime;
        float prediction = PredictionTime;
        Vector3 velocity = networkKart.OwnerVelocity;
        Vector3 angularVelocity = networkKart.OwnerAngularVelocity;

        Vector3 targetPosition = ownerPosition + velocity * prediction;
        Quaternion targetRotation = Rotate(ownerRotation, angularVelocity, prediction);

        if (!hasDrawnPose || snapNext ||
            (drawnPosition - targetPosition).sqrMagnitude > snapDistance * snapDistance)
        {
            drawnPosition = targetPosition;
            drawnRotation = targetRotation;
            hasDrawnPose = true;
            snapNext = false;
        }
        else
        {
            // La copia sigue andando con su velocidad, y la diferencia con lo
            // que manda el dueño se va borrando de a poco: no salta cuando
            // llega un estado nuevo y no se queda atrás en las rectas.
            Vector3 advancedPosition = drawnPosition + velocity * deltaTime;
            Quaternion advancedRotation = Rotate(drawnRotation, angularVelocity, deltaTime);
            float keep = Mathf.Exp(-correctionSharpness * deltaTime);

            drawnPosition = targetPosition + (advancedPosition - targetPosition) * keep;
            drawnRotation = Quaternion.Slerp(targetRotation, advancedRotation, keep);
        }

        body.MovePosition(drawnPosition);
        body.MoveRotation(drawnRotation);
    }

    private static Quaternion Rotate(Quaternion rotation, Vector3 angularVelocity, float time)
    {
        float angle = angularVelocity.magnitude * time * Mathf.Rad2Deg;

        if (angle < 1e-4f)
            return rotation;

        return Quaternion.AngleAxis(angle, angularVelocity.normalized) * rotation;
    }
}
