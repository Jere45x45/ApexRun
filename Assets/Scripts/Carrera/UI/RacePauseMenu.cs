using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Menú de pausa de la carrera.
/// - Esc, P o Start del joystick lo abren y lo cierran, durante la cuenta
///   regresiva y la carrera (no después de cruzar la meta).
/// - En un jugador es una pausa de verdad: se congela todo, la física y lo
///   que usa el tiempo del juego (Time.timeScale = 0), el sonido
///   (AudioListener.pause) y el reloj de la carrera (RaceManager.PauseClock:
///   el cronómetro y la cuenta regresiva no avanzan). También se pausa solo
///   si el juego pierde el foco (alt-tab, clic en otra ventana, minimizar), y
///   al volver queda en pausa hasta tocar CONTINUAR.
/// - En red la carrera es de todos y no se frena: el menú se abre arriba de
///   la carrera, que sigue andando. Mientras está abierto, el kart propio no
///   recibe los mandos (sigue de largo sin acelerar, como si soltaras todo).
///   No se abre solo al perder el foco y no tiene REINICIAR.
/// - Menú: CONTINUAR, REINICIAR (vuelve a cargar la carrera, como Reintentar
///   de los resultados; solo en un jugador) y SALIR AL MENÚ (vuelve al Inicio).
/// Si la escena se cierra estando en pausa, deja el tiempo y el sonido como
/// estaban.
/// </summary>
public class RacePauseMenu : MonoBehaviour
{
    [SerializeField] private RaceManager raceManager;

    [Header("Pantalla")]
    [Tooltip("Panel de pausa (apagado mientras se corre).")]
    [SerializeField] private GameObject panel;

    [SerializeField] private Button continueButton;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button menuButton;

    [Tooltip("Pausar solo cuando el juego pierde el foco (alt-tab, otra ventana, minimizar). Solo en un jugador.")]
    [SerializeField] private bool pauseOnFocusLost = true;

    [Header("Escenas")]
    [Tooltip("Escena del menú, si no hay GameSession (por ejemplo, al probar Race directo en el editor).")]
    [SerializeField] private string menuSceneName = "Inicio";

    private InputAction pauseAction;

    // Menú abierto (en un jugador, además, todo congelado).
    private bool open;

    // True si al abrirlo se congeló la carrera (un jugador).
    private bool frozen;

    private float timeScaleBeforePause = 1f;

    // Lugar de SALIR AL MENÚ en el panel: sin REINICIAR, pasa a su lugar.
    private Vector2 menuButtonPosition;
    private bool hasMenuButtonPosition;

    // Mandos del jugador apagados mientras el menú está abierto en red.
    private readonly List<PlayerKartInput> blockedInputs = new List<PlayerKartInput>();

    /// <summary>True mientras el menú de pausa está abierto (en red, la carrera sigue).</summary>
    public bool IsOpen => open;

    /// <summary>True mientras la carrera está congelada (solo en un jugador).</summary>
    public bool IsPaused => frozen;

    private void Awake()
    {
        pauseAction = new InputAction("Pause", InputActionType.Button);
        pauseAction.AddBinding("<Keyboard>/escape");
        pauseAction.AddBinding("<Keyboard>/p");
        pauseAction.AddBinding("<Gamepad>/start");

        if (continueButton != null)
            continueButton.onClick.AddListener(Resume);

        if (restartButton != null)
            restartButton.onClick.AddListener(Restart);

        if (menuButton != null)
            menuButton.onClick.AddListener(GoToMenu);

        if (panel != null)
            panel.SetActive(false);

        if (menuButton != null && menuButton.transform is RectTransform menuRect)
        {
            menuButtonPosition = menuRect.anchoredPosition;
            hasMenuButtonPosition = true;
        }
    }

    private void OnEnable()
    {
        pauseAction.Enable();
    }

    private void OnDisable()
    {
        pauseAction.Disable();
        ReleasePause();
    }

    private void OnDestroy()
    {
        ReleasePause();
        pauseAction.Dispose();
    }

    private void Update()
    {
        if (open && !frozen && !CanPause())
        {
            // En red la carrera sigue: si terminó con el menú abierto, se
            // cierra para que se vean los resultados.
            Resume();
            return;
        }

        if (!pauseAction.WasPressedThisFrame())
            return;

        if (open)
            Resume();
        else if (CanPause())
            Pause();
    }

