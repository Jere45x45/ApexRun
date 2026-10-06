using System.Globalization;
using TMPro;
using UnityEngine;

/// <summary>
/// HUD de carrera del jugador:
/// - Posición (2.º / 6) y vuelta (VUELTA 1/3), arriba a la izquierda.
/// - Tiempo de carrera, última vuelta, mejor vuelta y penalización acumulada,
///   arriba a la derecha.
/// - Velocidad en km/h, abajo a la derecha.
/// - Aviso arriba al centro: FUERA DE PISTA mientras el kart está afuera de
///   los límites, y la penalización cuando se aplica.
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

    [Header("Límites de pista")]
    [SerializeField] private RaceTrackLimitsController trackLimits;

    [SerializeField] private RacePenaltyManager penaltyManager;

    [Tooltip("Cuánto se muestra el aviso de penalización (s).")]
    [SerializeField, Min(0f)] private float penaltyMessageDuration = 2f;

    [Header("Textos")]
    [SerializeField] private TMP_Text positionText;

    [SerializeField] private TMP_Text lapText;

    [SerializeField] private TMP_Text raceTimeText;

    [SerializeField] private TMP_Text lapTimesText;

    [SerializeField] private TMP_Text speedText;

    [Tooltip("Panel del aviso (se oculta cuando no hay aviso).")]
    [SerializeField] private GameObject warningPanel;

    [SerializeField] private TMP_Text warningText;

    // Tiempos en segundos (Time.time). -1 = todavía no pasó.
    private float raceStartTime = -1f;
    private float lapStartTime;
    private float finishTime = -1f;
    private float lastLapTime = -1f;
    private float bestLapTime = -1f;
    private int currentLap = 1;

    private KartBehaviour playerBehaviour;
    private float penaltyMessageUntil = -1f;
    private float lastPenaltySeconds;

    // Últimos valores mostrados, para no rearmar el texto en cada frame.
    private int shownPosition = -1;
    private int shownKartCount = -1;
    private int shownLap = -1;
    private bool shownFinished;
    private int shownSpeed = -1;
    private float shownPenalty = -1f;
    private int shownWarning = -1;

    private void OnEnable()
    {
        if (raceManager != null)
            raceManager.RaceStarted += HandleRaceStarted;

        if (penaltyManager != null)
            penaltyManager.PenaltyApplied += HandlePenaltyApplied;
    }

    private void OnDisable()
    {
        if (raceManager != null)
            raceManager.RaceStarted -= HandleRaceStarted;

        if (penaltyManager != null)
            penaltyManager.PenaltyApplied -= HandlePenaltyApplied;
    }

    private void Start()
    {
        if (playerKart == null || lapManager == null)
        {
            Debug.LogError("RaceHUD necesita el kart del jugador y el RaceLapManager.", this);
            enabled = false;
            return;
        }

        playerBehaviour = playerKart.GetComponent<KartBehaviour>();

        SetText(raceTimeText, FormatTime(0f));
        UpdateLapTimesText();
        UpdateWarning();
    }

    private void Update()
    {
        UpdateLap();
        UpdatePosition();
        UpdateRaceTime();
        UpdateSpeed();
        UpdatePenalty();
        UpdateWarning();
    }

    private void HandleRaceStarted()
    {
        raceStartTime = Time.time;
        lapStartTime = raceStartTime;
        currentLap = lapManager.GetCurrentLap(playerKart);
    }

    private void HandlePenaltyApplied(Rigidbody kart, float seconds)
    {
        if (kart != playerKart)
            return;

        lastPenaltySeconds = seconds;
        penaltyMessageUntil = Time.time + penaltyMessageDuration;
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

    private void UpdatePenalty()
    {
        float penalty = penaltyManager != null ? penaltyManager.GetPenaltyTime(playerKart) : 0f;

        if (Mathf.Approximately(penalty, shownPenalty))
            return;

        shownPenalty = penalty;

        UpdateLapTimesText();
    }

    /// <summary>
    /// 0 = sin aviso, 1 = fuera de pista, 2 = penalización recién aplicada
    /// (tiene prioridad durante penaltyMessageDuration).
    /// </summary>
    private void UpdateWarning()
    {
        int warning = 0;

        if (Time.time < penaltyMessageUntil)
            warning = 2;
        else if (trackLimits != null && playerBehaviour != null && trackLimits.IsOutsideTrackLimits(playerBehaviour))
            warning = 1;

        if (warning == shownWarning)
            return;

        shownWarning = warning;

        if (warningPanel != null)
            warningPanel.SetActive(warning != 0);

        if (warning == 2)
            SetText(warningText, "<color=#FF5A4A>+" + FormatSeconds(lastPenaltySeconds) + " s</color>  LÍMITES DE PISTA");
        else if (warning == 1)
            SetText(warningText, "<color=#FFC23D>FUERA DE PISTA</color>");
        else
            SetText(warningText, "");
    }

    private void UpdateLapTimesText()
    {
        string last = lastLapTime >= 0f ? FormatTime(lastLapTime) : "--:--.---";
        string best = bestLapTime >= 0f ? FormatTime(bestLapTime) : "--:--.---";

        string text = $"ÚLTIMA {last}\nMEJOR  {best}";

        if (shownPenalty > 0f)
            text += "\n<color=#FF5A4A>PENALIZACIÓN +" + FormatSeconds(shownPenalty) + " s</color>";

        SetText(lapTimesText, text);
    }

    /// <summary>Formato de carrera: m:ss.mmm, siempre con punto decimal.</summary>
    private static string FormatTime(float seconds)
    {
        int minutes = Mathf.FloorToInt(seconds / 60f);
        float rest = seconds - minutes * 60f;

        return minutes + ":" + rest.ToString("00.000", CultureInfo.InvariantCulture);
    }

    private static string FormatSeconds(float seconds)
    {
        return seconds.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private static void SetText(TMP_Text text, string value)
    {
        if (text != null)
            text.text = value;
    }
}
