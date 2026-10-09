using System.Collections.Generic;
using UnityEngine;

public class RacePositionManager : MonoBehaviour
{
    [SerializeField]
    private RaceCheckpointManager checkpointManager;

    [SerializeField]
    private RaceLapManager lapManager;

    [Tooltip("Línea central de la pista. Mide cuánto avanzó cada kart entre dos checkpoints siguiendo la pista. Sin ella, se usa la recta entre checkpoints.")]
    [SerializeField]
    private TrackCenterline centerline;

    // Los registra RaceManager cuando aparecen (solo en el servidor).
    private readonly List<Rigidbody> raceKarts =
        new List<Rigidbody>();

    private readonly Dictionary<Rigidbody, int>
        positionByKart =
            new Dictionary<Rigidbody, int>();

    // Karts que terminaron, en el orden en que llegaron.
    private readonly List<Rigidbody>
        finishOrder =
            new List<Rigidbody>();

    // Distancia de cada checkpoint sobre la línea central.
    private float[] checkpointDistances;

    public int KartCount =>
        positionByKart.Count;

    public void RegisterKart(
        Rigidbody kart)
    {
        if (kart != null && !raceKarts.Contains(kart))
            raceKarts.Add(kart);
    }

    public void UnregisterKart(
        Rigidbody kart)
    {
        raceKarts.Remove(kart);
        positionByKart.Remove(kart);
    }

    private void OnEnable()
    {
        if (lapManager != null)
        {
            lapManager.KartFinished +=
                HandleKartFinished;
        }
    }

    private void OnDisable()
    {
        if (lapManager != null)
        {
            lapManager.KartFinished -=
                HandleKartFinished;
        }
    }

    private void Update()
    {
        // Las posiciones las calcula el servidor; los clientes las leen en RaceTimingManager.
        if (NetworkRole.IsClientOnly)
            return;

        UpdatePositions();
    }

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

        if (raceKarts.Count == 0)
        {
            return;
        }

        if (checkpointManager.CheckpointCount == 0)
            return;

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

    /// <summary>
    /// Avance total del kart en la carrera, medido en checkpoints: los
    /// checkpoints que ya pasó más la fracción del tramo en el que está.
    /// </summary>
    private KartProgress CalculateProgress(
        Rigidbody kart)
    {
        int checkpointCount =
            checkpointManager.CheckpointCount;

        // Los que ya terminaron van adelante de todos, en el orden en que llegaron.
        int finishIndex =
            finishOrder.IndexOf(kart);

        if (finishIndex >= 0)
        {
            float finishedProgress =
                (lapManager.TotalLaps + 1) * checkpointCount
                +
                (finishOrder.Count - finishIndex);

            return new KartProgress(
                kart,
                finishedProgress
            );
        }

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

        // Checkpoints pasados en toda la carrera. La vuelta sube al pasar el
        // último checkpoint, así que ese ya está contado en la vuelta.
        int passedCheckpoints =
            (lap - 1) * checkpointCount
            +
            (lastCheckpoint + 1) % checkpointCount;

        float segmentProgress =
            CalculateSegmentProgress(
                kart,
                nextCheckpoint
            );

        return new KartProgress(
            kart,
            passedCheckpoints + segmentProgress
        );
    }

    /// <summary>
    /// Qué fracción (0 a 1) del tramo entre el checkpoint anterior y el
    /// siguiente recorrió el kart.
    /// </summary>
    private float CalculateSegmentProgress(
        Rigidbody kart,
        int nextCheckpointIndex)
    {
        int previousCheckpointIndex =
            nextCheckpointIndex - 1;

        if (previousCheckpointIndex < 0)
        {
            previousCheckpointIndex =
                checkpointManager.CheckpointCount - 1;
        }

        if (centerline != null &&
            centerline.IsValid)
        {
            return CalculateCenterlineProgress(
                kart,
                previousCheckpointIndex,
                nextCheckpointIndex
            );
        }

        return CalculateStraightProgress(
            kart,
            previousCheckpointIndex,
            nextCheckpointIndex
        );
    }

    /// <summary>
    /// Avance medido sobre la línea central: sigue las curvas de la pista,
    /// así que dos karts en el mismo tramo se ordenan bien aunque el tramo
    /// tenga curvas.
    /// </summary>
    private float CalculateCenterlineProgress(
        Rigidbody kart,
        int previousCheckpointIndex,
        int nextCheckpointIndex)
    {
        EnsureCheckpointDistances();

        float trackLength =
            centerline.Length;

        float segmentStart =
            checkpointDistances[previousCheckpointIndex];

        float segmentLength =
            Mathf.Repeat(
                checkpointDistances[nextCheckpointIndex] - segmentStart,
                trackLength
            );

        if (segmentLength < 0.01f)
            return 0f;

        float travelled =
            Mathf.Repeat(
                centerline.GetDistance(kart.position) - segmentStart,
                trackLength
            );

        if (travelled <= segmentLength)
            return travelled / segmentLength;

        float behind =
            trackLength - travelled;

        // Fuera del tramo y más cerca del checkpoint siguiente: ya lo alcanzó.
        if (travelled - segmentLength < behind)
            return 1f;

        // Detrás del checkpoint anterior (por ejemplo, en la grilla, antes de
        // la línea de llegada): avance negativo, así el que está más atrás
        // queda detrás.
        return -behind / segmentLength;
    }

    /// <summary>
    /// Avance medido sobre la recta entre los dos checkpoints. Se usa solo si
    /// no hay línea central.
    /// </summary>
    private float CalculateStraightProgress(
        Rigidbody kart,
        int previousCheckpointIndex,
        int nextCheckpointIndex)
    {
        RaceCheckpoint nextCheckpoint =
            checkpointManager.GetCheckpoint(
                nextCheckpointIndex
            );

        RaceCheckpoint previousCheckpoint =
            checkpointManager.GetCheckpoint(
                previousCheckpointIndex
            );

        if (nextCheckpoint == null ||
            previousCheckpoint == null)
        {
            return 0f;
        }

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

        return Mathf.Clamp01(
            projection /
            segmentLengthSquared
        );
    }

    private void EnsureCheckpointDistances()
    {
        int count =
            checkpointManager.CheckpointCount;

        if (checkpointDistances != null &&
            checkpointDistances.Length == count)
        {
            return;
        }

        checkpointDistances =
            new float[count];

        for (int i = 0;
             i < count;
             i++)
        {
            RaceCheckpoint checkpoint =
                checkpointManager.GetCheckpoint(i);

            if (checkpoint == null)
                continue;

            checkpointDistances[i] =
                centerline.GetDistance(
                    checkpoint.transform.position
                );
        }
    }

    private void HandleKartFinished(
        Rigidbody kart)
    {
        if (kart == null)
            return;

        if (!finishOrder.Contains(kart))
        {
            finishOrder.Add(kart);
        }
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
