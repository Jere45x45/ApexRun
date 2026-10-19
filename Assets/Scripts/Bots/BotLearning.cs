
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class QEntry
{
    public string state;
    public float[] values;

    public QEntry(string state, float[] values)
    {
        this.state = state;
        this.values = values;
    }
}

[Serializable]
public class QTableData
{
    public float exploration = -1f;
    public List<QEntry> entries = new List<QEntry>();
}

public class BotLearning : MonoBehaviour
{
    public static BotLearning Instance { get; private set; }

    public const int ActionCount = 3;

    [Header("Aprendizaje")]
    [SerializeField] private float learningRate = 0.30f;
    [SerializeField] private float discount = 0.95f;

    [Header("Exploracion")]
    [SerializeField, Range(0f, 1f)]
    private float startExploration = 0.30f;

    [SerializeField, Range(0f, 1f)]
    private float minExploration = 0.02f;

    [SerializeField, Range(0.99f, 1f)]
    private float explorationDecay = 0.99995f;

    [Header("Entrenamiento")]
    [Tooltip("Desactivado: usa la tabla sin aprender. Activado: permite aprender.")]
    [SerializeField] private bool inferenceOnly = true;

    [SerializeField, Range(1f, 20f)]
    private float trainingTimeScale = 1f;

    [Header("Guardado")]
    [SerializeField] private int saveEveryDecisions = 5000;

    private readonly Dictionary<int, float[]> qTable =
        new Dictionary<int, float[]>();

    private float exploration;
    private int decisionsSinceSave;
    private int currentDifficulty = 1;
    private string folderPath;
    private string savePath;

    public float Exploration => exploration;
    public bool IsTraining => !inferenceOnly;
    public int CurrentDifficulty => currentDifficulty;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError(
                "Hay mas de un BotLearning en la escena. Deja solo uno.",
                this
            );
            Destroy(this);
            return;
        }

        Instance = this;

        folderPath = Path.Combine(Application.dataPath, "AIData");

        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);

        exploration = startExploration;

        Time.timeScale = inferenceOnly ? 1f : trainingTimeScale;

        LoadDifficultyTable(1);
    }

    private void OnDestroy()
    {
        if (Instance != this)
            return;

        if (!inferenceOnly)
            SaveQTable();

        Time.timeScale = 1f;
        Instance = null;
    }

    private void OnApplicationQuit()
    {
        if (!inferenceOnly)
            SaveQTable();
    }

    public void SetDifficultyTable(int level)
    {
        if (level < 1 || level > 3)
        {
            Debug.LogError("Dificultad invalida. Usa 1, 2 o 3.", this);
            return;
        }

        if (level == currentDifficulty && savePath != null)
            return;

        if (!inferenceOnly)
            SaveQTable();

        LoadDifficultyTable(level);
    }

    private void LoadDifficultyTable(int level)
    {
        currentDifficulty = level;

        string fileName;

        switch (level)
        {
            case 1:
                fileName = "ApexRun_QTable_Easy.json";
                break;
            case 2:
                fileName = "ApexRun_QTable_Medium.json";
                break;
            default:
                fileName = "ApexRun_QTable_Hard.json";
                break;
        }

        savePath = Path.Combine(folderPath, fileName);

        qTable.Clear();
        decisionsSinceSave = 0;
        exploration = startExploration;

        if (!File.Exists(savePath))
        {
            Debug.Log(
                "Q-TABLE NUEVA | Nivel: " + level +
                " | Archivo: " + fileName,
                this
            );
            return;
        }

        try
        {
            QTableData data =
                JsonUtility.FromJson<QTableData>(
                    File.ReadAllText(savePath)
                );

            if (data == null || data.entries == null)
            {
                Debug.LogError("El archivo de la Q-table no es valido.", this);
                return;
            }

            foreach (QEntry entry in data.entries)
            {
                if (entry != null &&
                    int.TryParse(entry.state, out int state) &&
                    entry.values != null &&
                    entry.values.Length == ActionCount)
                {
                    qTable[state] = entry.values;
                }
            }

            if (data.exploration >= 0f)
            {
                exploration = Mathf.Clamp(
                    data.exploration,
                    minExploration,
                    startExploration
                );
            }

            Debug.Log(
                "Q-TABLE CARGADA | Nivel: " + level +
                " | Estados: " + qTable.Count +
                " | Archivo: " + fileName,
                this
            );
        }
        catch (Exception e)
        {
            Debug.LogError(
                "No se pudo cargar la Q-table: " + e.Message,
                this
            );
        }
    }

    public int ChooseAction(int state)
    {
        float[] values = GetValues(state);

        if (!inferenceOnly &&
            UnityEngine.Random.value < exploration)
        {
            return UnityEngine.Random.Range(0, ActionCount);
        }

        return BestAction(values);
    }

    public void Learn(
        int state,
        int action,
        float reward,
        int nextState,
        bool terminal)
    {
        if (inferenceOnly)
            return;

        if (action < 0 || action >= ActionCount)
            return;

        float[] values = GetValues(state);
        float target = reward;

        if (!terminal)
        {
            float[] nextValues = GetValues(nextState);
            target += discount * nextValues[BestAction(nextValues)];
        }

        values[action] += learningRate * (target - values[action]);

        exploration = Mathf.Max(
            minExploration,
            exploration * explorationDecay
        );

        decisionsSinceSave++;

        if (decisionsSinceSave >= Mathf.Max(1, saveEveryDecisions))
        {
            SaveQTable();
            decisionsSinceSave = 0;
        }
    }

    private float[] GetValues(int state)
    {
        if (!qTable.TryGetValue(state, out float[] values))
        {
            values = new float[ActionCount];
            qTable[state] = values;
        }

        return values;
    }

    private static int BestAction(float[] values)
    {
        int best = 0;

        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] > values[best])
                best = i;
        }

        return best;
    }

    public void SaveQTable()
    {
        if (string.IsNullOrEmpty(savePath))
            return;

        try
        {
            QTableData data = new QTableData
            {
                exploration = exploration
            };

            foreach (var entry in qTable)
            {
                data.entries.Add(
                    new QEntry(
                        entry.Key.ToString(),
                        entry.Value
                    )
                );
            }

            File.WriteAllText(
                savePath,
                JsonUtility.ToJson(data, true)
            );

            Debug.Log(
                "Q-TABLE GUARDADA | Nivel: " + currentDifficulty +
                " | Estados: " + qTable.Count +
                " | Exploracion: " + exploration.ToString("0.000") +
                " | Archivo: " + Path.GetFileName(savePath),
                this
            );
        }
        catch (Exception e)
        {
            Debug.LogError(
                "No se pudo guardar la Q-table: " + e.Message,
                this
            );
        }
    }

    public int GetStateCount()
    {
        return qTable.Count;
    }
}
