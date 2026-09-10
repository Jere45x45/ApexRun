using System;
using System.Collections.Generic;
using UnityEngine;

public class RaceLapManager : MonoBehaviour
{
    [Header("Race")]
    [SerializeField]
    [Min(1)]
    private int totalLaps = 3;

    [Header("References")]
    [SerializeField]
    private RaceCheckpointManager checkpointManager;

    private readonly Dictionary<Rigidbody, int>
        currentLapByKart =
            new Dictionary<Rigidbody, int>();

    private readonly HashSet<Rigidbody>
        finishedKarts =
            new HashSet<Rigidbody>();

    public int TotalLaps =>
        totalLaps;

    public event Action<Rigidbody>
        KartFinished;

    private void OnEnable()
    {
        if (checkpointManager != null)
        {
            checkpointManager.CheckpointPassed +=
                HandleCheckpointPassed;
        }
    }

    private void OnDisable()
    {
        if (checkpointManager != null)
        {
            checkpointManager.CheckpointPassed -=
                HandleCheckpointPassed;
        }
    }

    private void Start()
    {
        if (checkpointManager == null)
        {
            Debug.LogError(
                "RaceLapManager necesita un RaceCheckpointManager.",
                this
            );
        }
    }

    public void RegisterKart(
        Rigidbody kart)
    {
        if (kart == null)
            return;

        if (currentLapByKart.ContainsKey(kart))
            return;

        currentLapByKart[kart] =
            1;

        finishedKarts.Remove(kart);

        if (checkpointManager != null)
        {
            checkpointManager.RegisterKart(
                kart
            );
        }
    }

    public void UnregisterKart(
        Rigidbody kart)
    {
        if (kart == null)
            return;

        currentLapByKart.Remove(
            kart
        );

        finishedKarts.Remove(kart);

        if (checkpointManager != null)
        {
            checkpointManager.UnregisterKart(
                kart
            );
        }
    }

    public int GetCurrentLap(
        Rigidbody kart)
    {
        if (kart == null)
            return 0;

        if (!currentLapByKart.TryGetValue(
                kart,
                out int currentLap))
        {
            return 1;
        }

        return currentLap;
    }

    public bool IsFinished(
        Rigidbody kart)
    {
        if (kart == null)
            return false;

        return finishedKarts.Contains(kart);
    }

    private void HandleCheckpointPassed(
        Rigidbody kart,
        int checkpointIndex)
    {
        if (kart == null)
            return;

        if (!currentLapByKart.ContainsKey(kart))
        {
            RegisterKart(kart);
        }

        if (finishedKarts.Contains(kart))
            return;

        if (checkpointManager == null)
            return;

        int lastCheckpointIndex =
            checkpointManager.CheckpointCount - 1;

        if (checkpointIndex !=
            lastCheckpointIndex)
        {
            return;
        }

        CompleteLap(kart);
    }

    private void CompleteLap(
        Rigidbody kart)
    {
        int currentLap =
            GetCurrentLap(kart);

        if (currentLap >= totalLaps)
        {
            finishedKarts.Add(kart);

            Debug.Log(
                $"[{kart.name}] terminó la carrera.",
                kart
            );

            KartFinished?.Invoke(
                kart
            );

            return;
        }

        currentLap++;

        currentLapByKart[kart] =
            currentLap;

        Debug.Log(
            $"[{kart.name}] completó una vuelta. " +
            $"Ahora está en la vuelta {currentLap}/{totalLaps}.",
            kart
        );
    }
}