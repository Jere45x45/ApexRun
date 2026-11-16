using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using Unity.Netcode;
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
/// - Reintentar: el host vuelve a cargar la carrera para todos (los clientes
///   no lo ven: esperan al host).
/// - Menú: se sale de la partida y vuelve a la escena de inicio.
///
/// Qualy y campeonato (RaceSession):
/// - Después de la qualy, la tabla es de tiempos (mejor vuelta y diferencia
///   con la pole) y el botón de Reintentar pasa a A LA CARRERA: el anfitrión
///   carga la carrera con esa grilla (cuando todos terminaron su vuelta).
/// - En el campeonato, abajo de la carrera va la tabla del campeonato
///   (puntos de esta carrera y total), y el botón pasa a SIGUIENTE FECHA (o,
///   después de la última, NUEVO CAMPEONATO). Se habilita cuando llegaron
///   todos, que es cuando se anotan los puntos.
/// - También aparece si la sesión se cierra sin que el jugador cruce la meta
///   (por ejemplo, la qualy se cerró por tiempo).
/// </summary>
public class RaceResultsUI : MonoBehaviour
{
    [Header("Carrera")]
    [SerializeField] private RaceTimingManager timingManager;

    [Tooltip("Rigidbody del kart del jugador. En red lo asigna LocalPlayerKartBinder.")]
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

    // Tu fila va en rojo (el del juego) y en negrita.
    private const string PlayerColor = "#A00000";

    // Salida en falso en la columna de reacción.
    private const string JumpStartColor = "#D35400";

    private readonly StringBuilder builder = new StringBuilder();

    private float showAt = -1f;
    private float nextRefresh;
    private bool shown;

    private RaceManager raceManager;

    /// <summary>Qué hace el botón de Reintentar según la sesión.</summary>
    private enum RetryAction
    {
        Retry,
        ToRace,
        NextRound,
        NewChampionship
    }

    private RetryAction CurrentRetryAction
    {
        get
        {
            if (RaceSession.IsQualifying)
                return RetryAction.ToRace;

            if (RaceSession.IsChampionship)
                return RaceSession.IsLastRound ? RetryAction.NewChampionship : RetryAction.NextRound;

            return RetryAction.Retry;
        }
    }

    private void Awake()
    {
        if (panel != null)
            panel.SetActive(false);

        if (retryButton != null)
            retryButton.onClick.AddListener(Retry);

        if (menuButton != null)
            menuButton.onClick.AddListener(GoToMenu);

        raceManager = FindFirstObjectByType<RaceManager>();
    }

    private void OnEnable()
    {
        if (timingManager != null)
            timingManager.KartFinished += HandleKartFinished;

        if (raceManager != null)
            raceManager.RaceFinished += HandleRaceFinished;
    }

    private void OnDisable()
    {
        if (timingManager != null)
            timingManager.KartFinished -= HandleKartFinished;

        if (raceManager != null)
            raceManager.RaceFinished -= HandleRaceFinished;
    }

    /// <summary>Terminó la sesión: si el jugador no había cruzado la meta (qualy cerrada por tiempo), igual se muestran.</summary>
    private void HandleRaceFinished()
    {
        if (!shown && showAt < 0f)
            showAt = Time.time + showDelay;
    }

    private void Start()
    {
        if (timingManager == null || panel == null || tableText == null)
        {
            Debug.LogError("RaceResultsUI necesita el RaceTimingManager, el panel y el texto de la tabla.", this);
            enabled = false;
        }
    }

