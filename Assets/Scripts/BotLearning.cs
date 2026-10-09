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

/// <summary>
/// Cerebro compartido de los bots (Q-learning con tabla). Todos los bots de la
/// escena usan la misma tabla, así que entrenar varios a la vez la llena más
/// rápido.
/// - Estado: qué sensores ven pista (sí/no), armado como un número.
/// - Acciones: 0 = izquierda, 1 = derecho, 2 = derecha.
/// </summary>
public class BotLearning : MonoBehaviour
{
    public static BotLearning Instance { get; private set; }

    private const int ActionCount = 3;

    [Header("Learning")]
    [SerializeField] private float learningRate = 0.30f;
    [SerializeField] private float discount = 0.95f;

    [Header("Exploración (probar acciones al azar)")]
    [Tooltip("Probabilidad de probar una acción al azar al empezar una tabla nueva.")]
    [SerializeField, Range(0f, 1f)] private float startExploration = 0.30f;

    [Tooltip("Lo mínimo que sigue explorando cuando ya aprendió.")]
    [SerializeField, Range(0f, 1f)] private float minExploration = 0.02f;

    [Tooltip("Cuánto baja la exploración en cada decisión (0.9995 ≈ a la mitad cada 1400 decisiones).")]
    [SerializeField, Range(0.99f, 1f)] private float explorationDecay = 0.9995f;

    [Header("Entrenamiento")]
    [Tooltip("Velocidad del tiempo mientras se entrena (1 = normal). Con 5 aprende unas 5 veces más rápido si la compu aguanta.")]
    [SerializeField, Range(1f, 20f)] private float trainingTimeScale = 1f;

    [Header("Auto Save")]
    [SerializeField] private int saveEveryDecisions = 5000;

    // Archivo nuevo: los estados de antes (con alturas) no sirven para los de ahora (sí/no).
    private const string FileName = "ApexRun_QTable_v2.json";

    private int decisionsSinceSave = 0;

    private float exploration;

    private readonly Dictionary<int, float[]> qTable =
        new Dictionary<int, float[]>();

    private string savePath;

    public float Exploration => exploration;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        string folderPath = Path.Combine(
            Application.dataPath,
            "AIData"
        );

        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        savePath = Path.Combine(
            folderPath,
            FileName
        );

        exploration = startExploration;

        LoadQTable();

        Time.timeScale = trainingTimeScale;
    }

    private void OnDestroy()
    {
        if (Instance != this)
            return;

        Instance = null;
        Time.timeScale = 1f;
    }

    private void OnApplicationQuit()
    {
        SaveQTable();
    }

    public int ChooseAction(float[] observations)
    {
        float[] values = GetValues(GetState(observations));

        if (UnityEngine.Random.value < exploration)
        {
            return UnityEngine.Random.Range(0, ActionCount);
        }

        return BestAction(values);
    }

    public void Learn(
        float[] observations,
        int action,
        float reward,
        float[] nextObservations)
    {
        Learn(observations, action, reward, nextObservations, false);
    }

    /// <summary>
    /// Actualiza la tabla. Con terminal = true (se cayó, se trabó o terminó)
    /// no se suma lo que vendría después: el episodio terminó ahí.
    /// </summary>
    public void Learn(
        float[] observations,
        int action,
        float reward,
        float[] nextObservations,
        bool terminal)
    {
        if (observations == null)
            return;

        float[] values = GetValues(GetState(observations));

        float target = reward;

        if (!terminal && nextObservations != null)
        {
            float[] next = GetValues(GetState(nextObservations));
            target += discount * next[BestAction(next)];
        }

        values[action] += learningRate * (target - values[action]);

        exploration = Mathf.Max(minExploration, exploration * explorationDecay);

        decisionsSinceSave++;

        if (decisionsSinceSave >= saveEveryDecisions)
        {
            SaveQTable();
            decisionsSinceSave = 0;
        }
    }

    /// <summary>Cada sensor que ve pista prende un bit: 9 sensores = 512 estados posibles.</summary>
    private static int GetState(float[] observations)
    {
        int state = 0;

        for (int i = 0; i < observations.Length; i++)
        {
            if (observations[i] > 0.5f)
                state |= 1 << i;
        }

        return state;
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
        int bestAction = 0;

        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] > values[bestAction])
            {
                bestAction = i;
            }
        }

        return bestAction;
    }

    private void SaveQTable()
    {
        if (string.IsNullOrEmpty(savePath))
            return;

        QTableData data = new QTableData { exploration = exploration };

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
            "Q-TABLE GUARDADA | Estados: " +
            qTable.Count +
            " | Exploración: " +
            exploration.ToString("0.000")
        );
    }

    private void LoadQTable()
    {
        if (!File.Exists(savePath))
        {
            Debug.Log(
                "Q-TABLE NUEVA. El entrenamiento empieza desde cero."
            );

            return;
        }

        QTableData data =
            JsonUtility.FromJson<QTableData>(File.ReadAllText(savePath));

        qTable.Clear();

        foreach (QEntry entry in data.entries)
        {
            if (int.TryParse(entry.state, out int state) && entry.values != null && entry.values.Length == ActionCount)
                qTable[state] = entry.values;
        }

        // Una tabla que ya aprendió sigue con la exploración donde quedó.
        if (data.exploration >= 0f)
            exploration = Mathf.Clamp(data.exploration, minExploration, startExploration);

        Debug.Log(
            "Q-TABLE CARGADA | Estados: " +
            qTable.Count
        );
    }

    public int GetStateCount()
    {
        return qTable.Count;
    }
}
