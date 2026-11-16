using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Qualy y campeonato en la pista (RaceSession dice en qué fase está).
///
/// Qualy (tanda con tiempo):
/// - Largan todos juntos (sin penalización por salir antes) y dan vueltas
///   hasta que se termina el tiempo (RaceSession.QualifyingSeconds, desde la
///   largada).
/// - Vale la mejor vuelta de cada uno. Una vuelta con penalización (límites
///   de pista) queda anulada.
/// - Cuando se termina el tiempo (bandera), cada uno termina la vuelta en la
///   que va (esa cuenta) y queda TERMINADO. Si alguno no llega en
///   flagTimeout segundos, la tanda se cierra igual.
/// - El servidor publica los mejores tiempos (RaceSetupSync), así todos ven
///   la misma tabla. El orden (GetQualifyingOrder) arma la grilla de la
///   carrera: en los resultados, el anfitrión toca A LA CARRERA.
///
/// Campeonato: cuando termina la carrera (todos llegaron), el servidor anota
/// el orden de llegada en RaceSession (puntos estilo F1).
///
/// Se arma solo en las escenas con RaceManager (no hace falta tocar la escena).
/// </summary>
public class RaceWeekend : MonoBehaviour
{
    [Tooltip("Después de la bandera, cuánto se espera a que todos terminen su vuelta (s).")]
    [SerializeField, Min(10f)] private float flagTimeout = 120f;

    [Tooltip("Cada cuánto el servidor publica los tiempos de la qualy (s).")]
    [SerializeField, Min(0.1f)] private float publishInterval = 0.5f;

    private class KartQualifying
    {
        public float Best = -1f;
        public bool LapValid = true;
    }

    private RaceManager raceManager;
    private RaceLapManager lapManager;
    private RaceTimingManager timing;
    private RacePenaltyManager penalties;

    // Servidor: lo de cada kart.
    private readonly Dictionary<Rigidbody, KartQualifying> karts = new Dictionary<Rigidbody, KartQualifying>();
    private bool flagged;
    private double flagTime;
    private float nextPublish;
    private bool raceRecorded;

    // Todos: mejor vuelta válida de cada kart (por id de red).
    private readonly Dictionary<ulong, float> bestById = new Dictionary<ulong, float>();
    private string lastParsed;

    private readonly List<Rigidbody> order = new List<Rigidbody>();
    private readonly StringBuilder builder = new StringBuilder();

    public static RaceWeekend Instance { get; private set; }

    /// <summary>Segundos que le quedan a la qualy (antes de largar, la duración entera).</summary>
    public float Remaining
    {
        get
        {
            if (!RaceSession.IsQualifying || raceManager == null)
                return 0f;

            if (raceManager.IsWaiting() || raceManager.IsCountdown())
                return RaceSession.QualifyingSeconds;

            return Mathf.Max(0f, RaceSession.QualifyingSeconds - (float)(raceManager.Now - raceManager.StartServerTime));
        }
    }

    /// <summary>Se terminó el tiempo de la qualy (cada uno termina su vuelta).</summary>
    public bool IsFlagged => RaceSession.IsQualifying && raceManager != null &&
                             (raceManager.IsRacing() || raceManager.IsFinished()) && Remaining <= 0f;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        lapManager = FindFirstObjectByType<RaceLapManager>();
        timing = FindFirstObjectByType<RaceTimingManager>();
        penalties = FindFirstObjectByType<RacePenaltyManager>();

        if (lapManager != null)
            lapManager.LapCompleted += HandleLapCompleted;

        if (penalties != null)
            penalties.PenaltyApplied += HandlePenalty;

        if (raceManager != null)
            raceManager.RaceFinished += HandleRaceFinished;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        if (lapManager != null)
            lapManager.LapCompleted -= HandleLapCompleted;

        if (penalties != null)
            penalties.PenaltyApplied -= HandlePenalty;

