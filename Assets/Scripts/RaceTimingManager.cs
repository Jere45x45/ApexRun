using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Cronometraje y clasificación de la carrera, para todos los karts.
/// - En el servidor (el host) se mide todo: tiempo de carrera desde la
///   largada, última y mejor vuelta, llegada, penalizaciones, vuelta y
///   posición en pista. Los tiempos usan la hora del servidor.
/// - Cada décima de segundo el servidor publica el estado de cada kart en una
///   NetworkList, y los clientes leen de ahí.
/// - Tiempo final = tiempo + penalizaciones. Clasificación: primero los que
///   terminaron, por tiempo final; después los que siguen corriendo, por
///   posición en pista.
/// El HUD y la pantalla de resultados leen todo de acá, en el host y en los
/// clientes por igual.
/// </summary>
public class RaceTimingManager : NetworkBehaviour
{
    [SerializeField] private RaceManager raceManager;

    [SerializeField] private RaceLapManager lapManager;

    [SerializeField] private RacePenaltyManager penaltyManager;

    [SerializeField] private RacePositionManager positionManager;

    [SerializeField] private RaceCheckpointManager checkpointManager;

    [Tooltip("Cada cuánto el servidor publica el estado de la carrera (s).")]
    [SerializeField, Range(0.02f, 0.5f)] private float publishInterval = 0.1f;

    /// <summary>Resultado de un kart en la clasificación.</summary>
    public struct Result
    {
        public int Position;
        public Rigidbody Kart;
        public bool Finished;
        public int Lap;
        public float RaceTime;
        public float BestLap;
        public float Penalty;
        public float FinalTime;
    }

    /// <summary>Estado de un kart que el servidor publica para los clientes.</summary>
    public struct KartStatus : INetworkSerializeByMemcpy, IEquatable<KartStatus>
    {
        public ulong KartId;
        public int Position;
        public int Lap;
        public int LastCheckpoint;
        public byte Finished;
        public float FinishTime;
        public float LastLap;
        public float BestLap;
        public float Penalty;

        public bool Equals(KartStatus other)
        {
            return KartId == other.KartId
                && Position == other.Position
                && Lap == other.Lap
                && LastCheckpoint == other.LastCheckpoint
                && Finished == other.Finished
                && FinishTime == other.FinishTime
                && LastLap == other.LastLap
                && BestLap == other.BestLap
                && Penalty == other.Penalty;
        }
    }

    private class KartTiming
    {
        public double LapStart;
        public float LastLap = -1f;
        public float BestLap = -1f;
        public float FinishTime = -1f;
    }

    // Servidor: la medición de verdad.
    private readonly Dictionary<Rigidbody, KartTiming> timings = new Dictionary<Rigidbody, KartTiming>();

    // Lo que ven los clientes.
    private NetworkList<KartStatus> statuses;

    private readonly List<Result> classification = new List<Result>();
    private float nextPublish;

    /// <summary>
    /// Un kart cruzó la meta por última vez. En todas las computadoras: en el
    /// servidor al medirlo, en los clientes al llegar el estado.
    /// </summary>
    public event Action<Rigidbody> KartFinished;

    /// <summary>A un kart le aplicaron una penalización (segundos de esa penalización).</summary>
    public event Action<Rigidbody, float> PenaltyApplied;

    public bool HasStarted => raceManager != null &&
        (raceManager.CurrentState == RaceManager.RaceState.Racing || raceManager.CurrentState == RaceManager.RaceState.Finished);

    public int TotalLaps => lapManager != null ? lapManager.TotalLaps : 1;

    public int KartCount => raceManager != null ? raceManager.RaceKarts.Count : 0;

    private bool IsAuthority => NetworkRole.IsAuthority;

    private void Awake()
    {
        statuses = new NetworkList<KartStatus>();
    }

    private void OnEnable()
    {
        if (raceManager != null)
            raceManager.RaceStarted += HandleRaceStarted;

        if (lapManager != null)
        {
            lapManager.LapCompleted += HandleLapCompleted;
            lapManager.KartFinished += HandleKartFinished;
        }

        if (penaltyManager != null)
            penaltyManager.PenaltyApplied += HandlePenaltyApplied;
    }

    private void OnDisable()
    {
        if (raceManager != null)
            raceManager.RaceStarted -= HandleRaceStarted;

        if (lapManager != null)
        {
            lapManager.LapCompleted -= HandleLapCompleted;
            lapManager.KartFinished -= HandleKartFinished;
        }

        if (penaltyManager != null)
            penaltyManager.PenaltyApplied -= HandlePenaltyApplied;
    }

