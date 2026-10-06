using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Estado de la carrera: espera de jugadores, cuenta regresiva, carrera y fin.
/// Lo decide el servidor (el host) y se replica a todos:
/// - La largada es una hora del servidor (NetworkManager.ServerTime). Cada
///   computadora cuenta hacia esa hora, así todos arrancan juntos aunque el
///   aviso les llegue con atraso.
/// - Los participantes son los karts de red que van apareciendo
///   (NetworkKart.Spawned), en todas las computadoras.
/// - Cada computadora habilita los mandos solo de los karts que simula (el
///   propio, y en el host también los bots) y los apaga cuando ese kart
///   termina.
/// Un jugador es lo mismo con un host local y un solo kart.
/// </summary>
public class RaceManager : NetworkBehaviour
{
    public enum RaceState
    {
        Waiting,
        Countdown,
        Racing,
        Finished
    }

    [Header("Race")]
    [SerializeField, Min(0f)] private float countdownDuration = 3f;

    [SerializeField] private bool startAutomatically = true;

    [Header("Jugadores")]
    [Tooltip("Cuántos karts esperar antes de la cuenta regresiva. Lo pone la sesión (o el arranque de red en el editor).")]
    [SerializeField, Min(1)] private int expectedPlayers = 1;

    [Tooltip("Si no llegan todos, se larga igual después de este tiempo (s).")]
    [SerializeField, Min(0f)] private float maxWaitForPlayers = 15f;

    [Header("Systems")]
    [SerializeField] private RaceCheckpointManager checkpointManager;

    [SerializeField] private RaceLapManager lapManager;

    [SerializeField] private RacePositionManager positionManager;

    [SerializeField] private RaceTrackLimitsController trackLimitsController;

    [SerializeField] private RaceTimingManager timingManager;

    private readonly NetworkVariable<RaceState> networkState = new NetworkVariable<RaceState>(RaceState.Waiting);

    private readonly NetworkVariable<double> startServerTime = new NetworkVariable<double>(0.0);

    private readonly List<KartBehaviour> raceKarts = new List<KartBehaviour>();

    // Servidor: jugadores con la carrera cargada y su kart listo.
    private readonly HashSet<ulong> readyClients = new HashSet<ulong>();

    private RaceState currentState = RaceState.Waiting;
    private float countdownTimer;
    private float waitingSince;
    private bool reportedReady;

    public RaceState CurrentState => currentState;

    public float CountdownTimer => countdownTimer;

    /// <summary>Los karts que corren esta carrera (en esta computadora: todos, propios y ajenos).</summary>
    public IReadOnlyList<KartBehaviour> RaceKarts => raceKarts;

    /// <summary>Hora del servidor en que se larga.</summary>
    public double StartServerTime => startServerTime.Value;

    public event Action<RaceState> StateChanged;

    public event Action RaceStarted;

    public event Action RaceFinished;

    private void OnEnable()
    {
        NetworkKart.Spawned += HandleKartSpawned;
        NetworkKart.Despawned += HandleKartDespawned;

        if (lapManager != null)
            lapManager.KartFinished += HandleLapFinished;

        if (timingManager != null)
            timingManager.KartFinished += HandleKartFinished;
    }

    private void OnDisable()
    {
        NetworkKart.Spawned -= HandleKartSpawned;
        NetworkKart.Despawned -= HandleKartDespawned;

        if (lapManager != null)
            lapManager.KartFinished -= HandleLapFinished;

        if (timingManager != null)
            timingManager.KartFinished -= HandleKartFinished;
    }

    private void Start()
    {
        ValidateReferences();

        // Karts que aparecieron antes que este componente.
        foreach (NetworkKart kart in FindObjectsByType<NetworkKart>(FindObjectsSortMode.None))
        {
            if (kart.IsSpawned)
                HandleKartSpawned(kart);
        }
    }

    public override void OnNetworkSpawn()
    {
        networkState.OnValueChanged += HandleNetworkStateChanged;

        waitingSince = Time.time;

        ApplyNetworkState(networkState.Value);
        TryReportReady();
    }

