using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// La partida, de punta a punta. Vive mientras dura el juego (DontDestroyOnLoad):
/// - Un jugador: host local en 127.0.0.1, sin servicios ni internet.
/// - Multijugador: Unity Multiplayer Services. El anfitrión crea una sesión
///   privada con Relay (se juega por internet sin abrir puertos) y los demás
///   entran con el código. La sesión arranca Netcode sola (host o cliente).
/// - Pasa de escena a todos juntos (NetworkSceneManager): Inicio → Catálogo → Race.
/// - Salir o perder la conexión vuelve al Inicio, con un mensaje si hace falta.
/// </summary>
public class GameSession : MonoBehaviour
{
    [Tooltip("Prefab con el NetworkManager y el transporte.")]
    [SerializeField] private NetworkManager networkManagerPrefab;

    [Header("Escenas")]
    [SerializeField] private string menuScene = "Inicio";

    [SerializeField] private string catalogScene = "Catalog";

    [SerializeField] private string raceScene = "Race";

    [Header("Partida")]
    [Tooltip("Jugadores por partida (lugares en la grilla).")]
    [SerializeField, Range(2, 6)] private int maxPlayers = 6;

    private const string LocalAddress = "127.0.0.1";
    private const ushort LocalPort = 7777;

    private bool leaving;

    public static GameSession Instance { get; private set; }

    /// <summary>La sesión de multijugador (null en un jugador o sin partida).</summary>
    public ISession Session { get; private set; }

    /// <summary>Nombre que eligió el jugador de esta computadora.</summary>
    public string LocalPlayerName { get; private set; } = "Piloto";

    /// <summary>True mientras se conecta, crea o se une (para bloquear los botones).</summary>
    public bool IsBusy { get; private set; }

    /// <summary>Mensaje para mostrar al volver al Inicio (por ejemplo, si se cortó la conexión).</summary>
    public string PendingMessage { get; set; }

    public bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    public string RaceScene => raceScene;

    /// <summary>Algo cambió en la sesión: jugadores que entran o salen, estado.</summary>
    public event Action Changed;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // ───────────── Un jugador ─────────────

    /// <summary>Arranca un host local (sin internet) y va al Catálogo.</summary>
    public void StartSinglePlayer()
    {
        if (IsBusy)
            return;

        NetworkManager manager = EnsureNetworkManager();
        UnityTransport transport = manager.GetComponent<UnityTransport>();

        if (transport != null)
            transport.SetConnectionData(LocalAddress, LocalPort, LocalAddress);

        leaving = false;

        if (!manager.StartHost())
        {
            PendingMessage = "No se pudo arrancar el juego.";
            Changed?.Invoke();
            return;
        }

        LoadSceneForAll(catalogScene);
    }

    // ───────────── Multijugador ─────────────

    /// <summary>Crea una partida privada por Relay. Devuelve null si salió bien, o el error para mostrar.</summary>
    public async Task<string> CreateSessionAsync(string playerName)
    {
        return await RunSessionTask(playerName, async () =>
        {
            SessionOptions options = new SessionOptions
            {
                MaxPlayers = maxPlayers,
                IsPrivate = true
            }
            .WithRelayNetwork()
            .WithPlayerName(VisibilityPropertyOptions.Member);

            return await MultiplayerService.Instance.CreateSessionAsync(options);
        });
    }

    /// <summary>Se une a una partida con su código. Devuelve null si salió bien, o el error para mostrar.</summary>
    public async Task<string> JoinSessionAsync(string code, string playerName)
    {
        code = (code ?? "").Trim().ToUpperInvariant();

        if (code.Length == 0)
            return "Escribí el código de la partida.";

        return await RunSessionTask(playerName, () =>
        {
            JoinSessionOptions options = new JoinSessionOptions().WithPlayerName(VisibilityPropertyOptions.Member);

            return MultiplayerService.Instance.JoinSessionByCodeAsync(code, options);
        });
    }

    /// <summary>
    /// El anfitrión arranca: cierra la sesión a nuevos jugadores y lleva a
    /// todos al Catálogo.
    /// </summary>
    public async void StartMatch()
    {
        if (!IsHost)
            return;

        if (Session != null && Session.IsHost)
        {
            try
            {
                IHostSession host = Session.AsHost();
                host.IsLocked = true;
                await host.SavePropertiesAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"GameSession: no se pudo cerrar la sesión a nuevos jugadores ({exception.Message}).", this);
            }
        }

