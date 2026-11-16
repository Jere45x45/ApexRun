using System;
using System.Collections.Generic;
using UnityEngine;

public class RaceCheckpointManager : MonoBehaviour
{
    [SerializeField]
    private RaceCheckpoint[] checkpoints;

    private readonly Dictionary<Rigidbody, int>
        nextCheckpointByKart =
            new Dictionary<Rigidbody, int>();

    private readonly Dictionary<Rigidbody, int>
        lastCheckpointByKart =
            new Dictionary<Rigidbody, int>();

    public int CheckpointCount =>
        checkpoints != null
            ? checkpoints.Length
            : 0;

    public event Action<Rigidbody, int>
        CheckpointPassed;

    // Pista al revés (ver SetReversed): de qué checkpoint de la escena es
    // cada lugar del orden de carrera, y al revés. Null = al derecho.
    private RaceCheckpoint[] checkpointByOrder;
    private int[] orderByIndex;

    /// <summary>True si los checkpoints se cuentan en el orden contrario.</summary>
    public bool IsReversed => checkpointByOrder != null;

    /// <summary>
    /// Pista al revés (TrackDirection): los checkpoints se pasan en el orden
    /// contrario. El último sigue siendo la llegada (cuenta la vuelta); el que
    /// estaba justo antes de la llegada pasa a ser el primero, y así.
    /// Todo lo demás (vueltas, posiciones, reaparición) sigue hablando del orden
    /// de carrera: GetCheckpoint(0) es el primero que se pasa.
    /// </summary>
    public void SetReversed(bool reversed)
    {
        if (!reversed)
        {
            checkpointByOrder = null;
            orderByIndex = null;
            return;
        }

        int count = CheckpointCount;

        if (count == 0)
            return;

        checkpointByOrder = new RaceCheckpoint[count];
        orderByIndex = new int[count];

        foreach (RaceCheckpoint checkpoint in checkpoints)
        {
            if (checkpoint == null)
                continue;

            int index = checkpoint.CheckpointIndex;

            if (index < 0 || index >= count)
                continue;

            int order = index == count - 1 ? count - 1 : count - 2 - index;

            orderByIndex[index] = order;
            checkpointByOrder[order] = checkpoint;
        }
    }

    private void Awake()
    {
        ValidateCheckpoints();
    }

    public void TryPassCheckpoint(
        Rigidbody kart,
        int checkpointIndex)
    {
        if (kart == null)
            return;

        // Las vueltas las cuenta el servidor (el host).
        if (NetworkRole.IsClientOnly)
            return;

        if (checkpointIndex < 0 ||
            checkpointIndex >= CheckpointCount)
        {
            return;
        }

        // Al revés: el número del checkpoint de la escena pasa a su lugar en
        // el orden de carrera.
        if (orderByIndex != null)
            checkpointIndex = orderByIndex[checkpointIndex];

        int expectedCheckpoint =
            GetNextCheckpoint(kart);

        if (checkpointIndex !=
            expectedCheckpoint)
        {
            return;
        }

        lastCheckpointByKart[kart] =
            checkpointIndex;

        int nextCheckpoint =
            checkpointIndex + 1;

        if (nextCheckpoint >=
            CheckpointCount)
        {
            nextCheckpoint = 0;
        }

        nextCheckpointByKart[kart] =
            nextCheckpoint;

        CheckpointPassed?.Invoke(
            kart,
            checkpointIndex
        );
    }

    public int GetNextCheckpoint(
        Rigidbody kart)
    {
        if (kart == null)
            return 0;

        if (!nextCheckpointByKart.TryGetValue(
                kart,
                out int nextCheckpoint))
        {
            nextCheckpoint = 0;

            nextCheckpointByKart[kart] =
                nextCheckpoint;

            lastCheckpointByKart[kart] =
                -1;
        }

        return nextCheckpoint;
    }

    public int GetLastCheckpoint(
        Rigidbody kart)
    {
        if (kart == null)
            return -1;

        if (!lastCheckpointByKart.TryGetValue(
                kart,
                out int lastCheckpoint))
        {
            return -1;
        }

        return lastCheckpoint;
    }

    public RaceCheckpoint GetCheckpoint(
        int checkpointIndex)
    {
        if (checkpointIndex < 0 ||
            checkpointIndex >= CheckpointCount)
        {
            return null;
        }

        if (checkpointByOrder != null)
            return checkpointByOrder[checkpointIndex];

        return checkpoints[
            checkpointIndex
        ];
    }

    public void RegisterKart(
        Rigidbody kart)
    {
        if (kart == null)
            return;

        if (nextCheckpointByKart.ContainsKey(kart))
            return;

        nextCheckpointByKart[kart] =
            0;

        lastCheckpointByKart[kart] =
            -1;
    }

    public void UnregisterKart(
        Rigidbody kart)
    {
        if (kart == null)
            return;

        nextCheckpointByKart.Remove(
            kart
        );

        lastCheckpointByKart.Remove(
            kart
        );
    }

    private void ValidateCheckpoints()
    {
        if (checkpoints == null ||
            checkpoints.Length == 0)
        {
            Debug.LogError(
                "RaceCheckpointManager no tiene checkpoints asignados.",
                this
            );

            return;
        }

        HashSet<int> indexes =
            new HashSet<int>();

        for (int i = 0;
             i < checkpoints.Length;
             i++)
        {
            RaceCheckpoint checkpoint =
                checkpoints[i];

            if (checkpoint == null)
            {
                Debug.LogError(
                    $"El checkpoint en la posición {i} es null.",
                    this
                );

                continue;
            }

            if (!indexes.Add(
                    checkpoint.CheckpointIndex))
            {
                Debug.LogError(
                    $"Hay checkpoints con el mismo índice: " +
                    $"{checkpoint.CheckpointIndex}.",
                    checkpoint
                );
            }
        }

        for (int i = 0;
             i < checkpoints.Length;
             i++)
        {
            if (!indexes.Contains(i))
            {
                Debug.LogError(
                    $"Falta el checkpoint con índice {i}.",
                    this
                );
            }
        }
    }
}