    /// <summary>Kart del jugador de esta computadora.</summary>
    public void SetPlayerKart(Rigidbody kart)
    {
        playerKart = kart;
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
            UpdateRetryButton();
        }
    }

    private void HandleKartFinished(Rigidbody kart)
    {
        if (kart != null && kart == playerKart && showAt < 0f)
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

        // Reintentar es del host: vuelve a cargar la carrera para todos.
        // Los demás jugadores lo ven deshabilitado.
        if (retryButton != null)
        {
            retryButton.gameObject.SetActive(true);
            UpdateRetryButton();
        }

        panel.SetActive(true);
        RefreshTable();

        // Para manejar los botones con joystick o teclado.
        Button first = retryButton != null && retryButton.gameObject.activeSelf && retryButton.interactable ? retryButton : menuButton;

        if (first != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(first.gameObject);
    }

    /// <summary>
    /// El botón de Reintentar según la sesión: texto y si se puede tocar. Pasar
    /// a la carrera o a la fecha siguiente espera a que hayan llegado todos.
    /// </summary>
    private void UpdateRetryButton()
    {
        if (retryButton == null)
            return;

        RetryAction action = CurrentRetryAction;
        bool everyoneIn = action == RetryAction.Retry || raceManager == null || raceManager.IsFinished();

        retryButton.interactable = NetworkRole.IsAuthority && everyoneIn;

        if (action == RetryAction.Retry)
            return;

        string spanish;
        string english;

        switch (action)
        {
            case RetryAction.ToRace:
                spanish = "A la carrera";
                english = "To the race";
                break;
            case RetryAction.NextRound:
                spanish = "Siguiente fecha";
                english = "Next round";
                break;
            default:
                spanish = "Nuevo campeonato";
                english = "New championship";
                break;
        }

        LocalizedText localized = retryButton.GetComponentInChildren<LocalizedText>(true);

        if (localized != null)
        {
            if (localized.Spanish != spanish)
                localized.Set(spanish, english);
            return;
        }

        TMP_Text label = retryButton.GetComponentInChildren<TMP_Text>(true);
        string text = GameLanguage.Text(spanish, english);

        if (label != null && label.text != text)
            label.text = text;
    }

    private void RefreshTable()
    {
        if (RaceSession.IsQualifying && RaceWeekend.Instance != null)
        {
            RefreshQualifyingTable();
            return;
        }

        var results = timingManager.GetClassification();

        int playerPosition = 0;

        builder.Clear();
        builder.Append(GameLanguage.Text("<b>Pos.<pos=9%>Piloto<pos=29%>Reacción<pos=42%>Tiempo<pos=57%>Penal.<pos=67%>Final<pos=83%>Mejor vuelta</b>\n", "<b>Pos.<pos=9%>Driver<pos=29%>Reaction<pos=42%>Time<pos=57%>Pen.<pos=67%>Final<pos=83%>Best lap</b>\n"));

        foreach (RaceTimingManager.Result result in results)
        {
            bool isPlayer = result.Kart == playerKart;

            if (isPlayer)
            {
                playerPosition = result.Position;
                builder.Append("<color=").Append(PlayerColor).Append("><b>");
            }

            builder.Append(result.Position).Append(GameLanguage.IsEnglish ? RaceHUD.Ordinal(result.Position) : ".º");
            builder.Append("<pos=9%>").Append(isPlayer ? GameLanguage.Text("Vos", "You") : GetDisplayName(result.Kart));

            builder.Append("<pos=29%>").Append(FormatReaction(result));

            if (result.Finished)
            {
                builder.Append("<pos=42%>").Append(FormatTime(result.RaceTime));
                builder.Append("<pos=57%>").Append(result.Penalty > 0f ? "+" + FormatSeconds(result.Penalty) + " s" : "-");
                builder.Append("<pos=67%>").Append(FormatTime(result.FinalTime));
            }
            else
            {
                builder.Append("<pos=42%>").Append(GameLanguage.Text("En carrera (vuelta ", "Racing (lap ")).Append(result.Lap).Append(')');
            }

            builder.Append("<pos=83%>").Append(result.BestLap >= 0f ? FormatTime(result.BestLap) : "--:--.---");

            if (isPlayer)
                builder.Append("</b></color>");

            builder.Append('\n');
        }

        if (RaceSession.IsChampionship)
            AppendChampionship();

        AppendWaiting();
        tableText.text = builder.ToString();

        if (titleText != null)
            titleText.text = ChampionTitle() ?? (playerPosition > 0 ? (GameLanguage.IsEnglish ? $"You finished {playerPosition}{RaceHUD.Ordinal(playerPosition)}!" : $"¡Terminaste en {playerPosition}.º lugar!") : GameLanguage.Text("Resultados", "Results"));
    }

    /// <summary>Después de la qualy: por mejor vuelta, con la diferencia con la pole.</summary>
    private void RefreshQualifyingTable()
    {
        RaceWeekend weekend = RaceWeekend.Instance;
        IReadOnlyList<Rigidbody> order = weekend.GetQualifyingOrder();
        float pole = order.Count > 0 ? weekend.GetBestLap(order[0]) : -1f;
        int playerPosition = 0;

        builder.Clear();

        if (RaceSession.IsChampionship)
            builder.Append("<b>").Append(GameLanguage.Text("FECHA ", "ROUND ")).Append(RaceSession.Round).Append('/').Append(RaceSession.Rounds).Append("</b>\n");

        builder.Append(GameLanguage.Text("<b>Pos.<pos=9%>Piloto<pos=42%>Mejor vuelta<pos=62%>Diferencia</b>\n", "<b>Pos.<pos=9%>Driver<pos=42%>Best lap<pos=62%>Gap</b>\n"));

        for (int i = 0; i < order.Count; i++)
        {
            Rigidbody kart = order[i];
            bool isPlayer = kart == playerKart;
            float best = weekend.GetBestLap(kart);

            if (isPlayer)
            {
                playerPosition = i + 1;
                builder.Append("<color=").Append(PlayerColor).Append("><b>");
            }

            builder.Append(i + 1).Append(GameLanguage.IsEnglish ? RaceHUD.Ordinal(i + 1) : ".º");
            builder.Append("<pos=9%>").Append(isPlayer ? GameLanguage.Text("Vos", "You") : GetDisplayName(kart));
            builder.Append("<pos=42%>").Append(best >= 0f ? FormatTime(best) : GameLanguage.Text("Sin tiempo", "No time"));
            builder.Append("<pos=62%>").Append(i == 0 || best < 0f || pole < 0f ? "-" : "+" + (best - pole).ToString("0.000", CultureInfo.InvariantCulture));

            if (isPlayer)
                builder.Append("</b></color>");

            builder.Append('\n');
        }

        AppendWaiting();
        tableText.text = builder.ToString();

        if (titleText != null)
        {
            titleText.text = playerPosition > 0 && weekend.GetBestLap(playerKart) >= 0f
                ? (GameLanguage.IsEnglish ? $"You start {playerPosition}{RaceHUD.Ordinal(playerPosition)}!" : $"¡Largás {playerPosition}.º!")
                : GameLanguage.Text("Clasificación", "Qualifying");
        }
    }

    /// <summary>Tabla del campeonato, abajo de la carrera.</summary>
    private void AppendChampionship()
    {
        List<RaceSession.Standing> standings = RaceSession.GetStandings();
        string own = playerKart != null ? RaceSession.KartKey(playerKart) : null;

        builder.Append("\n<b>").Append(GameLanguage.Text("CAMPEONATO", "CHAMPIONSHIP")).Append("  -  ")
            .Append(GameLanguage.Text("FECHA ", "ROUND ")).Append(RaceSession.Round).Append('/').Append(RaceSession.Rounds).Append("</b>\n");

        if (standings.Count == 0)
        {
            builder.Append("<color=#888888>").Append(GameLanguage.Text("Los puntos se suman cuando llegan todos.", "Points are added when everyone finishes.")).Append("</color>\n");
            return;
        }

        builder.Append(GameLanguage.Text("<b>Pos.<pos=9%>Piloto<pos=42%>Esta carrera<pos=62%>Puntos</b>\n", "<b>Pos.<pos=9%>Driver<pos=42%>This race<pos=62%>Points</b>\n"));

        for (int i = 0; i < standings.Count; i++)
        {
            RaceSession.Standing standing = standings[i];
            bool isPlayer = standing.Key == own;

            if (isPlayer)
                builder.Append("<color=").Append(PlayerColor).Append("><b>");

            builder.Append(i + 1).Append(GameLanguage.IsEnglish ? RaceHUD.Ordinal(i + 1) : ".º");
            builder.Append("<pos=9%>").Append(isPlayer ? GameLanguage.Text("Vos", "You") : standing.Name);
            builder.Append("<pos=42%>").Append(standing.LastPosition > 0 ? "+" + standing.LastPoints : "-");
            builder.Append("<pos=62%>").Append(standing.Points);

            if (isPlayer)
                builder.Append("</b></color>");

            builder.Append('\n');
        }
    }

    /// <summary>Qué se espera (en gris): que terminen todos, o al anfitrión.</summary>
    private void AppendWaiting()
    {
        if (CurrentRetryAction == RetryAction.Retry)
            return;

        string text = null;

        if (raceManager != null && !raceManager.IsFinished())
            text = RaceSession.IsQualifying
                ? GameLanguage.Text("Esperando que todos terminen su vuelta...", "Waiting for everyone to finish their lap...")
                : GameLanguage.Text("Esperando que lleguen todos...", "Waiting for everyone to finish...");
        else if (!NetworkRole.IsAuthority)
            text = GameLanguage.Text("Esperando al anfitrión...", "Waiting for the host...");

        if (text != null)
            builder.Append("\n<color=#888888>").Append(text).Append("</color>");
    }

    /// <summary>Terminó la última fecha: el título dice quién ganó el campeonato.</summary>
    private string ChampionTitle()
    {
        if (!RaceSession.IsChampionship || !RaceSession.IsLastRound || raceManager == null || !raceManager.IsFinished())
            return null;

        List<RaceSession.Standing> standings = RaceSession.GetStandings();

        if (standings.Count == 0)
            return null;

        string own = playerKart != null ? RaceSession.KartKey(playerKart) : null;

        if (standings[0].Key == own)
            return GameLanguage.Text("¡Sos campeón!", "You are the champion!");

        return GameLanguage.IsEnglish ? $"{standings[0].Name} is the champion!" : $"¡{standings[0].Name} es campeón!";
    }

    private static string GetDisplayName(Rigidbody kart)
    {
        NetworkKart networkKart = kart.GetComponent<NetworkKart>();
        return networkKart != null ? networkKart.DisplayName : kart.name.ToUpperInvariant();
    }

    private void Retry()
    {
        if (!NetworkRole.IsAuthority)
            return;

        RetryAction action = CurrentRetryAction;

        // Pasar de sesión espera a que estén todos.
        if (action != RetryAction.Retry && raceManager != null && !raceManager.IsFinished())
            return;

        switch (action)
        {
            case RetryAction.ToRace:
                RaceSession.GoToRace(RaceWeekend.Instance != null ? RaceWeekend.Instance.GetGridKeys() : null);
                break;

            case RetryAction.NextRound:
                RaceSession.GoToNextRound();
                break;

            case RetryAction.NewChampionship:
                RaceSession.RestartChampionship();
                break;
        }

        NetworkManager manager = NetworkManager.Singleton;
        string scene = SceneManager.GetActiveScene().name;

        if (manager != null && manager.IsListening)
        {
            if (manager.IsServer)
                manager.SceneManager.LoadScene(scene, LoadSceneMode.Single);

            return;
        }

        SceneManager.LoadScene(scene);
    }

    private void GoToMenu()
    {
        // Se sale de la partida: el menú arranca sin red.
        if (GameSession.Instance != null)
        {
            GameSession.Instance.LeaveToMenu();
            return;
        }

        NetworkManager manager = NetworkManager.Singleton;

        if (manager != null)
        {
            manager.Shutdown();
            Destroy(manager.gameObject);
        }

        SceneManager.LoadScene(menuSceneName);
    }

    /// <summary>Formato de carrera: m:ss.mmm, siempre con punto decimal.</summary>
    private static string FormatTime(float seconds)
    {
        int minutes = Mathf.FloorToInt(seconds / 60f);
        float rest = seconds - minutes * 60f;

        return minutes + ":" + rest.ToString("00.000", CultureInfo.InvariantCulture);
    }

    /// <summary>Tiempo de reacción con milésimas; la salida en falso, negativa y en otro color.</summary>
    private static string FormatReaction(RaceTimingManager.Result result)
    {
        if (!result.HasReactionTime)
            return "-";

        string text = result.ReactionTime.ToString("0.000", CultureInfo.InvariantCulture);

        return result.ReactionTime < 0f ? "<color=" + JumpStartColor + ">" + text + "</color>" : text;
    }

    private static string FormatSeconds(float seconds)
    {
        return seconds.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
