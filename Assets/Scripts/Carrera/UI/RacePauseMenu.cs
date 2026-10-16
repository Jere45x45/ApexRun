using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Pausa de la carrera, solo en un jugador (en red la carrera es de todos y no
/// se puede frenar).
/// - Esc, P o Start del joystick pausan y reanudan, durante la cuenta
///   regresiva y la carrera (no después de cruzar la meta).
/// - También se pausa sola si el juego pierde el foco (alt-tab, clic en otra
///   ventana, minimizar). Al volver queda en pausa hasta tocar CONTINUAR.
/// - Al pausar se congela todo: la física y lo que usa el tiempo del juego
///   (Time.timeScale = 0), el sonido (AudioListener.pause) y el reloj de la
///   carrera (RaceManager.PauseClock: el cronómetro y la cuenta regresiva no
///   avanzan).
/// - Menú: CONTINUAR, REINICIAR (vuelve a cargar la carrera, como Reintentar
///   de los resultados) y SALIR AL MENÚ (vuelve al Inicio).
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

    [Tooltip("Pausar solo cuando el juego pierde el foco (alt-tab, otra ventana, minimizar).")]
    [SerializeField] private bool pauseOnFocusLost = true;

    [Header("Escenas")]
    [Tooltip("Escena del menú, si no hay GameSession (por ejemplo, al probar Race directo en el editor).")]
    [SerializeField] private string menuSceneName = "Inicio";

    private InputAction pauseAction;

    private bool paused;
    private float timeScaleBeforePause = 1f;

    /// <summary>True mientras la carrera está en pausa.</summary>
    public bool IsPaused => paused;

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
        if (!pauseAction.WasPressedThisFrame())
            return;

        if (paused)
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
        if (pauseOnFocusLost && !paused && CanPause())
            Pause();
    }

    /// <summary>Se puede pausar en un jugador, durante la cuenta regresiva o la carrera.</summary>
    public bool CanPause()
    {
        if (raceManager == null || !raceManager.IsSpawned || !IsSinglePlayer())
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
        if (paused)
            return;

        paused = true;

        timeScaleBeforePause = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        AudioListener.pause = true;

        raceManager.PauseClock();

        if (panel != null)
            panel.SetActive(true);

        // Para manejar los botones con joystick o teclado.
        if (continueButton != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(continueButton.gameObject);
    }

    public void Resume()
    {
        if (!paused)
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

    /// <summary>Saca la pausa: tiempo, sonido y reloj de la carrera.</summary>
    private void ReleasePause()
    {
        if (!paused)
            return;

        paused = false;

        Time.timeScale = timeScaleBeforePause;
        AudioListener.pause = false;

        if (raceManager != null && raceManager.IsSpawned)
            raceManager.ResumeClock();
    }
}