    // El juego dejó de estar en primer plano (alt-tab, clic en otra ventana).
    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            PauseForFocusLoss();
    }

    // Minimizado (o la app pasó a segundo plano en el celular).
    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
            PauseForFocusLoss();
    }

    private void PauseForFocusLoss()
    {
        // En red no: la carrera sigue igual y el menú solo taparía la pista.
        if (pauseOnFocusLost && !open && CanPause() && IsSinglePlayer())
            Pause();
    }

    /// <summary>Se puede abrir durante la cuenta regresiva o la carrera.</summary>
    public bool CanPause()
    {
        if (raceManager == null || !raceManager.IsSpawned)
            return false;

        return raceManager.IsCountdown() || raceManager.IsRacing();
    }

    /// <summary>
    /// Un jugador: esta computadora decide la carrera (host o sin red), no hay
    /// partida en línea y no hay nadie más conectado.
    /// </summary>
    public static bool IsSinglePlayer()
    {
        if (!NetworkRole.IsAuthority)
            return false;

        if (GameSession.Instance != null && GameSession.Instance.Session != null)
            return false;

        NetworkManager manager = NetworkManager.Singleton;
        return manager == null || !manager.IsListening || manager.ConnectedClientsIds.Count <= 1;
    }

    public void Pause()
    {
        if (open)
            return;

        open = true;

        if (IsSinglePlayer())
        {
            frozen = true;

            timeScaleBeforePause = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            AudioListener.pause = true;

            raceManager.PauseClock();
        }
        else
        {
            BlockPlayerInput();
        }

        // Reiniciar la carrera es para un jugador: en red la reinicia el anfitrión para todos.
        ShowRestartButton(frozen);

        if (panel != null)
            panel.SetActive(true);

        // Para manejar los botones con joystick o teclado.
        if (continueButton != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(continueButton.gameObject);
    }

    public void Resume()
    {
        if (!open)
            return;

        ReleasePause();

        if (panel != null)
            panel.SetActive(false);

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    /// <summary>Vuelve a cargar la carrera (con la misma configuración).</summary>
    private void Restart()
    {
        ReleasePause();

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

    /// <summary>Sale de la partida y vuelve al Inicio.</summary>
    private void GoToMenu()
    {
        ReleasePause();

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

    /// <summary>Muestra u oculta REINICIAR. Sin él, SALIR AL MENÚ sube a su lugar y no queda un hueco.</summary>
    private void ShowRestartButton(bool show)
    {
        if (restartButton == null)
            return;

        restartButton.gameObject.SetActive(show);

        if (!hasMenuButtonPosition || !(menuButton.transform is RectTransform menuRect) ||
            !(restartButton.transform is RectTransform restartRect))
        {
            return;
        }

        menuRect.anchoredPosition = show ? menuButtonPosition : restartRect.anchoredPosition;
    }

    /// <summary>
    /// En red: el teclado deja de manejar el kart propio mientras el menú está
    /// abierto, y el kart queda sin acelerar, sin frenar y con el volante derecho.
    /// </summary>
    private void BlockPlayerInput()
    {
        blockedInputs.Clear();

        foreach (PlayerKartInput input in FindObjectsByType<PlayerKartInput>(FindObjectsSortMode.None))
        {
            if (!input.enabled)
                continue;

            input.enabled = false;
            blockedInputs.Add(input);

            KartBehaviour kart = input.GetComponent<KartBehaviour>();

            if (kart != null)
                kart.SetInputs(0f, 0f, false);
        }
    }

    private void RestorePlayerInput()
    {
        foreach (PlayerKartInput input in blockedInputs)
        {
            if (input != null)
                input.enabled = true;
        }

        blockedInputs.Clear();
    }

    /// <summary>Saca la pausa: tiempo, sonido y reloj de la carrera, o los mandos en red.</summary>
    private void ReleasePause()
    {
        if (!open)
            return;

        open = false;

        RestorePlayerInput();

        if (!frozen)
            return;

        frozen = false;

        Time.timeScale = timeScaleBeforePause;
        AudioListener.pause = false;

        if (raceManager != null && raceManager.IsSpawned)
            raceManager.ResumeClock();
    }
}
