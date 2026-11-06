using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Paradas en boxes (con la calle de PitLane):
/// - Cada kart tiene su box (su rectángulo): se reparten en el orden de los
///   karts en red (NetworkObjectId), así todas las computadoras dan lo mismo.
/// - En la calle de boxes hay limitador (KartBehaviour.SpeedLimit).
/// - Parada: el kart tiene que quedar quieto (a 0 km/h) con el centro dentro
///   de su rectángulo. Ahí el jugador elige las ruedas (PitStopHUD: 1, 2, 3 o
///   4, o con las flechas y Enter, o con el mouse). Mientras se las cambian
///   el kart queda frenado (KartBehaviour.PitHold); las mismas ruedas tardan
///   sameWheelsTime y otras, otherWheelsTime. Salen nuevas y a la
///   temperatura de los calentadores. Si arranca sin elegir, no paró.
/// - Las ruedas nuevas se instalan como en el Catálogo y se publican por red
///   (NetworkKart.PublishLoadout), así todos ven el cambio.
/// - Las vueltas: la calle de boxes no pasa por los checkpoints de la recta,
///   así que el servidor los da por pasados cuando el kart los deja atrás por
///   la calle (también la llegada).
/// - Parada obligatoria (RaceSession.MandatoryPitStop, elegible en el menú):
///   en carreras de 2 vueltas o más, el que llega sin haber parado recibe
///   missedStopPenalty segundos. Las paradas las cuenta el servidor y las
///   publica (RaceSetupSync), así el HUD de todos sabe quién paró.
/// - Bots (en la computadora que los simula): paran si la parada es
///   obligatoria (en una vuelta al azar de la segunda mitad) o si tienen las
///   cubiertas gastadas y quedan vueltas. Para entrar, salir y parar los
///   maneja esto (CustomIABots se apaga mientras tanto) y se ponen las
///   mismas ruedas.
/// Se arma solo en las escenas con RaceManager y una PitLane.
/// </summary>
public class PitStops : MonoBehaviour
{
    [Header("Parada")]
    [Tooltip("Velocidad a la que se considera que el kart está quieto (m/s). 0,15 m/s es medio km/h.")]
    [SerializeField, Min(0.01f)] private float stopSpeed = 0.15f;

    [Tooltip("Cuánto tardan en cambiar las ruedas por otras iguales (s).")]
    [SerializeField, Min(0.5f)] private float sameWheelsTime = 3f;

    [Tooltip("Cuánto tardan en poner otro tipo de ruedas (s).")]
    [SerializeField, Min(0.5f)] private float otherWheelsTime = 5f;

    [Tooltip("Temperatura de las cubiertas nuevas (vienen de los calentadores) (°C).")]
    [SerializeField] private float blanketTemperature = 50f;

    [Header("Parada obligatoria")]
    [Tooltip("Segundos de penalización para el que llega sin haber parado.")]
    [SerializeField, Min(0f)] private float missedStopPenalty = 30f;

    [Header("Bots")]
    [Tooltip("Desgaste promedio de las cubiertas desde el que un bot para (0 a 1).")]
    [SerializeField, Range(0f, 1f)] private float botWearToPit = 0.55f;

    [Tooltip("A cuántos metros del principio del recorrido de boxes el bot deja de manejar solo y entra.")]
    [SerializeField, Min(2f)] private float botEntryLead = 12f;

    [Tooltip("Cuánto frena el bot para llegar al limitador y a su box (m/s²).")]
    [SerializeField, Min(1f)] private float botBrakeDecel = 4f;

    [Tooltip("Ángulo de las ruedas con el volante a fondo, para la cuenta del volante del bot (°).")]
    [SerializeField, Min(5f)] private float botSteerAngle = 25f;

    public enum StopState
    {
        None,
        Choosing,
        Working,
        Done
    }

    private class KartPit
    {
        public KartBehaviour Kart;
        public Rigidbody Body;
        public NetworkKart Network;
        public CustomIABots Bot;
        public ulong Id;
        public int Box = -1;

        public bool InLane;
        public float S;
        public float Lateral;

        public StopState State;
        public float WorkStart;
        public float WorkUntil;
        public WheelData Chosen;

        // Bots.
        public int BotPitLap = -1;
        public bool BotDriving;
        public bool BotServed;
        public float BotDrivingSince;
        public float NextBotCheck;
    }

    public static PitStops Instance { get; private set; }

