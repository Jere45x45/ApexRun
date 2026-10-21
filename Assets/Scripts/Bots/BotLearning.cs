using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Lo que aprendió un nivel de bots, tal como se guarda en su JSON.
/// - values: la Q-table, una fila por situación y una columna por acción.
/// - visits: cuántas veces aprendió de cada situación.
/// - layout: cómo están armadas las situaciones (tramos de pista, rangos de
///   velocidad, carriles, acciones). Si no coincide con el de los bots (otra
///   pista u otra versión), la tabla no sirve y se empieza de cero.
/// </summary>
[Serializable]
public class BotQTableData
{
    public int version;
    public string layout;
    public float exploration = -1f;
    public long decisions;
    public int cleanLaps;
    public float bestLapTime;
    public int incidents;
    public float kilometers;
    public float[] values = new float[0];
    public int[] visits = new int[0];
}

/// <summary>
/// El "cerebro" de los bots: una Q-table por nivel (fácil, medio y difícil),
/// las tres cargadas a la vez. Todos los bots de un nivel comparten la suya:
/// lo que aprende uno lo usan todos.
/// - Carrera (inferenceOnly activado): usan lo aprendido, sin explorar ni
///   aprender. Las tablas salen de los TextAsset de cada nivel
///   (BotLevelProfile.qTable), así que también andan en el juego compilado.
/// - Entrenamiento (desactivado): exploran (a veces prueban una acción al
///   azar), aprenden con Q-learning, aceleran el tiempo (trainingTimeScale) y
///   guardan lo aprendido cada tanto y al salir de Play, en el mismo JSON de
///   cada nivel. Cada minuto escriben en la consola cómo va cada nivel.
/// </summary>
[DefaultExecutionOrder(-100)]
public class BotLearning : MonoBehaviour
{
    public const int Version = 10;

    public static BotLearning Instance { get; private set; }

    [Header("Niveles")]
    [Tooltip("Fácil, medio y difícil (BotLevelProfile).")]
    [SerializeField] private BotLevelProfile[] levels = new BotLevelProfile[0];

    [Header("Aprendizaje")]
    [SerializeField, Range(0.01f, 1f)] private float learningRate = 0.2f;

    [Tooltip("Cuánto valen los premios que vienen después (por decisión, cada 0,25 s).")]
    [SerializeField, Range(0.5f, 0.999f)] private float discount = 0.97f;

    [Header("Exploración")]
    [SerializeField, Range(0f, 1f)] private float startExploration = 0.3f;

    [SerializeField, Range(0f, 1f)] private float minExploration = 0.03f;

    [Tooltip("Por cada decisión aprendida, la exploración se multiplica por esto.")]
    [SerializeField, Range(0.99f, 1f)] private float explorationDecay = 0.99998f;

    [Header("Entrenamiento")]
    [Tooltip("Activado: los bots usan lo que aprendieron, sin aprender (carrera). " +
             "Desactivado: entrenan y van guardando lo aprendido.")]
    [SerializeField] private bool inferenceOnly = true;

    [Tooltip("Velocidad del tiempo mientras entrenan.")]
    [SerializeField, Range(1f, 20f)] private float trainingTimeScale = 3f;

    [Tooltip("Cada cuánto se guarda lo aprendido (s reales).")]
    [SerializeField, Min(10f)] private float saveEverySeconds = 120f;

    [Tooltip("Cada cuánto se escribe en la consola cómo va cada nivel (s reales).")]
    [SerializeField, Min(5f)] private float reportEverySeconds = 60f;

    private class Brain
    {
        public BotLevelProfile Profile;
        public BotQTableData Data;
        public string Layout;
        public int States;
        public int Actions;
        public float[] Q;
        public int[] Visits;
        public bool[] Primed;
        public float Exploration;
        public float RecentLapSum;
        public int RecentLaps;
        public bool Dirty;
    }

    private readonly Dictionary<RaceDifficulty, Brain> brains = new Dictionary<RaceDifficulty, Brain>();