    public override void OnNetworkDespawn()
    {
        networkState.OnValueChanged -= HandleNetworkStateChanged;
    }

    /// <summary>Cuántos jugadores esperar antes de largar (lo pone la sesión).</summary>
    public void SetExpectedPlayers(int count)
    {
        expectedPlayers = Mathf.Max(1, count);
    }

    private void Update()
    {
        if (!IsSpawned)
            return;

        if (IsServer && currentState == RaceState.Waiting && startAutomatically && IsReadyToStart())
            BeginCountdown();

        if (currentState == RaceState.Countdown)
        {
            countdownTimer = Mathf.Max(0f, (float)(startServerTime.Value - NetworkManager.ServerTime.Time));

            if (countdownTimer <= 0f)
                StartRaceLocally();
        }
    }

    public void BeginCountdown()
    {
        if (!IsServer || currentState != RaceState.Waiting)
            return;

        startServerTime.Value = NetworkManager.ServerTime.Time + countdownDuration;
        networkState.Value = RaceState.Countdown;

        ApplyNetworkState(RaceState.Countdown);
    }

    /// <summary>Termina la carrera para todos. Solo el servidor.</summary>
    public void FinishRace()
    {
        if (!IsServer || currentState == RaceState.Finished)
            return;

        networkState.Value = RaceState.Finished;

        FinishLocally();
    }

    public bool IsWaiting() => currentState == RaceState.Waiting;

    public bool IsCountdown() => currentState == RaceState.Countdown;

    public bool IsRacing() => currentState == RaceState.Racing;

    public bool IsFinished() => currentState == RaceState.Finished;

    /// <summary>
    /// Se larga cuando todos los jugadores avisaron que tienen la carrera
    /// cargada (si no, alguno se perdería la cuenta regresiva), o al pasar
    /// maxWaitForPlayers.
    /// </summary>
    private bool IsReadyToStart()
    {
        if (raceKarts.Count == 0)
            return false;

        return readyClients.Count >= expectedPlayers || Time.time - waitingSince >= maxWaitForPlayers;
    }

    /// <summary>
    /// Cada computadora avisa al servidor cuando ya tiene la escena cargada y
    /// su kart en pista (el host también, a sí mismo).
    /// </summary>
    private void TryReportReady()
    {
        if (reportedReady || !IsSpawned)
            return;

        foreach (KartBehaviour kart in raceKarts)
        {
            NetworkKart networkKart = kart != null ? kart.GetComponent<NetworkKart>() : null;

            if (networkKart != null && networkKart.IsLocalPlayer)
            {
                reportedReady = true;
                ReportReadyRpc();
                return;
            }
        }
    }

    [Rpc(SendTo.Server, RequireOwnership = false)]
    private void ReportReadyRpc(RpcParams rpcParams = default)
    {
        readyClients.Add(rpcParams.Receive.SenderClientId);
    }

    private void HandleNetworkStateChanged(RaceState previous, RaceState current)
    {
        ApplyNetworkState(current);
    }

    private void ApplyNetworkState(RaceState state)
    {
        switch (state)
        {
            case RaceState.Countdown:
                if (currentState == RaceState.Waiting)
                {
                    SetSimulatedKartsInput(false);
                    ChangeState(RaceState.Countdown);
                }
                break;

            case RaceState.Racing:
                StartRaceLocally();
                break;

            case RaceState.Finished:
                FinishLocally();
                break;

            default:
                SetSimulatedKartsInput(false);
                ChangeState(state);
                break;
        }
    }

    /// <summary>
    /// Largada en esta computadora: cuando llega la hora del servidor (o el
    /// aviso del servidor, si llegó antes de que corriera la cuenta).
    /// </summary>
    private void StartRaceLocally()
    {
        if (currentState == RaceState.Racing || currentState == RaceState.Finished)
            return;

        countdownTimer = 0f;

        SetSimulatedKartsInput(true);
        ChangeState(RaceState.Racing);

        RaceStarted?.Invoke();

        if (IsServer)
            networkState.Value = RaceState.Racing;
    }

