using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Kart en red (Netcode for GameObjects). Cada jugador simula su propio kart
/// con la física de siempre, y en las demás computadoras se ve una copia:
/// - Posición y rotación: NetworkTransform + NetworkRigidbody con autoridad del
///   dueño. En las copias el Rigidbody queda cinemático e interpolado.
/// - Piezas: el dueño instala las que eligió en el Catálogo
///   (KartLoadoutStore), publica sus partID y cada computadora arma el mismo
///   kart con el PartCatalog.
/// - Motor, volante y velocidad: el dueño publica rpm, acelerador, ángulo de
///   las ruedas (para el sonido y las ruedas de las copias) y la velocidad
///   del kart (para los choques en red).
/// - Choques: la computadora que resuelve un choque le manda al dueño del otro
///   kart su parte del golpe (SendHit), pasando por el servidor, que lo valida.
/// </summary>
[RequireComponent(typeof(KartBehaviour))]
public class NetworkKart : NetworkBehaviour
{
    [SerializeField] private KartBehaviour kart;

    [SerializeField] private KartConfigurationController configurationController;

    [Tooltip("Catálogo con todas las piezas: con él se traducen los partID.")]
    [SerializeField] private PartCatalog catalog;

    [Tooltip("Control del jugador: solo queda activo en el kart propio.")]
    [SerializeField] private PlayerKartInput playerInput;