    private void Start()
    {
        if (raceManager == null || lapManager == null || penaltyManager == null || positionManager == null || checkpointManager == null)
        {
            Debug.LogError("RaceTimingManager necesita el RaceManager, RaceLapManager, RacePenaltyManager, RacePositionManager y RaceCheckpointManager.", this);
            enabled = false;
        }
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
            statuses.OnListChanged += HandleStatusesChanged;
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer)
            statuses.OnListChanged -= HandleStatusesChanged;
    }

    private void Update()
    {
        if (!IsSpawned || !IsServer || Time.unscaledTime < nextPublish)
            return;

        nextPublish = Time.unscaledTime + publishInterval;
        PublishStatuses();
    }

    // ───────────── Consultas (host y clientes) ─────────────

    /// <summary>Tiempo de carrera del kart: hasta que cruzó la meta, o el actual si sigue corriendo.</summary>
    public float GetRaceTime(Rigidbody kart)
    {
        if (!HasStarted)
            return 0f;

        float finishTime = GetFinishTime(kart);

        if (finishTime >= 0f)
            return finishTime;

        return Mathf.Max(0f, (float)(ServerNow - raceManager.StartServerTime));
    }

    /// <summary>Última vuelta en segundos, -1 si todavía no completó ninguna.</summary>
    public float GetLastLap(Rigidbody kart)
    {
        if (IsAuthority)
            return TryGetTiming(kart, out KartTiming timing) ? timing.LastLap : -1f;

        return TryGetStatus(kart, out KartStatus status) ? status.LastLap : -1f;
    }

    /// <summary>Mejor vuelta en segundos, -1 si todavía no completó ninguna.</summary>
    public float GetBestLap(Rigidbody kart)
    {
        if (IsAuthority)
            return TryGetTiming(kart, out KartTiming timing) ? timing.BestLap : -1f;

        return TryGetStatus(kart, out KartStatus status) ? status.BestLap : -1f;
    }

    public bool IsFinished(Rigidbody kart)
    {
        return GetFinishTime(kart) >= 0f;
    }

    public float GetPenalty(Rigidbody kart)
    {
        if (IsAuthority)
            return penaltyManager != null ? penaltyManager.GetPenaltyTime(kart) : 0f;

        return TryGetStatus(kart, out KartStatus status) ? status.Penalty : 0f;
    }

    /// <summary>Vuelta en la que va (empieza en 1).</summary>
    public int GetLap(Rigidbody kart)
    {
        if (IsAuthority)
            return lapManager.GetCurrentLap(kart);

        return TryGetStatus(kart, out KartStatus status) ? status.Lap : 1;
    }

    /// <summary>Posición en pista (1 = primero), 0 si todavía no se sabe.</summary>
    public int GetPosition(Rigidbody kart)
    {
        if (IsAuthority)
            return positionManager.GetPosition(kart);

        return TryGetStatus(kart, out KartStatus status) ? status.Position : 0;
    }

    /// <summary>Último checkpoint que pasó, -1 si ninguno (para el respawn).</summary>
    public int GetLastCheckpoint(Rigidbody kart)
    {
        if (IsAuthority)
            return checkpointManager.GetLastCheckpoint(kart);

        return TryGetStatus(kart, out KartStatus status) ? status.LastCheckpoint : -1;
    }

    /// <summary>
    /// Clasificación actual de todos los karts de la carrera. La lista se
    /// reutiliza: no guardarla entre llamadas.
    /// </summary>
    public IReadOnlyList<Result> GetClassification()
    {
        classification.Clear();

        foreach (KartBehaviour kartBehaviour in raceManager.RaceKarts)
        {
            if (kartBehaviour == null)
                continue;

            Rigidbody kart = kartBehaviour.GetComponent<Rigidbody>();

            if (kart == null)
                continue;

            float raceTime = GetRaceTime(kart);
            float penalty = GetPenalty(kart);

            classification.Add(new Result
            {
                Kart = kart,
                Finished = IsFinished(kart),
                Lap = GetLap(kart),
                RaceTime = raceTime,
                BestLap = GetBestLap(kart),
                Penalty = penalty,
                FinalTime = raceTime + penalty
            });
        }

        classification.Sort(CompareResults);

        for (int i = 0; i < classification.Count; i++)
        {
            Result result = classification[i];
            result.Position = i + 1;
            classification[i] = result;
        }

        return classification;
    }

    private int CompareResults(Result a, Result b)
    {
        if (a.Finished != b.Finished)
            return a.Finished ? -1 : 1;

        if (a.Finished)
            return a.FinalTime.CompareTo(b.FinalTime);

        // Los que siguen corriendo, por su posición en pista.
        return GetPosition(a.Kart).CompareTo(GetPosition(b.Kart));
    }

    private double ServerNow => NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.Time : Time.timeAsDouble;

    private float GetFinishTime(Rigidbody kart)
    {
        if (IsAuthority)
            return TryGetTiming(kart, out KartTiming timing) ? timing.FinishTime : -1f;

        return TryGetStatus(kart, out KartStatus status) && status.Finished != 0 ? status.FinishTime : -1f;
    }

    private bool TryGetTiming(Rigidbody kart, out KartTiming timing)
    {
        timing = null;
        return kart != null && timings.TryGetValue(kart, out timing);
    }

    private bool TryGetStatus(Rigidbody kart, out KartStatus status)
    {
        status = default;

        NetworkObject networkObject = kart != null ? kart.GetComponent<NetworkObject>() : null;

        if (networkObject == null || !networkObject.IsSpawned)
            return false;

        ulong id = networkObject.NetworkObjectId;

        foreach (KartStatus candidate in statuses)
        {
            if (candidate.KartId == id)
            {
                status = candidate;
                return true;
            }
        }

        return false;
    }

    // ───────────── Medición (servidor) ─────────────

    private void HandleRaceStarted()
    {
        if (IsAuthority)
            timings.Clear();
    }

    private void HandleLapCompleted(Rigidbody kart)
    {
        if (!IsAuthority || !HasStarted || kart == null)
            return;

        KartTiming timing = GetOrCreateTiming(kart);

        double now = ServerNow;
        float lapTime = (float)(now - timing.LapStart);

        timing.LastLap = lapTime;
        timing.LapStart = now;

        if (timing.BestLap < 0f || lapTime < timing.BestLap)
            timing.BestLap = lapTime;
    }

    private void HandleKartFinished(Rigidbody kart)
    {
        if (!IsAuthority || !HasStarted || kart == null)
            return;

        GetOrCreateTiming(kart).FinishTime = (float)(ServerNow - raceManager.StartServerTime);

        PublishStatuses();

        KartFinished?.Invoke(kart);
    }

    private void HandlePenaltyApplied(Rigidbody kart, float seconds)
    {
        if (IsAuthority)
            PenaltyApplied?.Invoke(kart, seconds);
    }

    private KartTiming GetOrCreateTiming(Rigidbody kart)
    {
        if (!timings.TryGetValue(kart, out KartTiming timing))
        {
            // La primera vuelta se mide desde la largada.
            timing = new KartTiming { LapStart = raceManager.StartServerTime };
            timings.Add(kart, timing);
        }

        return timing;
    }

    /// <summary>Servidor: actualiza la lista que ven los clientes (solo lo que cambió).</summary>
    private void PublishStatuses()
    {
        if (!IsSpawned || !IsServer)
            return;

        // Saca los karts que ya no están.
        for (int i = statuses.Count - 1; i >= 0; i--)
        {
            if (!NetworkManager.SpawnManager.SpawnedObjects.ContainsKey(statuses[i].KartId))
                statuses.RemoveAt(i);
        }

        foreach (KartBehaviour kartBehaviour in raceManager.RaceKarts)
        {
            NetworkObject networkObject = kartBehaviour != null ? kartBehaviour.GetComponent<NetworkObject>() : null;

            if (networkObject == null || !networkObject.IsSpawned)
                continue;

            Rigidbody kart = kartBehaviour.GetComponent<Rigidbody>();
            TryGetTiming(kart, out KartTiming timing);

            KartStatus status = new KartStatus
            {
                KartId = networkObject.NetworkObjectId,
                Position = positionManager.GetPosition(kart),
                Lap = lapManager.GetCurrentLap(kart),
                LastCheckpoint = checkpointManager.GetLastCheckpoint(kart),
                Finished = (byte)(timing != null && timing.FinishTime >= 0f ? 1 : 0),
                FinishTime = timing != null ? timing.FinishTime : -1f,
                LastLap = timing != null ? timing.LastLap : -1f,
                BestLap = timing != null ? timing.BestLap : -1f,
                Penalty = penaltyManager.GetPenaltyTime(kart)
            };

            int index = IndexOf(status.KartId);

            if (index < 0)
                statuses.Add(status);
            else if (!statuses[index].Equals(status))
                statuses[index] = status;
        }
    }

    private int IndexOf(ulong kartId)
    {
        for (int i = 0; i < statuses.Count; i++)
        {
            if (statuses[i].KartId == kartId)
                return i;
        }

        return -1;
    }

    /// <summary>Clientes: avisa las llegadas y las penalizaciones nuevas.</summary>
    private void HandleStatusesChanged(NetworkListEvent<KartStatus> change)
    {
        bool added = change.Type == NetworkListEvent<KartStatus>.EventType.Add;
        bool changed = change.Type == NetworkListEvent<KartStatus>.EventType.Value;

        if (!added && !changed)
            return;

        KartStatus current = change.Value;
        KartStatus previous = changed ? change.PreviousValue : default;

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(current.KartId, out NetworkObject networkObject))
            return;

        Rigidbody kart = networkObject.GetComponent<Rigidbody>();

        if (current.Penalty > previous.Penalty + 0.001f)
            PenaltyApplied?.Invoke(kart, current.Penalty - previous.Penalty);

        if (current.Finished != 0 && previous.Finished == 0)
            KartFinished?.Invoke(kart);
    }
}