    private PitLane lane;
    private RaceManager raceManager;
    private RaceLapManager lapManager;
    private RaceCheckpointManager checkpointManager;
    private RacePenaltyManager penaltyManager;
    private TrackCenterline centerline;

    private readonly Dictionary<KartBehaviour, KartPit> karts = new Dictionary<KartBehaviour, KartPit>();
    private readonly List<KartPit> ordered = new List<KartPit>();
    private float nextRefresh;

    // Servidor: paradas hechas por kart, y a quién se le cobró no parar.
    private readonly Dictionary<Rigidbody, int> stopsByKart = new Dictionary<Rigidbody, int>();
    private readonly HashSet<Rigidbody> missedStop = new HashSet<Rigidbody>();

    // Todos: paradas por id de red (lo publicado por el servidor).
    private readonly Dictionary<ulong, int> stopsById = new Dictionary<ulong, int>();
    private string lastParsed;

    // Distancia de cada checkpoint sobre la línea central (en el sentido de esta carrera).
    private readonly Dictionary<RaceCheckpoint, float> checkpointDistance = new Dictionary<RaceCheckpoint, float>();
    private bool checkpointsReversed;

    private readonly StringBuilder builder = new StringBuilder();

    public PitLane Lane => lane;

    /// <summary>True si en esta carrera hay que parar (y no es la qualy ni una carrera de 1 vuelta).</summary>
    public bool StopIsMandatory =>
        RaceSession.MandatoryPitStop && !RaceSession.IsQualifying && lapManager != null &&
        lapManager.TotalLaps >= 2 && lapManager.TotalLaps < RaceSetupSync.QualifyingLaps;

    public float MissedStopPenalty => missedStopPenalty;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        lapManager = FindFirstObjectByType<RaceLapManager>();
        checkpointManager = FindFirstObjectByType<RaceCheckpointManager>();
        penaltyManager = FindFirstObjectByType<RacePenaltyManager>();
        centerline = FindFirstObjectByType<TrackCenterline>();

