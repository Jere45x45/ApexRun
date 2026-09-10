using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class RacePositionManager : MonoBehaviour
{
    [SerializeField]
    private RaceCheckpointManager checkpointManager;

    [SerializeField]
    private RaceLapManager lapManager;

    [SerializeField]
    private Rigidbody[] raceKarts;

    private readonly Dictionary<Rigidbody, int>
        positionByKart =
            new Dictionary<Rigidbody, int>();

    public int GetPosition(
        Rigidbody kart)
    {
        if (kart == null)
            return 0;

        if (!positionByKart.TryGetValue(
                kart,
                out int position))
        {
            UpdatePositions();
        }

        if (positionByKart.TryGetValue(
                kart,
                out position))
        {
            return position;
        }

        return 0;
    }

    public void UpdatePositions()
    {
        if (checkpointManager == null ||
            lapManager == null)
        {
            return;
        }

        if (raceKarts == null ||
            raceKarts.Length == 0)
        {
            return;
        }

        List<KartProgress> progressList =
            new List<KartProgress>();

        foreach (Rigidbody kart in raceKarts)
        {
            if (kart == null)
                continue;

            progressList.Add(
                CalculateProgress(kart)
            );
        }

        progressList.Sort(
            (a, b) =>
                b.Progress.CompareTo(
                    a.Progress
                )
        );

        positionByKart.Clear();

        for (int i = 0;
             i < progressList.Count;
             i++)
        {
            positionByKart[
                progressList[i].Kart
            ] = i + 1;
        }
    }

    private KartProgress CalculateProgress(
        Rigidbody kart)
    {
        int lap =
            lapManager.GetCurrentLap(kart);

        int lastCheckpoint =
            checkpointManager.GetLastCheckpoint(
                kart
            );

        int nextCheckpoint =
            checkpointManager.GetNextCheckpoint(
                kart
            );

        float segmentProgress =
            CalculateSegmentProgress(
                kart,
                nextCheckpoint
            );

        float progress =
            (
                (lap - 1) *
                checkpointManager.CheckpointCount
            )
            +
            lastCheckpoint
            +
            segmentProgress;

        if (lastCheckpoint < 0)
        {
            progress =
                segmentProgress - 1f;
        }

        return new KartProgress(
            kart,
            progress
        );
    }

    private float CalculateSegmentProgress(
        Rigidbody kart,
        int nextCheckpointIndex)
    {
        RaceCheckpoint nextCheckpoint =
            checkpointManager.GetCheckpoint(
                nextCheckpointIndex
            );

        if (nextCheckpoint == null)
            return 0f;

        int previousCheckpointIndex =
            nextCheckpointIndex - 1;

        if (previousCheckpointIndex < 0)
        {
            previousCheckpointIndex =
                checkpointManager.CheckpointCount - 1;
        }

        RaceCheckpoint previousCheckpoint =
            checkpointManager.GetCheckpoint(
                previousCheckpointIndex
            );

        if (previousCheckpoint == null)
            return 0f;

        Vector3 start =
            previousCheckpoint.transform.position;

        Vector3 end =
            nextCheckpoint.transform.position;

        Vector3 segment =
            end - start;

        float segmentLengthSquared =
            segment.sqrMagnitude;

        if (segmentLengthSquared < 0.0001f)
            return 0f;

        float projection =
            Vector3.Dot(
                kart.position - start,
                segment
            );

        float normalized =
            projection /
            segmentLengthSquared;

        return Mathf.Clamp01(
            normalized
        );
    }

    private struct KartProgress
    {
        public Rigidbody Kart;
        public float Progress;

        public KartProgress(
            Rigidbody kart,
            float progress)
        {
            Kart = kart;
            Progress = progress;
        }
    }
}