        LoadSceneForAll(catalogScene);
    }

    /// <summary>Nombres de los jugadores de la sesión (en un jugador, solo el propio).</summary>
    public List<string> GetPlayerNames()
    {
        List<string> names = new List<string>();

        if (Session == null)
        {
            names.Add(LocalPlayerName);
            return names;
        }

        foreach (IReadOnlyPlayer player in Session.Players)
            names.Add(CleanName(player.GetPlayerName()));

        return names;
    }

    // ───────────── Escenas y salida ─────────────

    /// <summary>
    /// Carga una escena para todos. Solo el anfitrión: los clientes la cargan
    /// cuando él la carga. Sin red, la carga solo acá.
    /// </summary>
    public void LoadSceneForAll(string sceneName)
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (manager != null && manager.IsListening)
        {
            if (manager.IsServer)
                manager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);

            return;
        }

        SceneManager.LoadScene(sceneName);
    }

    /// <summary>Sale de la partida (si hay) y vuelve al Inicio.</summary>
    public async void LeaveToMenu(string message = null)
    {
        if (leaving)
            return;

        leaving = true;
        PendingMessage = message;

        ISession session = Session;
        Session = null;

        if (session != null)
        {
            UnsubscribeSession(session);

            try
            {
                await session.LeaveAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"GameSession: error al salir de la sesión ({exception.Message}).", this);
            }
        }

        NetworkManager manager = NetworkManager.Singleton;

        if (manager != null)
        {
            manager.OnClientStopped -= HandleClientStopped;
            manager.Shutdown();
            Destroy(manager.gameObject);
        }

        SceneManager.LoadScene(menuScene);

        leaving = false;
        Changed?.Invoke();
    }

    // ───────────── Interno ─────────────

    private async Task<string> RunSessionTask(string playerName, Func<Task<ISession>> createSession)
    {
        if (IsBusy)
            return "Esperá un momento...";

        IsBusy = true;
        leaving = false;
        Changed?.Invoke();

        try
        {
            LocalPlayerName = CleanName(playerName);

            await SignInAsync();

            EnsureNetworkManager();

            Session = await createSession();
            SubscribeSession(Session);

            return null;
        }
        catch (SessionException exception)
        {
            Debug.LogWarning($"GameSession: {exception.Error} — {exception.Message}", this);
            return DescribeError(exception);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"GameSession: {exception}", this);
            return "No se pudo conectar. Revisá la conexión a internet.";
        }
        finally
        {
            IsBusy = false;
            Changed?.Invoke();
        }
    }

    private async Task SignInAsync()
    {
        if (UnityServices.State == ServicesInitializationState.Uninitialized)
        {
            InitializationOptions options = new InitializationOptions();
            options.SetProfile(GetProfileName());

            await UnityServices.InitializeAsync(options);
        }

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();

        // Los servicios no aceptan espacios en el nombre.
        string serviceName = LocalPlayerName.Replace(' ', '_');

        if (CleanName(AuthenticationService.Instance.PlayerName) != serviceName)
            await AuthenticationService.Instance.UpdatePlayerNameAsync(serviceName);
    }

    /// <summary>
    /// Perfil de cuenta anónima. En el editor, cada jugador virtual de
    /// Multiplayer Play Mode necesita el suyo: si no, todos serían el mismo
    /// jugador y no podrían entrar a la misma partida.
    /// </summary>
    private static string GetProfileName()
    {
#if UNITY_EDITOR
        if (!Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor)
            return "vp" + Mathf.Abs(Application.dataPath.GetHashCode());
#endif
        return "default";
    }

    private NetworkManager EnsureNetworkManager()
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (manager == null)
            manager = Instantiate(networkManagerPrefab);

        manager.OnClientStopped -= HandleClientStopped;
        manager.OnClientStopped += HandleClientStopped;

        return manager;
    }

    /// <summary>Netcode se cortó sin que el jugador lo pidiera (se fue el anfitrión, se cayó internet).</summary>
    private void HandleClientStopped(bool wasHost)
    {
        if (leaving)
            return;

        LeaveToMenu(wasHost ? null : "Se perdió la conexión con la partida.");
    }

    private void SubscribeSession(ISession session)
    {
        session.PlayerJoined += HandlePlayerJoined;
        session.PlayerHasLeft += HandlePlayerLeft;
        session.Changed += HandleSessionChanged;
        session.RemovedFromSession += HandleRemovedFromSession;
    }

    private void UnsubscribeSession(ISession session)
    {
        session.PlayerJoined -= HandlePlayerJoined;
        session.PlayerHasLeft -= HandlePlayerLeft;
        session.Changed -= HandleSessionChanged;
        session.RemovedFromSession -= HandleRemovedFromSession;
    }

    private void HandlePlayerJoined(string playerId) => Changed?.Invoke();

    private void HandlePlayerLeft(string playerId) => Changed?.Invoke();

    private void HandleSessionChanged() => Changed?.Invoke();

    private void HandleRemovedFromSession()
    {
        if (!leaving)
            LeaveToMenu("Saliste de la partida.");
    }

    private static string DescribeError(SessionException exception)
    {
        // Lobby avisa "llena" o "cerrada" en el mensaje ("lobby is locked").
        string inner = exception.Message + " " + (exception.InnerException != null ? exception.InnerException.Message : "");

        if (inner.IndexOf("full", StringComparison.OrdinalIgnoreCase) >= 0)
            return "La partida está llena.";

        if (inner.IndexOf("locked", StringComparison.OrdinalIgnoreCase) >= 0)
            return "La partida ya empezó.";

        // Código con letras que no se usan, o que no existe.
        if (inner.IndexOf("invalid character", StringComparison.OrdinalIgnoreCase) >= 0 ||
            inner.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0)
            return "No existe una partida con ese código.";

        switch (exception.Error)
        {
            case SessionError.SessionNotFound:
            case SessionError.InvalidSessionIdentifier:
                return "No existe una partida con ese código.";
            case SessionError.InvalidPlayerName:
                return "Ese nombre no se puede usar.";
            default:
                return "No se pudo conectar (" + exception.Error + ").";
        }
    }

    /// <summary>Saca el sufijo #1234 que agregan los servicios y limita el largo.</summary>
    private static string CleanName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Piloto";

        int hash = name.IndexOf('#');

        if (hash > 0)
            name = name.Substring(0, hash);

        name = name.Replace('_', ' ').Trim();

        return name.Length > 14 ? name.Substring(0, 14) : name;
    }
}