        if (lapManager != null)
            lapManager.KartFinished += HandleKartFinished;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        if (lapManager != null)
            lapManager.KartFinished -= HandleKartFinished;
    }

    // ------------------------------------------------------------------
    // Consultas (HUD)
    // ------------------------------------------------------------------

    /// <summary>Box del kart (0 = el primero en esta carrera), o -1.</summary>
    public int GetBox(KartBehaviour kart)
    {
        return kart != null && karts.TryGetValue(kart, out KartPit pit) ? pit.Box : -1;
    }

    public StopState GetState(KartBehaviour kart)
    {
        return kart != null && karts.TryGetValue(kart, out KartPit pit) ? pit.State : StopState.None;
    }

    /// <summary>True si el kart está en la calle de boxes (en el limitador).</summary>
    public bool IsInLane(KartBehaviour kart)
    {
        return kart != null && karts.TryGetValue(kart, out KartPit pit) && pit.InLane;
    }

    /// <summary>De 0 a 1, cuánto del cambio de ruedas ya está hecho.</summary>
    public float GetWorkProgress(KartBehaviour kart)
    {
        if (kart == null || !karts.TryGetValue(kart, out KartPit pit) || pit.State != StopState.Working)
            return 0f;

        return Mathf.InverseLerp(pit.WorkStart, pit.WorkUntil, Time.time);
    }

    public float GetWorkRemaining(KartBehaviour kart)
    {
        if (kart == null || !karts.TryGetValue(kart, out KartPit pit) || pit.State != StopState.Working)
            return 0f;

        return Mathf.Max(0f, pit.WorkUntil - Time.time);
    }

    /// <summary>Ruedas que se están poniendo (en el cambio).</summary>
    public WheelData GetChosenWheels(KartBehaviour kart)
    {
        return kart != null && karts.TryGetValue(kart, out KartPit pit) ? pit.Chosen : null;
    }

    /// <summary>Ruedas que se pueden elegir (las del catálogo, en su orden).</summary>
    public List<WheelData> GetWheelOptions(KartBehaviour kart)
    {
        var options = new List<WheelData>();
        NetworkKart network = kart != null ? kart.GetComponent<NetworkKart>() : null;
        PartCatalog catalog = network != null ? network.Catalog : null;

        if (catalog == null)
            return options;

        foreach (KartPart part in catalog.GetParts(PartType.Wheels))
        {
            if (part is WheelData wheels)
                options.Add(wheels);
        }

        return options;
    }

    /// <summary>Ruedas que tiene puestas el kart.</summary>
    public static WheelData GetCurrentWheels(KartBehaviour kart)
    {
        Kart model = kart != null ? kart.Kart : null;
        return model != null && model.Configuration != null ? model.Configuration.Wheels : null;
    }

    /// <summary>Paradas que hizo el kart en esta carrera.</summary>
    public int GetStops(Rigidbody kart)
    {
        if (kart == null)
            return 0;

        if (NetworkRole.IsAuthority)
            return stopsByKart.TryGetValue(kart, out int count) ? count : 0;

        NetworkObject networkObject = kart.GetComponent<NetworkObject>();
        return networkObject != null && stopsById.TryGetValue(networkObject.NetworkObjectId, out int published) ? published : 0;
    }

    /// <summary>True si esta penalización es la de llegar sin la parada obligatoria (para el aviso del HUD).</summary>
    public bool IsMissedStopPenalty(Rigidbody kart, float seconds)
    {
        return StopIsMandatory && GetStops(kart) == 0 && Mathf.Approximately(seconds, missedStopPenalty);
    }

    // ------------------------------------------------------------------
    // Elegir ruedas (jugador)
    // ------------------------------------------------------------------

    /// <summary>
    /// El jugador eligió las ruedas parado en su box: empiezan a cambiarlas.
    /// Devuelve false si no estaba esperando la elección.
    /// </summary>
    public bool ChooseWheels(KartBehaviour kart, WheelData wheels)
    {
        if (kart == null || wheels == null || !karts.TryGetValue(kart, out KartPit pit) || pit.State != StopState.Choosing)
            return false;

        StartWork(pit, wheels);
        return true;
    }

    private void StartWork(KartPit pit, WheelData wheels)
    {
        bool same = wheels == GetCurrentWheels(pit.Kart);

        pit.Chosen = wheels;
        pit.State = StopState.Working;
        pit.WorkStart = Time.time;
        pit.WorkUntil = Time.time + (same ? sameWheelsTime : otherWheelsTime);
        pit.Kart.PitHold = true;
    }

    private void FinishWork(KartPit pit)
    {
        KartBehaviour kart = pit.Kart;
        WheelData chosen = pit.Chosen;

        if (chosen != null && chosen != GetCurrentWheels(kart))
        {
            KartConfigurationController configuration = pit.Network != null ? pit.Network.ConfigurationController : kart.GetComponentInChildren<KartConfigurationController>();

            if (configuration != null)
                configuration.InstallPart(chosen);

            if (pit.Network != null)
                pit.Network.PublishLoadout();
        }

        // Nuevas, de los calentadores.
        if (kart.TireThermals != null)
            kart.TireThermals.Fit(blanketTemperature);

        kart.PitHold = false;
        pit.State = StopState.Done;
        pit.BotServed = true;

        Debug.Log($"BOXES: {kart.name} paró en el box {pit.Box + 1} ({(chosen != null ? chosen.partName : "-")}).", kart);

        ReportStop(pit);
    }

    private void ReportStop(KartPit pit)
    {
        if (NetworkRole.IsAuthority)
        {
            ServerAddStop(pit.Body);
            return;
        }

        NetworkObject networkObject = pit.Body.GetComponent<NetworkObject>();

        if (RaceSetupSync.Instance != null && networkObject != null)
            RaceSetupSync.Instance.ReportPitStop(networkObject);
    }

    /// <summary>Servidor: un kart terminó una parada.</summary>
    public void ServerAddStop(Rigidbody kart)
    {
        if (kart == null || !NetworkRole.IsAuthority)
            return;

        stopsByKart.TryGetValue(kart, out int count);
        stopsByKart[kart] = count + 1;
        Publish();
    }

    private void Publish()
    {
        builder.Clear();

        foreach (KeyValuePair<Rigidbody, int> pair in stopsByKart)
        {
            NetworkObject networkObject = pair.Key != null ? pair.Key.GetComponent<NetworkObject>() : null;

            if (networkObject == null || !networkObject.IsSpawned)
                continue;

            builder.Append(networkObject.NetworkObjectId.ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(pair.Value.ToString(CultureInfo.InvariantCulture)).Append(';');
        }

        if (RaceSetupSync.Instance != null)
            RaceSetupSync.Instance.PublishPitStops(builder.ToString());
    }

    private void ReadPublished()
    {
        string text = RaceSetupSync.Instance != null ? RaceSetupSync.Instance.PitStopsText : null;

        if (text == null || text == lastParsed)
            return;

        lastParsed = text;
        stopsById.Clear();

        foreach (string entry in text.Split(';'))
        {
            int colon = entry.IndexOf(':');

            if (colon <= 0)
                continue;

            if (ulong.TryParse(entry.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong id) &&
                int.TryParse(entry.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
            {
                stopsById[id] = count;
            }
        }
    }

    /// <summary>Servidor: el que llega sin la parada obligatoria, penalizado.</summary>
    private void HandleKartFinished(Rigidbody kart)
    {
        if (!NetworkRole.IsAuthority || kart == null || !StopIsMandatory || GetStops(kart) > 0 || !missedStop.Add(kart))
            return;

        if (penaltyManager != null)
            penaltyManager.AddPenalty(kart, missedStopPenalty);

        Debug.Log($"BOXES: {kart.name} llegó sin la parada obligatoria (+{missedStopPenalty:0} s).", kart);
    }

    // ------------------------------------------------------------------
    // Cada paso
    // ------------------------------------------------------------------

    private void Update()
    {
        if (lane == null)
            return;

        if (!NetworkRole.IsAuthority)
            ReadPublished();

        // El box del jugador de esta computadora, en amarillo.
        int ownBox = -1;

        foreach (KartPit pit in ordered)
        {
            if (pit.Kart != null && pit.Kart.UsesPlayerAssists)
                ownBox = pit.Box;
        }

        lane.SetHighlightedBox(ownBox);
    }

    private void FixedUpdate()
    {
        if (lane == null || raceManager == null)
            return;

        RefreshKarts();

        foreach (KartPit pit in ordered)
        {
            if (pit.Kart == null || pit.Body == null)
                continue;

            Vector3 position = pit.Body.position;
            pit.InLane = lane.IsInLane(position, out pit.S, out pit.Lateral);

            if (pit.Kart.IsSimulated)
                OwnerStep(pit);

            if (NetworkRole.IsAuthority && raceManager.IsRacing())
                PassCheckpointsInLane(pit, position);
        }
    }

    /// <summary>Los karts de la carrera, con su box (en el orden de red, igual en todas las computadoras).</summary>
    private void RefreshKarts()
    {
        if (Time.time < nextRefresh)
            return;

        nextRefresh = Time.time + 0.5f;

        foreach (KartBehaviour kart in raceManager.RaceKarts)
        {
            if (kart == null || karts.ContainsKey(kart))
                continue;

            karts[kart] = new KartPit
            {
                Kart = kart,
                Body = kart.GetComponent<Rigidbody>(),
                Network = kart.GetComponent<NetworkKart>(),
                Bot = kart.GetComponent<CustomIABots>()
            };
        }

        ordered.Clear();

        foreach (KartPit pit in karts.Values)
        {
            if (pit.Kart == null)
                continue;

            // El id de red es el mismo en todas las computadoras.
            NetworkObject networkObject = pit.Kart.GetComponent<NetworkObject>();
            pit.Id = networkObject != null && networkObject.IsSpawned ? networkObject.NetworkObjectId : (ulong)(uint)pit.Kart.GetInstanceID();
            ordered.Add(pit);
        }

        ordered.Sort((a, b) => a.Id.CompareTo(b.Id));

        for (int i = 0; i < ordered.Count; i++)
            ordered[i].Box = i % lane.BoxCount;
    }

    /// <summary>En la computadora que simula el kart: limitador, parada y (bots) manejo en boxes.</summary>
    private void OwnerStep(KartPit pit)
    {
        KartBehaviour kart = pit.Kart;

        UpdateBotPlan(pit);

        if (pit.BotDriving)
            DriveBot(pit);

        kart.SpeedLimit = pit.InLane ? lane.SpeedLimit : 0f;

        float speed = pit.Body.linearVelocity.magnitude;
        bool inBox = pit.Box >= 0 && pit.InLane && lane.IsInsideBox(pit.Box, pit.Body.position);

        switch (pit.State)
        {
            case StopState.None:
                if (inBox && speed < stopSpeed && kart.InputEnabled && !kart.PitHold)
                {
                    // El bot se pone las mismas; el jugador elige (PitStopHUD).
                    if (pit.BotDriving)
                        StartWork(pit, GetCurrentWheels(kart));
                    else if (kart.UsesPlayerAssists)
                        pit.State = StopState.Choosing;
                }

                break;

            case StopState.Choosing:
                // Arrancó sin elegir: no paró.
                if (!inBox || speed > stopSpeed * 4f || !kart.InputEnabled)
                    pit.State = StopState.None;

                break;

            case StopState.Working:
                kart.PitHold = true;

                if (Time.time >= pit.WorkUntil)
                    FinishWork(pit);

                break;

            case StopState.Done:
                // Hasta que sale de la calle no vuelve a parar.
                if (!pit.InLane)
                    pit.State = StopState.None;

                break;
        }
    }

    // ------------------------------------------------------------------
    // Servidor: checkpoints que la calle de boxes no cruza
    // ------------------------------------------------------------------

    private void PassCheckpointsInLane(KartPit pit, Vector3 position)
    {
        if (checkpointManager == null || centerline == null || !centerline.IsValid)
            return;

        // Cerca del recorrido de boxes y fuera de la pista.
        float toLane = lane.Project(position, out _, out _);

        if (toLane > lane.HalfWidth + 1.5f)
            return;

        float kartDistance = centerline.GetDistance(position);
        Vector3 onTrack = centerline.GetPointAtDistance(kartDistance);
        onTrack.y = position.y;

        if (Vector3.Distance(onTrack, position) < centerline.AverageWidth * 0.5f + 0.5f)
            return;

        if (checkpointsReversed != centerline.IsReversed)
        {
            checkpointDistance.Clear();
            checkpointsReversed = centerline.IsReversed;
        }

        float length = centerline.Length;

        for (int guard = 0; guard < 3; guard++)
        {
            int next = checkpointManager.GetNextCheckpoint(pit.Body);
            RaceCheckpoint checkpoint = checkpointManager.GetCheckpoint(next);

            if (checkpoint == null)
                return;

            if (!checkpointDistance.TryGetValue(checkpoint, out float distance))
            {
                distance = centerline.GetDistance(checkpoint.transform.position);
                checkpointDistance[checkpoint] = distance;
            }

            // Cuánto lo dejó atrás (en el sentido de la carrera).
            float past = Mathf.Repeat(kartDistance - distance + length * 0.5f, length) - length * 0.5f;

            if (past < 0f || past > 40f)
                return;

            checkpointManager.TryPassCheckpoint(pit.Body, checkpoint.CheckpointIndex);

            if (checkpointManager.GetNextCheckpoint(pit.Body) == next)
                return;
        }
    }

    // ------------------------------------------------------------------
    // Bots
    // ------------------------------------------------------------------

    /// <summary>Si el bot quiere parar en esta vuelta y, cerca de la entrada, toma el mando.</summary>
    private void UpdateBotPlan(KartPit pit)
    {
        if (pit.Bot == null || pit.Kart.UsesPlayerAssists || pit.BotDriving || pit.State != StopState.None)
            return;

        if (!pit.Bot.enabled || !pit.Kart.InputEnabled || !raceManager.IsRacing() || lapManager == null || RaceSession.IsQualifying)
            return;

        if (Time.time < pit.NextBotCheck)
            return;

        pit.NextBotCheck = Time.time + 0.2f;

        if (!BotWantsToPit(pit))
            return;

        // ¿Ya está por llegar al principio del recorrido de boxes?
        float startDistance = centerline.GetDistance(lane.GetPoint(0f));
        float botDistance = centerline.GetDistance(pit.Body.position);
        float ahead = Mathf.Repeat(startDistance - botDistance, centerline.Length);

        if (ahead > botEntryLead)
            return;

        pit.BotDriving = true;
        pit.BotServed = false;
        pit.BotDrivingSince = Time.time;
        pit.Bot.enabled = false;
    }

    private bool BotWantsToPit(KartPit pit)
    {
        int laps = lapManager.TotalLaps;
        int lap = lapManager.GetCurrentLap(pit.Body);
        int stops = GetStops(pit.Body);

        if (StopIsMandatory && stops == 0)
        {
            // Una vuelta al azar entre la tercera parte y la penúltima.
            if (pit.BotPitLap < 0)
                pit.BotPitLap = Random.Range(Mathf.Max(1, Mathf.CeilToInt(laps * 0.3f)), Mathf.Max(2, laps));

            return lap >= pit.BotPitLap;
        }

        // Cubiertas gastadas y queda al menos otra vuelta después de esta.
        KartTireThermals thermals = pit.Kart.TireThermals;

        if (thermals == null || laps - lap < 1 || laps >= RaceSetupSync.QualifyingLaps)
            return false;

        float wear = 0f;

        for (int i = 0; i < 4; i++)
            wear += thermals.GetWear(i);

        return wear / 4f >= botWearToPit;
    }

    /// <summary>Maneja al bot por la calle: entra, frena al limitador, para en su box y sale.</summary>
    private void DriveBot(KartPit pit)
    {
        KartBehaviour kart = pit.Kart;
        Vector3 position = pit.Body.position;
        float toLane = lane.Project(position, out float s, out float lateral);
        float speed = Vector3.Dot(pit.Body.linearVelocity, pit.Body.transform.forward);
        float limit = lane.SpeedLimit;

        lane.GetBox(pit.Box, out _, out _, out float boxS, out float boxLateral);

        // Terminó: de vuelta en la pista, maneja solo otra vez.
        bool outOfLane = pit.BotServed && s >= lane.LimiterEnd + 15f;
        bool lost = toLane > lane.HalfWidth + 8f || Time.time - pit.BotDrivingSince > 90f;

        if (outOfLane || lost || !kart.InputEnabled)
        {
            ReleaseBot(pit);
            return;
        }

        if (pit.State == StopState.Working)
            return;

        // Velocidad buscada.
        float target;

        if (s < lane.LimiterStart)
            target = Mathf.Sqrt(limit * limit + 2f * botBrakeDecel * (lane.LimiterStart - s));
        else if (!pit.BotServed)
            target = Mathf.Min(limit, Mathf.Sqrt(2f * botBrakeDecel * 0.6f * Mathf.Max(0f, boxS - s - 0.15f)));
        else if (s <= lane.LimiterEnd)
            target = limit;
        else
            target = 30f;

        // Carril: por el de afuera de los boxes y, cerca del suyo, adentro del rectángulo.
        float fastLane = -Mathf.Sign(boxLateral) * Mathf.Min(1f, lane.HalfWidth * 0.4f);
        float lateralTarget = fastLane;

        if (!pit.BotServed && boxS - s < 14f)
            lateralTarget = Mathf.Lerp(boxLateral, fastLane, Mathf.Clamp01((boxS - s - 3f) / 11f));
        else if (pit.BotServed && s - boxS < 8f)
            lateralTarget = Mathf.Lerp(boxLateral, fastLane, Mathf.Clamp01((s - boxS) / 8f));

        // Afuera de la calle (entrando o saliendo), por el medio del recorrido.
        if (s < lane.LimiterStart - 5f || s > lane.LimiterEnd + 5f)
            lateralTarget = 0f;

        float lookAhead = Mathf.Clamp(3f + 0.35f * Mathf.Max(0f, speed), 3f, 12f);
        Vector3 aim = lane.GetPoint(Mathf.Min(s + lookAhead, lane.Length), lateralTarget);

        Vector3 toAim = aim - position;
        toAim.y = 0f;
        Vector3 forward = pit.Body.transform.forward;
        forward.y = 0f;

        float angle = Vector3.SignedAngle(forward, toAim, Vector3.up);
        float steering = Mathf.Clamp(angle / botSteerAngle, -1f, 1f);

        float throttle;

        if (target < 0.3f && !pit.BotServed)
            throttle = -1f;
        else
            throttle = Mathf.Clamp((target - speed) * 0.5f, -1f, 1f);

        kart.SetInputs(throttle, steering, false);
    }

    private void ReleaseBot(KartPit pit)
    {
        pit.BotDriving = false;
        pit.BotPitLap = -1;
        pit.Kart.PitHold = false;

        if (pit.Bot != null)
        {
            pit.Bot.enabled = true;
            pit.Bot.ResumeAfterPit();
        }
    }

    // ------------------------------------------------------------------
    // Armado automático
    // ------------------------------------------------------------------

    private static bool installerHooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void HookInstaller()
    {
        if (!installerHooked)
        {
            SceneManager.sceneLoaded += (scene, mode) => InstallIn(scene);
            installerHooked = true;
        }

        InstallIn(SceneManager.GetActiveScene());
    }

    private static void InstallIn(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        RaceManager manager = null;
        PitLane pitLane = null;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (manager == null)
                manager = root.GetComponentInChildren<RaceManager>(true);

            if (pitLane == null)
                pitLane = root.GetComponentInChildren<PitLane>();
        }

        if (manager == null || pitLane == null || !pitLane.IsValid || FindFirstObjectByType<PitStops>() != null)
            return;

        var go = new GameObject("Boxes (paradas)");
        SceneManager.MoveGameObjectToScene(go, scene);

        var stops = go.AddComponent<PitStops>();
        stops.raceManager = manager;
        stops.lane = pitLane;
    }
}