    private float nextSave;
    private float nextReport;

    public bool IsTraining => !inferenceOnly;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("Hay más de un BotLearning en la escena. Dejá solo uno.", this);
            Destroy(this);
            return;
        }

        Instance = this;

        if (IsTraining)
            Time.timeScale = trainingTimeScale;

        nextSave = Time.unscaledTime + saveEverySeconds;
        nextReport = Time.unscaledTime + reportEverySeconds;
    }

    private void OnDestroy()
    {
        if (Instance != this)
            return;

        if (IsTraining)
        {
            SaveAll();
            Time.timeScale = 1f;
        }

        Instance = null;
    }

    private void OnApplicationQuit()
    {
        if (Instance == this && IsTraining)
            SaveAll();
    }

    private void Update()
    {
        if (!IsTraining)
            return;

        if (Time.unscaledTime >= nextReport)
        {
            nextReport = Time.unscaledTime + reportEverySeconds;
            Report();
        }

        if (Time.unscaledTime >= nextSave)
        {
            nextSave = Time.unscaledTime + saveEverySeconds;
            SaveAll();
        }
    }

    /// <summary>El nivel (sus ajustes), o null si no está cargado en este BotLearning.</summary>
    public BotLevelProfile GetProfile(RaceDifficulty difficulty)
    {
        foreach (BotLevelProfile level in levels)
        {
            if (level != null && level.difficulty == difficulty)
                return level;
        }

        return null;
    }

    /// <summary>
    /// Prepara la tabla de un nivel para los bots que la van a usar: la carga
    /// de su JSON si coincide el armado (layout), o la arma vacía.
    /// </summary>
    public bool Prepare(RaceDifficulty difficulty, int states, int actions, string layout)
    {
        if (brains.TryGetValue(difficulty, out Brain existing))
        {
            if (existing.Layout == layout)
                return true;

            Debug.LogError($"Bots {difficulty}: hay bots con otro armado de la tabla ({layout} y {existing.Layout}). " +
                           "¿Corren en pistas distintas?", this);
            return false;
        }

        BotLevelProfile profile = GetProfile(difficulty);

        if (profile == null)
        {
            Debug.LogError($"BotLearning no tiene el nivel {difficulty} (BotLevelProfile).", this);
            return false;
        }

        var brain = new Brain
        {
            Profile = profile,
            Layout = layout,
            States = states,
            Actions = actions,
            Q = new float[states * actions],
            Visits = new int[states],
            Primed = new bool[states],
            Exploration = startExploration
        };

        BotQTableData data = Load(profile);

        if (data != null && data.version == Version && SameLayout(data.layout, layout) &&
            data.values != null && data.values.Length == brain.Q.Length)
        {
            Array.Copy(data.values, brain.Q, brain.Q.Length);

            if (data.visits != null && data.visits.Length == states)
                Array.Copy(data.visits, brain.Visits, states);

            if (data.exploration >= 0f)
                brain.Exploration = Mathf.Clamp(data.exploration, minExploration, startExploration);

            brain.Data = data;

            Debug.Log($"BOTS {profile.displayName}: tabla cargada ({CountVisited(brain)} de {states} situaciones aprendidas, " +
                      $"{data.cleanLaps} vueltas limpias en el entrenamiento).", this);
        }
        else
        {
            brain.Data = new BotQTableData();

            if (data != null && data.values != null && data.values.Length > 0)
                Debug.LogWarning($"BOTS {profile.displayName}: la tabla guardada es de otra pista o de otra versión de los bots; se empieza de cero.", this);
            else
                Debug.Log($"BOTS {profile.displayName}: tabla nueva, todavía no aprendió nada.", this);
        }

        brains[difficulty] = brain;
        return true;
    }

    /// <summary>True si de esa situación todavía no se aprendió nada (para darle un valor de arranque).</summary>
    public bool NeedsPrior(RaceDifficulty difficulty, int state)
    {
        return brains.TryGetValue(difficulty, out Brain brain) && IsValid(brain, state) &&
               brain.Visits[state] == 0 && !brain.Primed[state];
    }

    /// <summary>Valores de arranque de una situación nueva (una idea aproximada de qué conviene, que el entrenamiento corrige).</summary>
    public void SetPrior(RaceDifficulty difficulty, int state, float[] values)
    {
        if (!brains.TryGetValue(difficulty, out Brain brain) || !IsValid(brain, state) || values == null)
            return;

        Array.Copy(values, 0, brain.Q, state * brain.Actions, Mathf.Min(values.Length, brain.Actions));
        brain.Primed[state] = true;
    }

    /// <summary>Cuántas veces aprendió de esa situación (0 si no está cargada).</summary>
    public int GetVisits(RaceDifficulty difficulty, int state)
    {
        return brains.TryGetValue(difficulty, out Brain brain) && IsValid(brain, state) ? brain.Visits[state] : 0;
    }

    /// <summary>La mejor acción para esa situación; entrenando, a veces una al azar.</summary>
    public int ChooseAction(RaceDifficulty difficulty, int state)
    {
        if (!brains.TryGetValue(difficulty, out Brain brain) || !IsValid(brain, state))
            return 0;

        if (IsTraining && UnityEngine.Random.value < brain.Exploration)
            return UnityEngine.Random.Range(0, brain.Actions);

        return BestAction(brain, state);
    }

    /// <summary>Q-learning: corrige el valor de lo que hizo según lo que pasó después.</summary>
    public void Learn(RaceDifficulty difficulty, int state, int action, float reward, int nextState, bool terminal)
    {
        if (!IsTraining || !brains.TryGetValue(difficulty, out Brain brain))
            return;

        if (!IsValid(brain, state) || action < 0 || action >= brain.Actions)
            return;

        float target = reward;

        if (!terminal && IsValid(brain, nextState))
            target += discount * brain.Q[nextState * brain.Actions + BestAction(brain, nextState)];

        int index = state * brain.Actions + action;
        brain.Q[index] += learningRate * (target - brain.Q[index]);
        brain.Visits[state]++;
        brain.Exploration = Mathf.Max(minExploration, brain.Exploration * explorationDecay);
        brain.Data.decisions++;
        brain.Dirty = true;
    }

    /// <summary>Un bot de ese nivel dio una vuelta entera sin incidentes.</summary>
    public void ReportLap(RaceDifficulty difficulty, float lapTime)
    {
        if (!brains.TryGetValue(difficulty, out Brain brain))
            return;

        brain.Data.cleanLaps++;

        if (brain.Data.bestLapTime <= 0f || lapTime < brain.Data.bestLapTime)
            brain.Data.bestLapTime = lapTime;

        brain.RecentLapSum += lapTime;
        brain.RecentLaps++;
    }

    public void ReportIncident(RaceDifficulty difficulty)
    {
        if (brains.TryGetValue(difficulty, out Brain brain))
            brain.Data.incidents++;
    }

    public void ReportDistance(RaceDifficulty difficulty, float meters)
    {
        if (brains.TryGetValue(difficulty, out Brain brain) && meters > 0f)
            brain.Data.kilometers += meters / 1000f;
    }

    /// <summary>Guarda lo aprendido por cada nivel en su JSON.</summary>
    public void SaveAll()
    {
        foreach (Brain brain in brains.Values)
        {
            if (brain.Dirty)
                Save(brain);
        }
    }

    private void Report()
    {
        foreach (Brain brain in brains.Values)
        {
            BotQTableData data = brain.Data;
            float incidentsPerKm = data.kilometers > 0.01f ? data.incidents / data.kilometers : 0f;
            string recent = brain.RecentLaps > 0 ? FormatTime(brain.RecentLapSum / brain.RecentLaps) : "-";
            string best = data.bestLapTime > 0f ? FormatTime(data.bestLapTime) : "-";

            Debug.Log($"ENTRENAMIENTO {brain.Profile.displayName}: {CountVisited(brain)}/{brain.States} situaciones | " +
                      $"exploración {brain.Exploration:P0} | {data.cleanLaps} vueltas limpias (último minuto: {brain.RecentLaps}, " +
                      $"promedio {recent}; mejor {best}) | {incidentsPerKm:F2} incidentes por km en {data.kilometers:F1} km", this);

            brain.RecentLapSum = 0f;
            brain.RecentLaps = 0;
        }
    }

    private static string FormatTime(float seconds)
    {
        int minutes = Mathf.FloorToInt(seconds / 60f);
        return $"{minutes}:{seconds - minutes * 60f:00.0}";
    }

    private static bool IsValid(Brain brain, int state) => state >= 0 && state < brain.States;

    // Las primeras tablas se guardaron con coma decimal ("15,0"): valen igual.
    private static bool SameLayout(string saved, string current)
    {
        return saved != null && saved.Replace(',', '.') == current.Replace(',', '.');
    }

    private static int BestAction(Brain brain, int state)
    {
        int offset = state * brain.Actions;
        int best = 0;

        for (int i = 1; i < brain.Actions; i++)
        {
            if (brain.Q[offset + i] > brain.Q[offset + best])
                best = i;
        }

        return best;
    }

    private static int CountVisited(Brain brain)
    {
        int visited = 0;

        foreach (int visits in brain.Visits)
        {
            if (visits > 0)
                visited++;
        }

        return visited;
    }

    private BotQTableData Load(BotLevelProfile profile)
    {
        string json = null;

#if UNITY_EDITOR
        // En el editor se lee el archivo: puede ser más nuevo que el TextAsset importado.
        string path = GetSavePath(profile);

        if (File.Exists(path))
            json = File.ReadAllText(path);
#endif

        if (string.IsNullOrEmpty(json) && profile.qTable != null)
            json = profile.qTable.text;

        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonUtility.FromJson<BotQTableData>(json);
        }
        catch (Exception e)
        {
            Debug.LogError($"No se pudo leer la tabla de {profile.displayName}: {e.Message}", this);
            return null;
        }
    }

    private void Save(Brain brain)
    {
        BotQTableData data = brain.Data;
        data.version = Version;
        data.layout = brain.Layout;
        data.exploration = brain.Exploration;
        data.values = brain.Q;
        data.visits = brain.Visits;

        string path = GetSavePath(brain.Profile);

        try
        {
            string folder = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            File.WriteAllText(path, JsonUtility.ToJson(data));
            brain.Dirty = false;

            Debug.Log($"BOTS {brain.Profile.displayName}: tabla guardada ({CountVisited(brain)}/{brain.States} situaciones, " +
                      $"exploración {brain.Exploration:P0}).", this);

#if UNITY_EDITOR
            string assetPath = GetAssetPath(brain.Profile);

            if (!string.IsNullOrEmpty(assetPath))
                UnityEditor.AssetDatabase.ImportAsset(assetPath);
#endif
        }
        catch (Exception e)
        {
            Debug.LogError($"No se pudo guardar la tabla de {brain.Profile.displayName}: {e.Message}", this);
        }
    }

    private static string GetSavePath(BotLevelProfile profile)
    {
#if UNITY_EDITOR
        string assetPath = GetAssetPath(profile);
        string projectFolder = Directory.GetParent(Application.dataPath).FullName;
        return Path.Combine(projectFolder, assetPath);
#else
        return Path.Combine(Application.persistentDataPath, "ApexRun_QTable_" + profile.difficulty + ".json");
#endif
    }

#if UNITY_EDITOR
    private static string GetAssetPath(BotLevelProfile profile)
    {
        string assetPath = profile.qTable != null ? UnityEditor.AssetDatabase.GetAssetPath(profile.qTable) : null;
        return string.IsNullOrEmpty(assetPath) ? "Assets/AIData/ApexRun_QTable_" + profile.difficulty + ".json" : assetPath;
    }
#endif
}
