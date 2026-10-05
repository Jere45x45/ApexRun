using System.Globalization;
using TMPro;
using UnityEngine;

/// <summary>
/// HUD de carrera del jugador:
/// - Posición (2.º / 6) y vuelta (VUELTA 1/3), arriba a la izquierda.
/// - Tiempo de carrera, última vuelta y mejor vuelta, arriba a la derecha.
/// - Velocidad en km/h, abajo a la derecha.
/// El tiempo arranca con la largada (RaceManager.RaceStarted) y se detiene
/// cuando el jugador termina. Cada vuelta se mide al cambiar de vuelta en
/// RaceLapManager.
/// </summary>
public class RaceHUD : MonoBehaviour
{
    [Header("Carrera")]
    [SerializeField] private RaceManager raceManager;

    [SerializeField] private RaceLapManager lapManager;

    [SerializeField] private RacePositionManager positionManager;

    [Tooltip("Rigidbody del kart del jugador.")]
    [SerializeField] private Rigidbody playerKart;

    [Header("Textos")]
    [SerializeField] private TMP_Text positionText;

    [SerializeField] private TMP_Text lapText;

    [SerializeField] private TMP_Text raceTimeText;

    [SerializeField] private TMP_Text lapTimesText;

    [SerializeField] private TMP_Text speedText;

    // Tiempos en segundos (Time.time). -1 = todavía no pasó.
    private float raceStartTime = -1f;
    private float lapStartTime;
    private float finishTime = -1f;
    private float lastLapTime = -1f;
    private float bestLapTime = -1f;
    private int currentLap = 1;

    // Últimos valores mostrados, para no rearmar el texto en cada frame.
    private int shownPosition = -1;
    private int shownKartCount = -1;
    private int shownLap = -1;
    private bool shownFinished;
    private int shownSpeed = -1;

    private void OnEnable()
    {
        if (raceManager != null)
            raceManager.RaceStarted += HandleRaceStarted;
    }

    private void OnDisable()
    {
        if (raceManager != null)
            raceManager.RaceStarted -= HandleRaceStarted;
    }

    private void Start()
    {
        if (playerKart == null || lapManager == null)
        {
            Debug.LogError("RaceHUD necesita el kart del jugador y el RaceLapManager.", this);
            enabled = false;
            return;
        }

        SetText(raceTimeText, FormatTime(0f));
        UpdateLapTimesText();
    }

    private void Update()
    {
        UpdateLap();
        UpdatePosition();
        UpdateRaceTime();
        UpdateSpeed();
    }

    private void HandleRaceStarted()
    {
        raceStartTime = Time.time;
        lapStartTime = raceStartTime;
        currentLap = lapManager.GetCurrentLap(playerKart);
    }

    /// <summary>Detecta el cambio de vuelta y el final para medir los tiempos de vuelta.</summary>
    private void UpdateLap()
    {
        int lap = lapManager.GetCurrentLap(playerKart);
        bool finished = lapManager.IsFinished(playerKart);

        if (raceStartTime >= 0f && finishTime < 0f)
        {
            if (lap > currentLap)
            {
                RegisterLap();
                currentLap = lap;
            }

            if (finished)
            {
                RegisterLap();
                finishTime = Time.time;
            }
        }

        if (lap == shownLap && finished == shownFinished)
            return;

        shownLap = lap;
        shownFinished = finished;

        int totalLaps = lapManager.TotalLaps;

        SetText(lapText, finished
            ? "TERMINADO"
            : $"VUELTA {Mathf.Min(lap, totalLaps)}/{totalLaps}");
    }

    private void RegisterLap()
    {
        lastLapTime = Time.time - lapStartTime;
        lapStartTime = Time.time;

        if (bestLapTime < 0f || lastLapTime < bestLapTime)
            bestLapTime = lastLapTime;

        UpdateLapTimesText();
    }

    private void UpdatePosition()
    {
        if (positionManager == null)
            return;

        int position = positionManager.GetPosition(playerKart);
        int kartCount = positionManager.KartCount;

        if (position == shownPosition && kartCount == shownKartCount)
            return;

        shownPosition = position;
        shownKartCount = kartCount;

        SetText(positionText, position > 0
            ? $"{position}<size=50%>.º / {kartCount}</size>"
            : "-");
    }

    private void UpdateRaceTime()
    {
        if (raceStartTime < 0f)
            return;

        float end = finishTime >= 0f ? finishTime : Time.time;

        SetText(raceTimeText, FormatTime(end - raceStartTime));
    }

    private void UpdateSpeed()
    {
        int speed = Mathf.RoundToInt(playerKart.linearVelocity.magnitude * 3.6f);

        if (speed == shownSpeed)
            return;

        shownSpeed = speed;

        SetText(speedText, $"{speed}<size=40%> km/h</size>");
    }

    private void UpdateLapTimesText()
    {
        string last = lastLapTime >= 0f ? FormatTime(lastLapTime) : "--:--.---";
        string best = bestLapTime >= 0f ? FormatTime(bestLapTime) : "--:--.---";

        SetText(lapTimesText, $"ÚLTIMA {last}\nMEJOR  {best}");
    }

    /// <summary>Formato de carrera: m:ss.mmm, siempre con punto decimal.</summary>
    private static string FormatTime(float seconds)
    {
        int minutes = Mathf.FloorToInt(seconds / 60f);
        float rest = seconds - minutes * 60f;

        return minutes + ":" + rest.ToString("00.000", CultureInfo.InvariantCulture);
    }

    private static void SetText(TMP_Text text, string value)
    {
        if (text != null)
            text.text = value;
    }
}
