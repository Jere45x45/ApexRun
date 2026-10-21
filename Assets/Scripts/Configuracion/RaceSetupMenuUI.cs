using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pantalla de configuración de la carrera del menú de Inicio. La abren
/// Un jugador y Crear servidor; el que crea la partida decide:
/// - Vueltas (de 1 a 10), con − y +.
/// - Clima (despejado, nublado, lluvia, tormenta...), Hora (mañana,
///   mediodía, atardecer, noche) y Niebla (sin, leve, densa), con &lt; y &gt;.
///   Cada una puede quedar AL AZAR (WeatherChoiceStore).
/// - Partida (solo al crear un servidor): PÚBLICA aparece en la lista de
///   servidores; PRIVADA no, y se entra solo con el código (RaceSetupStore).
/// - Bots (de 0 a 6), con − y +, y su Nivel (fácil, medio, difícil), con
///   &lt; y &gt;. Un jugador y Crear servidor guardan la cantidad aparte: al
///   crear un servidor arranca en 0, así solo hay bots si se ponen.
/// - Pista se ve pero todavía no se puede cambiar.
/// Lo elegido queda guardado (RaceSetupStore, WeatherChoiceStore) y lo
/// aplican en la carrera RaceSetupSync y RaceWeather.
/// El botón de abajo dice ¡A CORRER! (un jugador) o CREAR SERVIDOR (red).
/// </summary>
public class RaceSetupMenuUI : MonoBehaviour
{
    [SerializeField] private MenuManager menuManager;
    [SerializeField] private SessionMenuUI sessionMenu;

    [SerializeField] private TMP_Text titleText;

    [Header("Vueltas")]
    [SerializeField] private Button lapsMinus;
    [SerializeField] private Button lapsPlus;
    [SerializeField] private TMP_Text lapsValue;

    [Header("Clima")]
    [SerializeField] private Button weatherPrevious;
    [SerializeField] private Button weatherNext;
    [SerializeField] private TMP_Text weatherValue;

    [Header("Hora")]
    [SerializeField] private Button timePrevious;
    [SerializeField] private Button timeNext;
    [SerializeField] private TMP_Text timeValue;

    [Header("Niebla")]
    [SerializeField] private Button fogPrevious;
    [SerializeField] private Button fogNext;
    [SerializeField] private TMP_Text fogValue;

    [Header("Bots")]
    [SerializeField] private Button botsMinus;
    [SerializeField] private Button botsPlus;
    [SerializeField] private TMP_Text botsValue;

    [Header("Nivel de los bots")]
    [SerializeField] private Button levelPrevious;
    [SerializeField] private Button levelNext;
    [SerializeField] private TMP_Text levelValue;

    [Header("Partida (solo Crear servidor)")]
    [Tooltip("Fila entera: se muestra solo al crear un servidor.")]
    [SerializeField] private GameObject visibilityRow;
    [SerializeField] private Button visibilityPrevious;
    [SerializeField] private Button visibilityNext;
    [SerializeField] private TMP_Text visibilityValue;

    [Header("Abajo")]
    [SerializeField] private Button confirmButton;
    [SerializeField] private TMP_Text confirmText;
    [SerializeField] private Button backButton;

    private bool multiplayer;

    private void Awake()
    {
        lapsMinus.onClick.AddListener(() => ChangeLaps(-1));
        lapsPlus.onClick.AddListener(() => ChangeLaps(+1));

        weatherPrevious.onClick.AddListener(() => ChangeWeather(-1));
        weatherNext.onClick.AddListener(() => ChangeWeather(+1));

        timePrevious.onClick.AddListener(() => ChangeTime(-1));
        timeNext.onClick.AddListener(() => ChangeTime(+1));

        fogPrevious.onClick.AddListener(() => ChangeFog(-1));
        fogNext.onClick.AddListener(() => ChangeFog(+1));

        if (botsMinus != null)
            botsMinus.onClick.AddListener(() => ChangeBots(-1));

        if (botsPlus != null)
            botsPlus.onClick.AddListener(() => ChangeBots(+1));

        if (levelPrevious != null)
            levelPrevious.onClick.AddListener(() => ChangeLevel(-1));

        if (levelNext != null)
            levelNext.onClick.AddListener(() => ChangeLevel(+1));

        if (visibilityPrevious != null)
            visibilityPrevious.onClick.AddListener(ToggleVisibility);

        if (visibilityNext != null)
            visibilityNext.onClick.AddListener(ToggleVisibility);

        confirmButton.onClick.AddListener(Confirm);
        backButton.onClick.AddListener(Back);
    }

