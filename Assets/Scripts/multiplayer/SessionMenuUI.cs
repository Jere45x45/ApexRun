using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Menú de Inicio conectado a GameSession:
/// - Un jugador: arranca la partida local y va al Catálogo.
/// - Multijugador: nombre del jugador, Crear partida (muestra el código para
///   pasarles a los demás) o Unirse con código. En la sala se ve quién entró;
///   el anfitrión aprieta Empezar y van todos al Catálogo.
/// - Abajo, un mensaje de estado (conectando, errores, conexión perdida).
/// </summary>
public class SessionMenuUI : MonoBehaviour
{
    [SerializeField] private MenuManager menuManager;

    [Header("Menú principal")]
    [SerializeField] private Button singlePlayerButton;

    [Header("Multijugador")]
    [SerializeField] private TMP_InputField nameInput;

    [SerializeField] private Button createButton;

    [SerializeField] private Button joinButton;

    [SerializeField] private Button backButton;

    [Header("Unirse")]
    [SerializeField] private GameObject joinBox;

    [SerializeField] private TMP_InputField codeInput;

    [SerializeField] private Button joinConfirmButton;

    [SerializeField] private Button joinCancelButton;

    [Header("Sala")]
    [SerializeField] private GameObject lobbyBox;

    [SerializeField] private TMP_Text codeText;

    [SerializeField] private Button copyCodeButton;

    [SerializeField] private TMP_Text playersText;

    [SerializeField] private Button startButton;

    [SerializeField] private TMP_Text waitingText;

    [SerializeField] private Button leaveButton;

    [Header("Estado")]
    [SerializeField] private TMP_Text statusText;

    private const string NameKey = "ApexRun.NombreJugador";

    private GameSession Session => GameSession.Instance;

    private void Awake()
    {
        singlePlayerButton.onClick.AddListener(HandleSinglePlayer);
        createButton.onClick.AddListener(HandleCreate);
        joinButton.onClick.AddListener(() => ShowJoinBox(true));
        backButton.onClick.AddListener(() => menuManager.Show(MenuPanel.Main));
        joinConfirmButton.onClick.AddListener(HandleJoin);
        joinCancelButton.onClick.AddListener(() => ShowJoinBox(false));
        copyCodeButton.onClick.AddListener(CopyCode);
        startButton.onClick.AddListener(() => Session.StartMatch());
        leaveButton.onClick.AddListener(() => Session.LeaveToMenu());

        nameInput.characterLimit = 14;
        codeInput.characterLimit = 8;
        codeInput.onValidateInput += (text, index, character) => char.ToUpperInvariant(character);
    }

    private void OnDestroy()
    {
        if (Session != null)
            Session.Changed -= Refresh;
    }

    private void Start()
    {
        if (Session == null)
        {
            Debug.LogError("SessionMenuUI necesita un GameSession en la escena.", this);
            enabled = false;
            return;
        }

        // Se suscribe acá y no en OnEnable: GameSession puede despertar después que este menú.
        Session.Changed += Refresh;

        nameInput.text = PlayerPrefs.GetString(NameKey, "");

        ShowJoinBox(false);
        lobbyBox.SetActive(false);

        // Mensaje pendiente (por ejemplo, se cortó la conexión).
        SetStatus(Session.PendingMessage);
        Session.PendingMessage = null;

        Refresh();
    }

    private void HandleSinglePlayer()
    {
        SetStatus(null);
        Session.StartSinglePlayer();
    }

    private async void HandleCreate()
    {
        SaveName();
        SetStatus("Creando partida...");

        string error = await Session.CreateSessionAsync(nameInput.text);

        SetStatus(error);
        Refresh();
    }

    private async void HandleJoin()
    {
        SaveName();
        SetStatus("Uniéndose...");

        string error = await Session.JoinSessionAsync(codeInput.text, nameInput.text);

        SetStatus(error);
        Refresh();
    }

    private void ShowJoinBox(bool visible)
    {
        joinBox.SetActive(visible);

        if (visible)
            codeInput.ActivateInputField();
    }

    private void CopyCode()
    {
        if (Session.Session == null)
            return;

        GUIUtility.systemCopyBuffer = Session.Session.Code;
        SetStatus("Código copiado.");
    }

    /// <summary>Muestra la sala si hay sesión, y bloquea los botones mientras se conecta.</summary>
    private void Refresh()
    {
        if (this == null || Session == null)
            return;

        bool busy = Session.IsBusy;
        bool inSession = Session.Session != null;

        createButton.interactable = !busy;
        joinButton.interactable = !busy;
        joinConfirmButton.interactable = !busy;
        nameInput.interactable = !busy;

        // En la sala solo se ve la sala.
        createButton.gameObject.SetActive(!inSession);
        joinButton.gameObject.SetActive(!inSession);
        nameInput.gameObject.SetActive(!inSession);
        backButton.gameObject.SetActive(!inSession);

        lobbyBox.SetActive(inSession);

        if (!inSession)
            return;

        menuManager.Show(MenuPanel.Multiplayer);
        joinBox.SetActive(false);

        codeText.text = "CÓDIGO  <b>" + Session.Session.Code + "</b>";

        var names = Session.GetPlayerNames();
        playersText.text = "JUGADORES (" + names.Count + "/" + Session.Session.MaxPlayers + ")\n• " + string.Join("\n• ", names);

        bool isHost = Session.Session.IsHost;
        startButton.gameObject.SetActive(isHost);
        waitingText.gameObject.SetActive(!isHost);
    }

    private void SaveName()
    {
        PlayerPrefs.SetString(NameKey, nameInput.text.Trim());
    }

    private void SetStatus(string message)
    {
        statusText.text = message ?? "";
    }
}
