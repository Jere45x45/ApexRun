using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Kart en red (Netcode for GameObjects). Cada jugador simula su propio kart
/// con la física de siempre, y en las demás computadoras se ve una copia:
/// - Posición y rotación: NetworkTransform + NetworkRigidbody con autoridad del
///   dueño. En las copias el Rigidbody queda cinemático e interpolado.
/// - Piezas: el dueño publica los partID de su configuración y cada
///   computadora arma el mismo kart con el PartCatalog.
/// - Motor y volante: el dueño publica rpm, acelerador y ángulo de las ruedas,
///   para el sonido y las ruedas de las copias.
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

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Rpm);
            serializer.SerializeValue(ref Throttle);
            serializer.SerializeValue(ref SteerAngle);
        }

        public bool Equals(DriveState other)
        {
            return Rpm == other.Rpm && Throttle == other.Throttle && SteerAngle == other.SteerAngle;
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

    /// <summary>Apareció el kart del jugador de esta computadora.</summary>
    public static event Action<NetworkKart> LocalKartSpawned;

    public KartBehaviour Kart => kart;

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

        kart.SetSimulated(IsOwner);

        if (playerInput != null)
            playerInput.enabled = IsOwner;

        if (IsOwner)
        {
            loadout.Value = ReadLoadout();
            LocalKartSpawned?.Invoke(this);
        }
        else
        {
            ApplyLoadout(loadout.Value);
            loadout.OnValueChanged += HandleLoadoutChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        loadout.OnValueChanged -= HandleLoadoutChanged;
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
        if (!kart.HasEngineState)
            return;

        DriveState state = new DriveState
        {
            Rpm = (ushort)Mathf.Clamp(Mathf.RoundToInt(kart.EngineRpm / 10f), 0, ushort.MaxValue),
            Throttle = (byte)Mathf.RoundToInt(Mathf.Clamp01(kart.EngineThrottle) * 255f),
            SteerAngle = (short)Mathf.Clamp(Mathf.RoundToInt(kart.SteerAngle * 10f), short.MinValue, short.MaxValue)
        };

        if (!state.Equals(driveState.Value))
            driveState.Value = state;
    }

    private void ApplyDriveState()
    {
        DriveState state = driveState.Value;

        kart.SetRemoteState(state.Rpm * 10f, state.Throttle / 255f, state.SteerAngle / 10f);
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