        if (raceManager != null)
            raceManager.RaceFinished -= HandleRaceFinished;
    }

    private void Update()
    {
        if (!RaceSession.IsQualifying || raceManager == null)
            return;

        if (NetworkRole.IsAuthority)
            UpdateServer();
        else
            ReadPublished();
    }

    // ------------------------------------------------------------------
    // Servidor
    // ------------------------------------------------------------------

    private void UpdateServer()
    {
        if (raceManager.IsRacing())
        {
            // Bandera: desde ahora, la vuelta que cada uno termine es la última.
            if (!flagged && Remaining <= 0f)
            {
                flagged = true;
                flagTime = raceManager.Now;

                if (lapManager != null)
                    lapManager.SetTotalLaps(1);

                Debug.Log("QUALY: bandera. Cada uno termina su vuelta.", this);
            }

            // Si alguno no termina (quedó parado), se cierra igual.
            if (flagged && raceManager.Now - flagTime > flagTimeout)
                raceManager.FinishRace();
        }

        if (Time.unscaledTime >= nextPublish)
        {
            nextPublish = Time.unscaledTime + publishInterval;
            Publish();
        }
    }

    private KartQualifying Get(Rigidbody kart)
    {
        if (!karts.TryGetValue(kart, out KartQualifying data))
        {
            data = new KartQualifying();
            karts[kart] = data;
        }

        return data;
    }

    private void HandleLapCompleted(Rigidbody kart)
    {
        if (!RaceSession.IsQualifying || !NetworkRole.IsAuthority || kart == null)
            return;

        KartQualifying data = Get(kart);

        // RaceTimingManager ya anotó esta vuelta (se suscribió antes).
        float lap = timing != null ? timing.GetLastLap(kart) : -1f;

        if (data.LapValid && lap > 0f && (data.Best < 0f || lap < data.Best))
            data.Best = lap;

        // Empieza otra vuelta, limpia.
        data.LapValid = true;
        Publish();
    }

    private void HandlePenalty(Rigidbody kart, float seconds)
    {
        // En la qualy, salirse de los límites anula la vuelta (no suma tiempo).
        if (RaceSession.IsQualifying && NetworkRole.IsAuthority && kart != null)
            Get(kart).LapValid = false;
    }

    /// <summary>Servidor: "id:mejor;id:mejor;..." para los demás.</summary>
    private void Publish()
    {
        bestById.Clear();
        builder.Clear();

        foreach (KeyValuePair<Rigidbody, KartQualifying> pair in karts)
        {
            NetworkObject networkObject = pair.Key != null ? pair.Key.GetComponent<NetworkObject>() : null;

            if (networkObject == null || !networkObject.IsSpawned || pair.Value.Best < 0f)
                continue;

            bestById[networkObject.NetworkObjectId] = pair.Value.Best;

            builder.Append(networkObject.NetworkObjectId.ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(pair.Value.Best.ToString("R", CultureInfo.InvariantCulture)).Append(';');
        }

        if (RaceSetupSync.Instance != null)
            RaceSetupSync.Instance.PublishQualifyingTimes(builder.ToString());
    }

    /// <summary>Servidor: terminó la carrera; en el campeonato se anotan los puntos.</summary>
    private void HandleRaceFinished()
    {
        if (RaceSession.IsQualifying || !RaceSession.IsChampionship || !NetworkRole.IsAuthority || raceRecorded || timing == null)
            return;

        raceRecorded = true;

        var finishOrder = new List<KeyValuePair<string, string>>();

        foreach (RaceTimingManager.Result result in timing.GetClassification())
        {
            if (result.Kart == null)
                continue;

            finishOrder.Add(new KeyValuePair<string, string>(RaceSession.KartKey(result.Kart), RaceStandingsTower.DisplayName(result.Kart)));
        }

        RaceSession.RecordRace(finishOrder);
        Debug.Log($"CAMPEONATO: fecha {RaceSession.Round}/{RaceSession.Rounds} anotada ({finishOrder.Count} karts).", this);
    }

    // ------------------------------------------------------------------
    // Clientes
    // ------------------------------------------------------------------

    private void ReadPublished()
    {
        string text = RaceSetupSync.Instance != null ? RaceSetupSync.Instance.QualifyingTimes : null;

        if (text == null || text == lastParsed)
            return;

        lastParsed = text;
        bestById.Clear();

        foreach (string entry in text.Split(';'))
        {
            int colon = entry.IndexOf(':');

            if (colon <= 0)
                continue;

            if (ulong.TryParse(entry.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong id) &&
                float.TryParse(entry.Substring(colon + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float best))
            {
                bestById[id] = best;
            }
        }
    }

    // ------------------------------------------------------------------
    // Consultas (todos)
    // ------------------------------------------------------------------

    /// <summary>Mejor vuelta válida de la qualy (s), o -1 si no tiene.</summary>
    public float GetBestLap(Rigidbody kart)
    {
        NetworkObject networkObject = kart != null ? kart.GetComponent<NetworkObject>() : null;

        if (networkObject == null || !networkObject.IsSpawned)
            return NetworkRole.IsAuthority && kart != null && karts.TryGetValue(kart, out KartQualifying data) ? data.Best : -1f;

        return bestById.TryGetValue(networkObject.NetworkObjectId, out float best) ? best : -1f;
    }

    /// <summary>
    /// Karts ordenados por su mejor vuelta (los que no tienen tiempo, al
    /// final). La lista se reutiliza: no guardarla.
    /// </summary>
    public IReadOnlyList<Rigidbody> GetQualifyingOrder()
    {
        order.Clear();

        if (raceManager == null)
            return order;

        foreach (KartBehaviour kart in raceManager.RaceKarts)
        {
            Rigidbody body = kart != null ? kart.GetComponent<Rigidbody>() : null;

            if (body != null)
                order.Add(body);
        }

        // Ordena con el índice como desempate, así los que no tienen tiempo
        // quedan en el mismo orden de la grilla.
        var index = new Dictionary<Rigidbody, int>();

        for (int i = 0; i < order.Count; i++)
            index[order[i]] = i;

        order.Sort((a, b) =>
        {
            float ta = GetBestLap(a);
            float tb = GetBestLap(b);

            if ((ta >= 0f) != (tb >= 0f))
                return ta >= 0f ? -1 : 1;

            if (ta >= 0f && !Mathf.Approximately(ta, tb))
                return ta.CompareTo(tb);

            return index[a].CompareTo(index[b]);
        });

        return order;
    }

    /// <summary>Servidor: la grilla de la carrera (claves de los karts, en orden).</summary>
    public List<string> GetGridKeys()
    {
        var keys = new List<string>();

        foreach (Rigidbody kart in GetQualifyingOrder())
            keys.Add(RaceSession.KartKey(kart));

        return keys;
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

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            RaceManager manager = root.GetComponentInChildren<RaceManager>(true);

            if (manager == null)
                continue;

            if (FindFirstObjectByType<RaceWeekend>() != null)
                return;

            var go = new GameObject("FinDeSemana (qualy y campeonato)");
            SceneManager.MoveGameObjectToScene(go, scene);

            var weekend = go.AddComponent<RaceWeekend>();
            weekend.raceManager = manager;
            return;
        }
    }
}