    /// <summary>Muestra la pantalla para un jugador (false) o para crear un servidor (true).</summary>
    public void Open(bool forMultiplayer)
    {
        multiplayer = forMultiplayer;
        menuManager.Show(MenuPanel.RaceSetup);
        Refresh();
    }

    private void ChangeLaps(int delta)
    {
        RaceSetupStore.Laps += delta;
        Refresh();
    }

    private void ChangeWeather(int direction)
    {
        WeatherChoiceStore.Current = WeatherChoiceStore.Step(WeatherChoiceStore.Current, direction);
        Refresh();
    }

    private void ChangeTime(int direction)
    {
        WeatherChoiceStore.Time = WeatherChoiceStore.Step(WeatherChoiceStore.Time, direction);
        Refresh();
    }

    private void ChangeFog(int direction)
    {
        WeatherChoiceStore.Fog = WeatherChoiceStore.Step(WeatherChoiceStore.Fog, direction);
        Refresh();
    }

    private void ChangeBots(int delta)
    {
        RaceSetupStore.SetBots(multiplayer, RaceSetupStore.GetBots(multiplayer) + delta);
        Refresh();
    }

    private void ChangeLevel(int direction)
    {
        int count = System.Enum.GetValues(typeof(RaceDifficulty)).Length;
        int level = ((int)RaceSetupStore.BotDifficulty + direction + count) % count;

        RaceSetupStore.BotDifficulty = (RaceDifficulty)level;
        Refresh();
    }

    /// <summary>Pública ⇄ privada (solo hay dos, así que &lt; y &gt; hacen lo mismo).</summary>
    private void ToggleVisibility()
    {
        RaceSetupStore.PrivateServer = !RaceSetupStore.PrivateServer;
        Refresh();
    }

    private void Confirm()
    {
        // Los bots de esta carrera (los pone el host al cargar la pista).
        RaceSetupStore.BotsForRace = RaceSetupStore.GetBots(multiplayer);

        if (multiplayer)
        {
            // La sala (código y jugadores) está en la pantalla de Multijugador.
            menuManager.Show(MenuPanel.Multiplayer);
            sessionMenu.CreateFromSetup();
        }
        else
        {
            confirmButton.interactable = false;
            sessionMenu.StartSinglePlayerFromSetup();
        }
    }

    private void Back()
    {
        menuManager.Show(multiplayer ? MenuPanel.Multiplayer : MenuPanel.Main);
    }

    private void Refresh()
    {
        if (titleText != null)
            titleText.text = multiplayer ? "CREAR SERVIDOR" : "UN JUGADOR";

        if (confirmText != null)
            confirmText.text = multiplayer ? "CREAR SERVIDOR" : "¡A CORRER!";

        confirmButton.interactable = true;

        int laps = RaceSetupStore.Laps;
        lapsValue.text = laps.ToString();
        lapsMinus.interactable = laps > RaceSetupStore.MinLaps;
        lapsPlus.interactable = laps < RaceSetupStore.MaxLaps;

        weatherValue.text = WeatherChoiceStore.Label(WeatherChoiceStore.Current);
        timeValue.text = WeatherChoiceStore.Label(WeatherChoiceStore.Time);
        fogValue.text = WeatherChoiceStore.Label(WeatherChoiceStore.Fog);

        int bots = RaceSetupStore.GetBots(multiplayer);

        if (botsValue != null)
            botsValue.text = bots == 0 ? "SIN BOTS" : bots.ToString();

        if (botsMinus != null)
            botsMinus.interactable = bots > 0;

        if (botsPlus != null)
            botsPlus.interactable = bots < RaceSetupStore.MaxBots;

        // Sin bots, el nivel no importa: se ve apagado.
        if (levelValue != null)
        {
            levelValue.text = RaceDifficultySettings.Label(RaceSetupStore.BotDifficulty);
            levelValue.alpha = bots > 0 ? 1f : 0.4f;
        }

        if (levelPrevious != null)
            levelPrevious.interactable = bots > 0;

        if (levelNext != null)
            levelNext.interactable = bots > 0;

        if (visibilityRow != null)
            visibilityRow.SetActive(multiplayer);

        if (visibilityValue != null)
            visibilityValue.text = RaceSetupStore.PrivateServer ? "PRIVADA" : "PÚBLICA";
    }
}