    private void FinishLocally()
    {
        if (currentState == RaceState.Finished)
            return;

        SetSimulatedKartsInput(false);
        ChangeState(RaceState.Finished);

        RaceFinished?.Invoke();
    }

    private void HandleKartSpawned(NetworkKart networkKart)
    {
        KartBehaviour kart = networkKart != null ? networkKart.Kart : null;

        if (kart == null || raceKarts.Contains(kart))
            return;

        raceKarts.Add(kart);

        Rigidbody body = kart.GetComponent<Rigidbody>();

        // Vueltas y posiciones las cuenta solo el servidor.
        if (NetworkRole.IsAuthority)
        {
            if (lapManager != null)
                lapManager.RegisterKart(body);

            if (positionManager != null)
                positionManager.RegisterKart(body);
        }

        // Los límites los juzga cada computadora en sus karts simulados.
        if (trackLimitsController != null)
            trackLimitsController.RegisterKart(kart);

        // Llegó con la carrera en marcha: si es propio, que pueda manejar.
        if (currentState == RaceState.Racing)
            SetInput(kart, true);

        TryReportReady();
    }

    private void HandleKartDespawned(NetworkKart networkKart)
    {
        KartBehaviour kart = networkKart != null ? networkKart.Kart : null;

        if (kart == null || !raceKarts.Remove(kart))
            return;

        Rigidbody body = kart.GetComponent<Rigidbody>();

        if (lapManager != null)
            lapManager.UnregisterKart(body);

        if (positionManager != null)
            positionManager.UnregisterKart(body);

        if (trackLimitsController != null)
            trackLimitsController.UnregisterKart(kart);

        if (IsServer)
            readyClients.Remove(networkKart.OwnerClientId);

        if (IsServer && currentState == RaceState.Racing && AllKartsFinished())
            FinishRace();
    }

    /// <summary>Servidor: un kart cruzó la meta por última vez.</summary>
    private void HandleLapFinished(Rigidbody kart)
    {
        if (IsServer && AllKartsFinished())
            FinishRace();
    }

    /// <summary>Todas las computadoras: el que termina deja de manejar y frena solo (ver KartBehaviour).</summary>
    private void HandleKartFinished(Rigidbody body)
    {
        KartBehaviour kart = body != null ? body.GetComponent<KartBehaviour>() : null;

        if (kart != null && kart.IsSimulated)
            kart.SetInputEnabled(false);
    }

    private bool AllKartsFinished()
    {
        if (raceKarts.Count == 0)
            return false;

        foreach (KartBehaviour kart in raceKarts)
        {
            if (kart != null && !lapManager.IsFinished(kart.GetComponent<Rigidbody>()))
                return false;
        }

        return true;
    }

    private void SetSimulatedKartsInput(bool enabled)
    {
        foreach (KartBehaviour kart in raceKarts)
            SetInput(kart, enabled);
    }

    /// <summary>Solo toca los karts que corren su física acá; a los que ya terminaron no los vuelve a habilitar.</summary>
    private void SetInput(KartBehaviour kart, bool enabled)
    {
        if (kart == null || !kart.IsSimulated)
            return;

        if (enabled && timingManager != null && timingManager.IsFinished(kart.GetComponent<Rigidbody>()))
            return;

        kart.SetInputEnabled(enabled);
    }

    private void ChangeState(RaceState newState)
    {
        if (currentState == newState)
            return;

        currentState = newState;

        Debug.Log($"Race State → {currentState}", this);

        StateChanged?.Invoke(currentState);
    }

    private void ValidateReferences()
    {
        if (checkpointManager == null || lapManager == null || positionManager == null ||
            trackLimitsController == null || timingManager == null)
        {
            Debug.LogError("RaceManager necesita el RaceCheckpointManager, RaceLapManager, RacePositionManager, RaceTrackLimitsController y RaceTimingManager.", this);
        }
    }
}