    /// <summary>Piezas instaladas, por partID.</summary>
    public struct Loadout : INetworkSerializable, IEquatable<Loadout>
    {
        public FixedString64Bytes Engine;
        public FixedString64Bytes Chassis;
        public FixedString64Bytes Wheels;
        public FixedString64Bytes AeroKit;
        public FixedString64Bytes SteeringWheel;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Engine);
            serializer.SerializeValue(ref Chassis);
            serializer.SerializeValue(ref Wheels);
            serializer.SerializeValue(ref AeroKit);
            serializer.SerializeValue(ref SteeringWheel);
        }

        public bool Equals(Loadout other)
        {
            return Engine.Equals(other.Engine)
                && Chassis.Equals(other.Chassis)
                && Wheels.Equals(other.Wheels)
                && AeroKit.Equals(other.AeroKit)
                && SteeringWheel.Equals(other.SteeringWheel);
        }
    }

    /// <summary>
    /// Lo que hace falta para que la copia suene y doble como el original.
    /// Va redondeado: así solo se manda cuando cambia de verdad.
    /// </summary>
    public struct DriveState : INetworkSerializable, IEquatable<DriveState>
    {
        public ushort Rpm;          // rpm, de a 10
        public byte Throttle;       // 0 a 255
        public short SteerAngle;    // décimas de grado
        public short VelocityX;     // cm/s
        public short VelocityY;
        public short VelocityZ;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Rpm);
            serializer.SerializeValue(ref Throttle);
            serializer.SerializeValue(ref SteerAngle);
            serializer.SerializeValue(ref VelocityX);
            serializer.SerializeValue(ref VelocityY);
            serializer.SerializeValue(ref VelocityZ);
        }

        public bool Equals(DriveState other)
        {
            return Rpm == other.Rpm && Throttle == other.Throttle && SteerAngle == other.SteerAngle &&
                VelocityX == other.VelocityX && VelocityY == other.VelocityY && VelocityZ == other.VelocityZ;
        }
    }

    private readonly NetworkVariable<Loadout> loadout = new NetworkVariable<Loadout>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<DriveState> driveState = new NetworkVariable<DriveState>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    // Nombre que eligió el jugador en el menú (lo escribe él).
    private readonly NetworkVariable<FixedString32Bytes> playerName = new NetworkVariable<FixedString32Bytes>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    /// <summary>Apareció el kart del jugador de esta computadora.</summary>
    public static event Action<NetworkKart> LocalKartSpawned;

    /// <summary>Apareció un kart cualquiera (propio, de otro jugador o bot), en todas las computadoras.</summary>
    public static event Action<NetworkKart> Spawned;

    /// <summary>Un kart se fue de la partida.</summary>
    public static event Action<NetworkKart> Despawned;

    // Golpe más fuerte que se acepta de otra computadora: frenar en seco un
    // kart a 150 km/h (N·s por kg de masa).
    private const float MaxHitSpeedChange = 42f;

    // Distancia máxima entre los karts para aceptar un golpe (m).
    private const float MaxHitDistance = 6f;

    public KartBehaviour Kart => kart;

    /// <summary>
    /// Velocidad del kart según su dueño (m/s). En las copias es la que manda
    /// el dueño: la real de su física, no la que se deduce de las posiciones
    /// (que incluyen los empujones para separar karts superpuestos).
    /// </summary>
    public Vector3 OwnerVelocity { get; private set; }

    /// <summary>Nombre para mostrar: el que eligió el jugador o, si no hay, su número.</summary>
    public string DisplayName
    {
        get
        {
            if (!playerName.Value.IsEmpty)
                return playerName.Value.ToString().ToUpperInvariant();

            return NetworkObject != null && NetworkObject.IsPlayerObject ? $"JUGADOR {OwnerClientId + 1}" : name;
        }
    }

    private void Reset()
    {
        kart = GetComponent<KartBehaviour>();
        configurationController = GetComponentInChildren<KartConfigurationController>();
        playerInput = GetComponent<PlayerKartInput>();
    }

    public override void OnNetworkSpawn()
    {
        if (kart == null || configurationController == null || catalog == null)
        {
            Debug.LogError("NetworkKart necesita el KartBehaviour, el KartConfigurationController y el PartCatalog.", this);
            return;
        }

        // El dueño simula el kart (en el host, también los bots). El teclado
        // solo maneja el kart del jugador de esta computadora.
        kart.SetSimulated(IsOwner);

        if (playerInput != null)
            playerInput.enabled = IsLocalPlayer;

        if (IsOwner)
        {
            // El jugador corre con las piezas que eligió en el Catálogo.
            if (IsLocalPlayer)
                KartLoadoutStore.Apply(configurationController, catalog);

            loadout.Value = ReadLoadout();
        }
        else
        {
            ApplyLoadout(loadout.Value);
            loadout.OnValueChanged += HandleLoadoutChanged;
        }

        if (IsLocalPlayer && GameSession.Instance != null)
            playerName.Value = new FixedString32Bytes(GameSession.Instance.LocalPlayerName);

        if (IsLocalPlayer)
            LocalKartSpawned?.Invoke(this);

        Spawned?.Invoke(this);
    }

    public override void OnNetworkDespawn()
    {
        loadout.OnValueChanged -= HandleLoadoutChanged;

        Despawned?.Invoke(this);
    }

    /// <summary>
    /// Otra computadora chocó contra este kart y le manda su parte del golpe:
    /// impulso en el mundo y punto en coordenadas de este kart. Pasa por el
    /// servidor, que lo valida, y llega al dueño (KartRemoteContacts).
    /// </summary>
    public void SendHit(Vector3 impulse, Vector3 localPoint)
    {
        if (IsSpawned)
            HitServerRpc(impulse, localPoint);
    }

    [Rpc(SendTo.Server, RequireOwnership = false)]
    private void HitServerRpc(Vector3 impulse, Vector3 localPoint, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;

        // Solo vale si el que lo manda tiene un kart al lado de este.
        if (sender == OwnerClientId || !HasKartNear(sender))
            return;

        Rigidbody body = kart.GetComponent<Rigidbody>();
        float maxImpulse = (body != null ? body.mass : 150f) * MaxHitSpeedChange;

        HitOwnerRpc(Vector3.ClampMagnitude(impulse, maxImpulse), Vector3.ClampMagnitude(localPoint, 3f));
    }

    [Rpc(SendTo.Owner)]
    private void HitOwnerRpc(Vector3 impulse, Vector3 localPoint)
    {
        KartRemoteContacts contacts = GetComponent<KartRemoteContacts>();

        if (contacts != null)
            contacts.ReceiveHit(impulse, localPoint);
    }

    private bool HasKartNear(ulong clientId)
    {
        foreach (NetworkKart other in FindObjectsByType<NetworkKart>(FindObjectsSortMode.None))
        {
            if (other != this && other.IsSpawned && other.OwnerClientId == clientId &&
                (other.transform.position - transform.position).sqrMagnitude <= MaxHitDistance * MaxHitDistance)
            {
                return true;
            }
        }

        return false;
    }

    private void Update()
    {
        if (!IsSpawned || kart == null)
            return;

        if (IsOwner)
            PublishDriveState();
        else
            ApplyDriveState();
    }

    private void PublishDriveState()
    {
        Rigidbody body = kart.GetComponent<Rigidbody>();
        Vector3 velocity = body != null ? body.linearVelocity : Vector3.zero;
        bool engine = kart.HasEngineState;

        OwnerVelocity = velocity;

        DriveState state = new DriveState
        {
            Rpm = engine ? (ushort)Mathf.Clamp(Mathf.RoundToInt(kart.EngineRpm / 10f), 0, ushort.MaxValue) : (ushort)0,
            Throttle = engine ? (byte)Mathf.RoundToInt(Mathf.Clamp01(kart.EngineThrottle) * 255f) : (byte)0,
            SteerAngle = (short)Mathf.Clamp(Mathf.RoundToInt(kart.SteerAngle * 10f), short.MinValue, short.MaxValue),
            VelocityX = ToCentimeters(velocity.x),
            VelocityY = ToCentimeters(velocity.y),
            VelocityZ = ToCentimeters(velocity.z)
        };

        if (!state.Equals(driveState.Value))
            driveState.Value = state;
    }

    private void ApplyDriveState()
    {
        DriveState state = driveState.Value;

        kart.SetRemoteState(state.Rpm * 10f, state.Throttle / 255f, state.SteerAngle / 10f);

        OwnerVelocity = new Vector3(state.VelocityX, state.VelocityY, state.VelocityZ) * 0.01f;
    }

    private static short ToCentimeters(float metersPerSecond)
    {
        return (short)Mathf.Clamp(Mathf.RoundToInt(metersPerSecond * 100f), short.MinValue, short.MaxValue);
    }

    private Loadout ReadLoadout()
    {
        return new Loadout
        {
            Engine = GetPartId(PartType.Engine),
            Chassis = GetPartId(PartType.Chassis),
            Wheels = GetPartId(PartType.Wheels),
            AeroKit = GetPartId(PartType.AeroKit),
            SteeringWheel = GetPartId(PartType.SteeringWheel)
        };
    }

    private FixedString64Bytes GetPartId(PartType type)
    {
        KartPart part = configurationController.GetInstalledPart(type);
        return part != null && !string.IsNullOrEmpty(part.partID) ? new FixedString64Bytes(part.partID) : default;
    }

    private void HandleLoadoutChanged(Loadout previous, Loadout current)
    {
        ApplyLoadout(current);
    }

    /// <summary>Instala en la copia las piezas que tiene el original.</summary>
    private void ApplyLoadout(Loadout value)
    {
        InstallPart(PartType.Engine, value.Engine);
        InstallPart(PartType.Chassis, value.Chassis);
        InstallPart(PartType.Wheels, value.Wheels);
        InstallPart(PartType.AeroKit, value.AeroKit);
        InstallPart(PartType.SteeringWheel, value.SteeringWheel);
    }

    private void InstallPart(PartType type, FixedString64Bytes partId)
    {
        if (partId.IsEmpty)
            return;

        string id = partId.ToString();
        KartPart installed = configurationController.GetInstalledPart(type);

        if (installed != null && installed.partID == id)
            return;

        KartPart part = catalog.GetPartByID(id);

        if (part == null)
        {
            Debug.LogWarning($"NetworkKart: el catálogo no tiene la pieza '{id}'.", this);
            return;
        }

        configurationController.InstallPart(part);
    }
}
