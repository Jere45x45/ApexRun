using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Salida del Catálogo:
/// - En un jugador: A CORRER va directo a la carrera.
/// - En red: cada invitado elige sus piezas y toca LISTO. Mientras está listo
///   no puede cambiarlas (CANCELAR las habilita de nuevo). El anfitrión ve
///   quién está listo, y A CORRER se habilita cuando lo están todos.
/// - Volver sale de la partida y vuelve al Inicio.
/// </summary>
public class CatalogFlowUI : MonoBehaviour
{
    [Tooltip("Sala del Catálogo en red. En un jugador no se usa.")]
    [SerializeField] private CatalogLobby lobby;

    [Header("Botones")]
    [SerializeField] private Button raceButton;

    [SerializeField] private Button readyButton;

    [SerializeField] private TMP_Text readyButtonText;

    [SerializeField] private Button backButton;

    [Header("Textos")]
    [Tooltip("Qué falta para largar.")]
    [SerializeField] private TMP_Text waitingText;

    [Tooltip("Panel con la lista de jugadores (solo en red).")]
    [SerializeField] private GameObject playersPanel;

    [SerializeField] private TMP_Text playersText;

    [Tooltip("Lista de piezas: se bloquea mientras el jugador está listo.")]
    [SerializeField] private CanvasGroup selectionGroup;

    [Header("Escenas (sin GameSession, abriendo el Catálogo suelto)")]
    [SerializeField] private string raceScene = "Race";

    [SerializeField] private string menuScene = "Inicio";

    private const float RefreshInterval = 0.2f;
    private const string ReadyColor = "#5BE37D";
    private const string PickingColor = "#FFC23D";
    private const string HostColor = "#9AA4B2";

    private readonly StringBuilder builder = new StringBuilder();

    private float nextRefresh;
    private bool starting;

    /// <summary>Partida en red con sesión (no un jugador).</summary>
    private bool IsMultiplayer =>
        GameSession.Instance != null && GameSession.Instance.Session != null &&
        lobby != null && lobby.IsSpawned;

    private void Awake()
    {
        raceButton.onClick.AddListener(GoToRace);
        readyButton.onClick.AddListener(ToggleReady);
        backButton.onClick.AddListener(GoToMenu);
    }

    private void Start()
    {
        Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefresh)
            return;

        nextRefresh = Time.unscaledTime + RefreshInterval;
        Refresh();
    }

    private void Refresh()
    {
        bool authority = NetworkRole.IsAuthority;
        bool multiplayer = IsMultiplayer;
        bool localReady = multiplayer && lobby.IsLocalReady;

        raceButton.gameObject.SetActive(authority);
        readyButton.gameObject.SetActive(!authority);

        if (playersPanel != null)
            playersPanel.SetActive(multiplayer);

        if (selectionGroup != null)
            selectionGroup.interactable = !localReady;

        string status = "";

        if (authority)
        {
            bool canStart = true;

            if (multiplayer)
            {
                lobby.CountGuests(out int ready, out int total);
                canStart = ready == total;

                if (!canStart)
                    status = $"Esperando que estén listos ({ready}/{total})";
            }

            raceButton.interactable = canStart && !starting;
        }
        else
        {
            readyButton.interactable = multiplayer;
            SetText(readyButtonText, localReady ? "CANCELAR" : "¡LISTO!");
            status = localReady ? "Esperando al anfitrión..." : "Elegí tus piezas y tocá ¡LISTO!";
        }

        SetText(waitingText, status);

        if (waitingText != null)
            waitingText.gameObject.SetActive(status.Length > 0);

        if (multiplayer)
            SetText(playersText, BuildPlayerList());
    }

    private string BuildPlayerList()
    {
        builder.Clear();
        builder.Append("<b>JUGADORES</b>");

        for (int i = 0; i < lobby.Count; i++)
        {
            CatalogLobby.Player player = lobby[i];

            builder.Append('\n').Append(player.Name.ToString().ToUpperInvariant()).Append("  ");

            if (lobby.IsHostPlayer(player.ClientId))
                builder.Append("<color=").Append(HostColor).Append(">ANFITRIÓN</color>");
            else if (player.IsReady)
                builder.Append("<color=").Append(ReadyColor).Append(">LISTO</color>");
            else
                builder.Append("<color=").Append(PickingColor).Append(">eligiendo...</color>");
        }

        return builder.ToString();
    }

    private void ToggleReady()
    {
        if (IsMultiplayer)
            lobby.SetReady(!lobby.IsLocalReady);
    }

    private void GoToRace()
    {
        if (starting)
            return;

        if (IsMultiplayer)
        {
            lobby.CountGuests(out int ready, out int total);

            if (ready < total)
                return;
        }

        starting = true;
        raceButton.interactable = false;

        if (GameSession.Instance != null)
            GameSession.Instance.LoadSceneForAll(raceScene);
        else
            SceneManager.LoadScene(raceScene);
    }

    private void GoToMenu()
    {
        if (GameSession.Instance != null)
            GameSession.Instance.LeaveToMenu();
        else
            SceneManager.LoadScene(menuScene);
    }

    private static void SetText(TMP_Text text, string value)
    {
        if (text != null && text.text != value)
            text.text = value;
    }
}
