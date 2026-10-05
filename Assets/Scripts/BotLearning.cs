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
    public List<QEntry> entries = new List<QEntry>();
}

public class BotLearning : MonoBehaviour
{
    public static BotLearning Instance { get; private set; }

    [Header("Learning")]
    [SerializeField] private float learningRate = 0.30f;
    [SerializeField] private float discount = 0.95f;
    [SerializeField] private float exploration = 0.10f;

    [Header("Auto Save")]
    [SerializeField] private int saveEveryDecisions = 5000;

    private int decisionsSinceSave = 0;

    private Dictionary<string, float[]> qTable =
        new Dictionary<string, float[]>();

    private string savePath;

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
            "ApexRun_QTable.json"
        );

        LoadQTable();
    }

    private void OnApplicationQuit()
    {
        SaveQTable();
    }

    public int ChooseAction(float[] observations)
    {
        string state = GetState(observations);

        if (!qTable.ContainsKey(state))
        {
            qTable[state] = new float[3];
        }

        if (UnityEngine.Random.value < exploration)
        {
            return UnityEngine.Random.Range(0, 3);
        }

        float[] values = qTable[state];

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

    public void Learn(
        float[] observations,
        int action,
        float reward,
        float[] nextObservations)
    {
        string state = GetState(observations);
        string nextState = GetState(nextObservations);

        if (!qTable.ContainsKey(state))
            qTable[state] = new float[3];

        if (!qTable.ContainsKey(nextState))
            qTable[nextState] = new float[3];

        float currentValue = qTable[state][action];

        float bestNextValue = qTable[nextState][0];

        for (int i = 1; i < 3; i++)
        {
            if (qTable[nextState][i] > bestNextValue)
            {
                bestNextValue = qTable[nextState][i];
            }
        }

        float newValue =
            currentValue +
            learningRate *
            (reward +
             discount * bestNextValue -
             currentValue);

        qTable[state][action] = newValue;
        decisionsSinceSave++;

        if (decisionsSinceSave >= saveEveryDecisions)
        {
            SaveQTable();
            decisionsSinceSave = 0;
        }
    }

    private string GetState(float[] observations)
    {
        string state = "";

        for (int i = 0; i < observations.Length; i++)
        {
            int value = Mathf.Clamp(
                Mathf.RoundToInt(observations[i] * 4f),
                0,
                4
            );

            state += value.ToString();

            if (i < observations.Length - 1)
                state += "_";
        }

        return state;
    }

    private void SaveQTable()
    {
        QTableData data = new QTableData();

        foreach (var entry in qTable)
        {
            data.entries.Add(
                new QEntry(
                    entry.Key,
                    entry.Value
                )
            );
        }

        string json = JsonUtility.ToJson(
            data,
            true
        );

        File.WriteAllText(
            savePath,
            json
        );

        Debug.Log(
            "Q-TABLE GUARDADA | Estados: " +
            qTable.Count
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

        string json = File.ReadAllText(savePath);

        QTableData data =
            JsonUtility.FromJson<QTableData>(json);

        qTable.Clear();

        foreach (QEntry entry in data.entries)
        {
            qTable[entry.state] = entry.values;
        }

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