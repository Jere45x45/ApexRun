using System.Collections.Generic;
using TMPro;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pantalla "Unirse a servidor" del menú de Inicio:
/// - PIN: se escribe el código de la partida y ENTRAR.
/// - Servidores: la lista de partidas públicas abiertas (que no empezaron y
///   tienen lugar), con el anfitrión, los jugadores, las vueltas y el clima.
///   Se actualiza sola cada pocos segundos mientras la pantalla está abierta.
///   Un clic en una partida entra.
/// Al entrar, SessionMenuUI muestra la sala (pantalla de Multijugador).
/// </summary>
public class JoinServerMenuUI : MonoBehaviour
{
    [SerializeField] private MenuManager menuManager;
    [SerializeField] private SessionMenuUI sessionMenu;

    [Header("PIN")]
    [SerializeField] private TMP_InputField pinInput;
    [SerializeField] private Button joinButton;

    [Header("Servidores")]
    [Tooltip("Donde van las filas de la lista.")]
    [SerializeField] private RectTransform listContent;

    [Tooltip("Fila modelo (apagada): se copia una por partida.")]
    [SerializeField] private Button entryTemplate;

    [Tooltip("Texto cuando no hay partidas o se están buscando.")]
    [SerializeField] private TMP_Text emptyText;

    [SerializeField] private Button refreshButton;

    [Header("Abajo")]
    [SerializeField] private Button backButton;

    [Tooltip("Cada cuántos segundos se vuelve a buscar partidas mientras la pantalla está abierta.")]
    [SerializeField] private float refreshInterval = 5f;

    private readonly List<GameObject> entries = new List<GameObject>();

    private float nextRefresh;
    private bool querying;

    private void Awake()
    {
        joinButton.onClick.AddListener(JoinWithPin);
        refreshButton.onClick.AddListener(RefreshList);
        backButton.onClick.AddListener(() => menuManager.Show(MenuPanel.Multiplayer));

        pinInput.characterLimit = 8;
        pinInput.onValidateInput += (text, index, character) => char.ToUpperInvariant(character);
        pinInput.onSubmit.AddListener(_ => JoinWithPin());

        entryTemplate.gameObject.SetActive(false);
    }

    public void Open()
    {
        menuManager.Show(MenuPanel.JoinServer);
        sessionMenu.ShowStatus(null);
        pinInput.text = "";
        RefreshList();
    }

    private void Update()
    {
        bool busy = GameSession.Instance != null && GameSession.Instance.IsBusy;

        joinButton.interactable = !busy;
        pinInput.interactable = !busy;

        foreach (GameObject entry in entries)
        {
            Button button = entry.GetComponent<Button>();

            if (button != null)
                button.interactable = !busy;
        }

        if (!busy && !querying && Time.unscaledTime >= nextRefresh)
            RefreshList();
    }

    private void JoinWithPin()
    {
        if (GameSession.Instance == null || GameSession.Instance.IsBusy)
            return;

        sessionMenu.JoinByCode(pinInput.text);
    }

    private async void RefreshList()
    {
        if (querying || GameSession.Instance == null)
            return;

        querying = true;
        nextRefresh = Time.unscaledTime + refreshInterval;

        if (entries.Count == 0)
            ShowEmpty(GameLanguage.Text("Buscando partidas...", "Looking for matches..."));

        string error = null;
        List<ISessionInfo> sessions = await GameSession.Instance.QuerySessionsAsync(sessionMenu.PlayerName, message => error = message);

        querying = false;
        nextRefresh = Time.unscaledTime + refreshInterval;

        // La pantalla se cerró (o se destruyó) mientras se buscaba.
        if (this == null || !isActiveAndEnabled)
            return;

        BuildList(sessions, error);
    }

    private void BuildList(List<ISessionInfo> sessions, string error)
    {
        foreach (GameObject entry in entries)
            Destroy(entry);

        entries.Clear();

        if (sessions == null)
        {
            ShowEmpty(error ?? GameLanguage.Text("No se pudieron buscar partidas.", "Couldn't look for matches."));
            return;
        }

        if (sessions.Count == 0)
        {
            ShowEmpty(GameLanguage.Text("No hay partidas abiertas.\nCreá una o entrá con un PIN.", "No open matches.\nCreate one or join with a PIN."));
            return;
        }

        ShowEmpty(null);

        foreach (ISessionInfo info in sessions)
        {
            Button entry = Instantiate(entryTemplate, listContent);
            entry.gameObject.SetActive(true);
            entry.name = "Partida " + info.Name;

            TMP_Text label = entry.GetComponentInChildren<TMP_Text>();

            if (label != null)
                label.text = Describe(info);

            string id = info.Id;
            entry.onClick.AddListener(() => sessionMenu.JoinById(id));

            entries.Add(entry.gameObject);
        }
    }

    private static string Describe(ISessionInfo info)
    {
        int players = info.MaxPlayers - info.AvailableSlots;
        string laps = GetProperty(info, GameSession.LapsProperty);
        string weather = GetProperty(info, GameSession.WeatherProperty);

        string name = string.IsNullOrWhiteSpace(info.Name) ? GameLanguage.Text("PARTIDA", "MATCH") : info.Name.ToUpperInvariant();
        string details = (laps != null ? laps + (laps == "1" ? GameLanguage.Text(" VUELTA", " LAP") : GameLanguage.Text(" VUELTAS", " LAPS")) : "") +
                         (weather != null ? (laps != null ? "  ·  " : "") + weather : "");

        return $"<b>{name}</b>  <color=#9AA4B2>{players}/{info.MaxPlayers}</color>\n<size=75%><color=#9AA4B2>{details}</color></size>";
    }

    private static string GetProperty(ISessionInfo info, string key)
    {
        if (info.Properties == null || !info.Properties.TryGetValue(key, out SessionProperty property) || property == null)
            return null;

        return string.IsNullOrWhiteSpace(property.Value) ? null : property.Value;
    }

    private void ShowEmpty(string message)
    {
        if (emptyText == null)
            return;

        emptyText.gameObject.SetActive(!string.IsNullOrEmpty(message));
        emptyText.text = message ?? "";
    }
}
