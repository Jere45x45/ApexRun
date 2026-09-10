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

        if (checkpointIndex < 0 ||
            checkpointIndex >= CheckpointCount)
        {
            return;
        }

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