using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cronometraje de la carrera para todos los karts:
/// - Tiempo de carrera desde la largada (RaceManager.RaceStarted).
/// - Última y mejor vuelta de cada kart (RaceLapManager.LapCompleted).
/// - Tiempo de llegada y tiempo final = tiempo + penalizaciones.
/// - Clasificación: primero los que terminaron, por tiempo final (una
///   penalización puede hacer perder lugares); después los que siguen
///   corriendo, en el orden de RacePositionManager.
///
/// Lo usan el HUD y la pantalla de resultados, así los dos muestran los mismos
/// tiempos.
/// </summary>
public class RaceTimingManager : MonoBehaviour
{
    [SerializeField] private RaceManager raceManager;

    [SerializeField] private RaceLapManager lapManager;

    [SerializeField] private RacePenaltyManager penaltyManager;

    [SerializeField] private RacePositionManager positionManager;

    /// <summary>Resultado de un kart en la clasificación.</summary>
    public struct Result
    {
        public int Position;
        public Rigidbody Kart;
        public bool Finished;
        public int Lap;
        public float RaceTime;
        public float BestLap;
        public float Penalty;
        public float FinalTime;
    }

    private class KartTiming
    {
        public float LapStart;
        public float LastLap = -1f;
        public float BestLap = -1f;
        public float FinishTime = -1f;
    }

    private readonly Dictionary<Rigidbody, KartTiming> timings = new Dictionary<Rigidbody, KartTiming>();
    private readonly List<Result> classification = new List<Result>();

    private float raceStartTime = -1f;

    public bool HasStarted => raceStartTime >= 0f;

    /// <summary>
    /// Se dispara cuando un kart cruza la meta por última vez, después de
    /// guardar su tiempo.
    /// </summary>
    public event Action<Rigidbody> KartFinished;

    private void OnEnable()
    {
        if (raceManager != null)
            raceManager.RaceStarted += HandleRaceStarted;

        if (lapManager != null)
        {
            lapManager.LapCompleted += HandleLapCompleted;
            lapManager.KartFinished += HandleKartFinished;
        }
    }

    private void OnDisable()
    {
        if (raceManager != null)
            raceManager.RaceStarted -= HandleRaceStarted;

        if (lapManager != null)
        {
            lapManager.LapCompleted -= HandleLapCompleted;
            lapManager.KartFinished -= HandleKartFinished;
        }
    }

    private void Start()
    {
        if (raceManager == null || lapManager == null)
        {
            Debug.LogError("RaceTimingManager necesita el RaceManager y el RaceLapManager.", this);
            enabled = false;
        }
    }

    /// <summary>Tiempo de carrera del kart: hasta que cruzó la meta, o el actual si sigue corriendo.</summary>
    public float GetRaceTime(Rigidbody kart)
    {
        if (!HasStarted)
            return 0f;

        KartTiming timing = GetTiming(kart);

        if (timing != null && timing.FinishTime >= 0f)
            return timing.FinishTime;

        return Time.time - raceStartTime;
    }

    /// <summary>Última vuelta en segundos, -1 si todavía no completó ninguna.</summary>
    public float GetLastLap(Rigidbody kart)
    {
        KartTiming timing = GetTiming(kart);
        return timing != null ? timing.LastLap : -1f;
    }

    /// <summary>Mejor vuelta en segundos, -1 si todavía no completó ninguna.</summary>
    public float GetBestLap(Rigidbody kart)
    {
        KartTiming timing = GetTiming(kart);
        return timing != null ? timing.BestLap : -1f;
    }

    public bool IsFinished(Rigidbody kart)
    {
        KartTiming timing = GetTiming(kart);
        return timing != null && timing.FinishTime >= 0f;
    }

    public float GetPenalty(Rigidbody kart)
    {
        return penaltyManager != null ? penaltyManager.GetPenaltyTime(kart) : 0f;
    }

    /// <summary>
    /// Clasificación actual de todos los karts de la carrera. La lista se
    /// reutiliza: no guardarla entre llamadas.
    /// </summary>
    public IReadOnlyList<Result> GetClassification()
    {
        classification.Clear();

        foreach (KartBehaviour kartBehaviour in raceManager.RaceKarts)
        {
            if (kartBehaviour == null)
                continue;

            Rigidbody kart = kartBehaviour.GetComponent<Rigidbody>();

            if (kart == null)
                continue;

            bool finished = IsFinished(kart);
            float raceTime = GetRaceTime(kart);
            float penalty = GetPenalty(kart);

            classification.Add(new Result
            {
                Kart = kart,
                Finished = finished,
                Lap = lapManager.GetCurrentLap(kart),
                RaceTime = raceTime,
                BestLap = GetBestLap(kart),
                Penalty = penalty,
                FinalTime = raceTime + penalty
            });
        }

        classification.Sort(CompareResults);

        for (int i = 0; i < classification.Count; i++)
        {
            Result result = classification[i];
            result.Position = i + 1;
            classification[i] = result;
        }

        return classification;
    }

    private int CompareResults(Result a, Result b)
    {
        if (a.Finished != b.Finished)
            return a.Finished ? -1 : 1;

        if (a.Finished)
            return a.FinalTime.CompareTo(b.FinalTime);

        // Los que siguen corriendo, por su posición en pista.
        if (positionManager == null)
            return 0;

        return positionManager.GetPosition(a.Kart).CompareTo(positionManager.GetPosition(b.Kart));
    }

    private void HandleRaceStarted()
    {
        raceStartTime = Time.time;
        timings.Clear();
    }

    private void HandleLapCompleted(Rigidbody kart)
    {
        if (!HasStarted || kart == null)
            return;

        KartTiming timing = GetOrCreateTiming(kart);

        float now = Time.time;
        float lapTime = now - timing.LapStart;

        timing.LastLap = lapTime;
        timing.LapStart = now;

        if (timing.BestLap < 0f || lapTime < timing.BestLap)
            timing.BestLap = lapTime;
    }

    private void HandleKartFinished(Rigidbody kart)
    {
        if (!HasStarted || kart == null)
            return;

        GetOrCreateTiming(kart).FinishTime = Time.time - raceStartTime;

        KartFinished?.Invoke(kart);
    }

    private KartTiming GetTiming(Rigidbody kart)
    {
        if (kart == null)
            return null;

        timings.TryGetValue(kart, out KartTiming timing);
        return timing;
    }

    private KartTiming GetOrCreateTiming(Rigidbody kart)
    {
        if (!timings.TryGetValue(kart, out KartTiming timing))
        {
            // La primera vuelta se mide desde la largada.
            timing = new KartTiming { LapStart = raceStartTime };
            timings.Add(kart, timing);
        }

        return timing;
    }
}
