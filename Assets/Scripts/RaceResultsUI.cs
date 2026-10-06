using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Pantalla de resultados. Aparece un rato después de que el jugador cruza la
/// meta por última vez:
/// - Tabla con posición, piloto, tiempo, penalización, tiempo final y mejor
///   vuelta. Los que siguen corriendo aparecen abajo como "EN CARRERA" y la
///   tabla se actualiza cuando terminan.
/// - Botones Reintentar (vuelve a cargar esta escena) y Menú (escena de inicio).
/// </summary>
public class RaceResultsUI : MonoBehaviour
{
    [Header("Carrera")]
    [SerializeField] private RaceTimingManager timingManager;

    [Tooltip("Rigidbody del kart del jugador.")]
    [SerializeField] private Rigidbody playerKart;

    [Header("Pantalla")]
    [Tooltip("Panel de resultados (se muestra al terminar).")]
    [SerializeField] private GameObject panel;

    [SerializeField] private TMP_Text titleText;

    [SerializeField] private TMP_Text tableText;

    [SerializeField] private Button retryButton;

    [SerializeField] private Button menuButton;

    [Tooltip("Lo que se oculta al mostrar los resultados (HUD, minimapa).")]
    [SerializeField] private GameObject[] hideWhenShown = new GameObject[0];

    [Tooltip("Cuánto se espera después de cruzar la meta (s).")]
    [SerializeField, Min(0f)] private float showDelay = 1.5f;

    [Header("Escenas")]
    [Tooltip("Escena del menú. Tiene que estar en Build Settings.")]
    [SerializeField] private string menuSceneName = "Inicio";

    private const float RefreshInterval = 0.25f;
    private const string PlayerColor = "#FFC23D";

    private readonly StringBuilder builder = new StringBuilder();

    private float showAt = -1f;
    private float nextRefresh;
    private bool shown;

    private void Awake()
    {
        if (panel != null)
            panel.SetActive(false);

        if (retryButton != null)
            retryButton.onClick.AddListener(Retry);

        if (menuButton != null)
            menuButton.onClick.AddListener(GoToMenu);
    }

    private void OnEnable()
    {
        if (timingManager != null)
            timingManager.KartFinished += HandleKartFinished;
    }

    private void OnDisable()
    {
        if (timingManager != null)
            timingManager.KartFinished -= HandleKartFinished;
    }

    private void Start()
    {
        if (timingManager == null || playerKart == null || panel == null || tableText == null)
        {
            Debug.LogError("RaceResultsUI necesita el RaceTimingManager, el kart del jugador, el panel y el texto de la tabla.", this);
            enabled = false;
        }
    }

    private void Update()
    {
        if (!shown)
        {
            if (showAt >= 0f && Time.time >= showAt)
                Show();

            return;
        }

        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + RefreshInterval;
            RefreshTable();
        }
    }

    private void HandleKartFinished(Rigidbody kart)
    {
        if (kart == playerKart && showAt < 0f)
            showAt = Time.time + showDelay;
    }

    private void Show()
    {
        shown = true;

        foreach (GameObject hidden in hideWhenShown)
        {
            if (hidden != null)
                hidden.SetActive(false);
        }

        panel.SetActive(true);
        RefreshTable();

        // Para manejar los botones con joystick o teclado.
        if (retryButton != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(retryButton.gameObject);
    }

    private void RefreshTable()
    {
        var results = timingManager.GetClassification();

        int playerPosition = 0;

        builder.Clear();
        builder.Append("<b>POS<pos=10%>PILOTO<pos=36%>TIEMPO<pos=54%>PENAL.<pos=67%>FINAL<pos=85%>MEJOR V.</b>\n");

        foreach (RaceTimingManager.Result result in results)
        {
            bool isPlayer = result.Kart == playerKart;

            if (isPlayer)
            {
                playerPosition = result.Position;
                builder.Append("<color=").Append(PlayerColor).Append('>');
            }

            builder.Append(result.Position).Append(".º");
            builder.Append("<pos=10%>").Append(isPlayer ? "VOS" : result.Kart.name.ToUpperInvariant());

            if (result.Finished)
            {
                builder.Append("<pos=36%>").Append(FormatTime(result.RaceTime));
                builder.Append("<pos=54%>").Append(result.Penalty > 0f ? "+" + FormatSeconds(result.Penalty) + " s" : "-");
                builder.Append("<pos=67%><b>").Append(FormatTime(result.FinalTime)).Append("</b>");
            }
            else
            {
                builder.Append("<pos=36%>EN CARRERA (VUELTA ").Append(result.Lap).Append(')');
            }

            builder.Append("<pos=85%>").Append(result.BestLap >= 0f ? FormatTime(result.BestLap) : "--:--.---");

            if (isPlayer)
                builder.Append("</color>");

            builder.Append('\n');
        }

        tableText.text = builder.ToString();

        if (titleText != null)
            titleText.text = playerPosition > 0 ? $"TERMINASTE {playerPosition}.º" : "RESULTADOS";
    }

    private void Retry()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void GoToMenu()
    {
        SceneManager.LoadScene(menuSceneName);
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
}
