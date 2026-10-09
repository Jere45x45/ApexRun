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
/// Todo sale de RaceTimingManager (el mismo que usa la pantalla de
/// resultados), que en los clientes trae lo que publica el servidor. El kart
/// del jugador lo asigna LocalPlayerKartBinder cuando aparece por red.
/// </summary>
public class RaceHUD : MonoBehaviour
{
    [Header("Carrera")]
    [SerializeField] private RaceTimingManager timingManager;

    [Tooltip("Rigidbody del kart del jugador. En red lo asigna LocalPlayerKartBinder.")]
    [SerializeField] private Rigidbody playerKart;

    [Header("Límites de pista")]
    [SerializeField] private RaceTrackLimitsController trackLimits;

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
    private float shownLastLap = -2f;
    private float shownBestLap = -2f;
    private int shownWarning = -1;

    private void OnEnable()
    {
        if (timingManager != null)
            timingManager.PenaltyApplied += HandlePenaltyApplied;
    }

    private void OnDisable()
    {
        if (timingManager != null)
            timingManager.PenaltyApplied -= HandlePenaltyApplied;
    }

    private void Start()
    {
        if (timingManager == null)
        {
            Debug.LogError("RaceHUD necesita el RaceTimingManager.", this);
            enabled = false;
            return;
        }

        SetText(raceTimeText, FormatTime(0f));
        SetPlayerKart(playerKart);
    }

    /// <summary>Kart del jugador de esta computadora.</summary>
    public void SetPlayerKart(Rigidbody kart)
    {
        playerKart = kart;
        playerBehaviour = kart != null ? kart.GetComponent<KartBehaviour>() : null;

        // Que se rearme todo con el kart nuevo.
        shownPosition = shownKartCount = shownLap = shownSpeed = shownWarning = -1;
        shownPenalty = -1f;
        shownLastLap = shownBestLap = -2f;
    }

    private void Update()
    {
        if (playerKart == null)
            return;

        UpdateLap();
        UpdatePosition();
        UpdateRaceTime();
        UpdateLapTimes();
        UpdateSpeed();
        UpdateWarning();
    }

    private void HandlePenaltyApplied(Rigidbody kart, float seconds)
    {
        if (kart != playerKart)
            return;

        lastPenaltySeconds = seconds;
        penaltyMessageUntil = Time.time + penaltyMessageDuration;
    }

    private void UpdateLap()
    {
        int lap = timingManager.GetLap(playerKart);
        bool finished = timingManager.IsFinished(playerKart);

        if (lap == shownLap && finished == shownFinished)
            return;

        shownLap = lap;
        shownFinished = finished;

        int totalLaps = timingManager.TotalLaps;

        SetText(lapText, finished
            ? "TERMINADO"
            : $"VUELTA {Mathf.Min(lap, totalLaps)}/{totalLaps}");
    }

    private void UpdatePosition()
    {
        int position = timingManager.GetPosition(playerKart);
        int kartCount = timingManager.KartCount;

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
        if (!timingManager.HasStarted)
            return;

        SetText(raceTimeText, FormatTime(timingManager.GetRaceTime(playerKart)));
    }

    /// <summary>Última vuelta, mejor vuelta y penalización: solo se rearma si algo cambió.</summary>
    private void UpdateLapTimes()
    {
        float lastLap = timingManager.GetLastLap(playerKart);
        float bestLap = timingManager.GetBestLap(playerKart);
        float penalty = timingManager.GetPenalty(playerKart);

        if (lastLap == shownLastLap && bestLap == shownBestLap && Mathf.Approximately(penalty, shownPenalty))
            return;

        shownLastLap = lastLap;
        shownBestLap = bestLap;
        shownPenalty = penalty;

        string last = lastLap >= 0f ? FormatTime(lastLap) : "--:--.---";
        string best = bestLap >= 0f ? FormatTime(bestLap) : "--:--.---";

        string text = $"ÚLTIMA {last}\nMEJOR  {best}";

        if (penalty > 0f)
            text += "\n<color=#FF5A4A>PENALIZACIÓN +" + FormatSeconds(penalty) + " s</color>";

        SetText(lapTimesText, text);
    }

    private void UpdateSpeed()
    {
        int speed = Mathf.RoundToInt(playerKart.linearVelocity.magnitude * 3.6f);

        if (speed == shownSpeed)
            return;

        shownSpeed = speed;

        SetText(speedText, $"{speed}<size=40%> km/h</size>");
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
