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
/// El cartel de arriba dice Create Server o Un jugador (con su ícono); los
/// textos están en el Inspector, en español y en inglés (GameLanguage).
/// </summary>
public class RaceSetupMenuUI : MonoBehaviour
{
    [SerializeField] private MenuManager menuManager;
    [SerializeField] private SessionMenuUI sessionMenu;

    [Header("Cartel del título")]
    [Tooltip("Texto del cartel de arriba.")]
    [SerializeField] private TMP_Text titleText;

    [Tooltip("Ícono al lado del título (opcional).")]
    [SerializeField] private Image titleIcon;

    [SerializeField] private Sprite multiplayerIcon;
    [SerializeField] private Sprite singlePlayerIcon;

    [Tooltip("Título al crear un servidor: en español y en inglés.")]
    [SerializeField] private string multiplayerTitleEs = "Crear servidor";
    [SerializeField] private string multiplayerTitleEn = "Create Server";

    [Tooltip("Título en un jugador: en español y en inglés.")]
    [SerializeField] private string singlePlayerTitleEs = "Un jugador";
    [SerializeField] private string singlePlayerTitleEn = "Single Player";

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

    [Tooltip("Fondo de la fila Nivel: sin bots usa el recuadro deshabilitado.")]
    [SerializeField] private Image levelRow;
    [SerializeField] private TMP_Text levelLabel;
    [SerializeField] private Sprite rowSprite;
    [SerializeField] private Sprite disabledRowSprite;

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
        // El cartel es liso: el texto y el ícono van encima.
        if (titleText != null)
        {
            titleText.text = multiplayer
                ? GameLanguage.Text(multiplayerTitleEs, multiplayerTitleEn)
                : GameLanguage.Text(singlePlayerTitleEs, singlePlayerTitleEn);
        }

        if (titleIcon != null)
        {
            titleIcon.sprite = multiplayer ? multiplayerIcon : singlePlayerIcon;
            titleIcon.gameObject.SetActive(titleIcon.sprite != null);
        }

        confirmButton.interactable = true;

        int laps = RaceSetupStore.Laps;
        lapsValue.text = laps.ToString();
        SetUsable(lapsMinus, laps > RaceSetupStore.MinLaps);
        SetUsable(lapsPlus, laps < RaceSetupStore.MaxLaps);

        weatherValue.text = WeatherChoiceStore.Label(WeatherChoiceStore.Current);
        timeValue.text = WeatherChoiceStore.Label(WeatherChoiceStore.Time);
        fogValue.text = WeatherChoiceStore.Label(WeatherChoiceStore.Fog);

        int bots = RaceSetupStore.GetBots(multiplayer);

        if (botsValue != null)
            botsValue.text = bots == 0 ? GameLanguage.Text("SIN BOTS", "NO BOTS") : bots.ToString();

        SetUsable(botsMinus, bots > 0);
        SetUsable(botsPlus, bots < RaceSetupStore.MaxBots);

        // Sin bots, el nivel no importa: se ve apagado.
        if (levelValue != null)
        {
            levelValue.text = LevelLabel(RaceSetupStore.BotDifficulty);
            levelValue.alpha = bots > 0 ? 1f : 0.4f;
        }

        SetUsable(levelPrevious, bots > 0);
        SetUsable(levelNext, bots > 0);

        if (levelRow != null && rowSprite != null && disabledRowSprite != null)
            levelRow.sprite = bots > 0 ? rowSprite : disabledRowSprite;

        if (levelLabel != null)
            levelLabel.alpha = bots > 0 ? 1f : 0.4f;

        if (visibilityRow != null)
            visibilityRow.SetActive(multiplayer);

        if (visibilityValue != null)
            visibilityValue.text = RaceSetupStore.PrivateServer ? GameLanguage.Text("PRIVADA", "PRIVATE") : GameLanguage.Text("PÚBLICA", "PUBLIC");
    }

    /// <summary>Nivel de los bots en el idioma del juego.</summary>
    private static string LevelLabel(RaceDifficulty difficulty)
    {
        switch (difficulty)
        {
            case RaceDifficulty.Easy: return GameLanguage.Text("FÁCIL", "EASY");
            case RaceDifficulty.Hard: return GameLanguage.Text("DIFÍCIL", "HARD");
            default: return GameLanguage.Text("MEDIO", "MEDIUM");
        }
    }

    /// <summary>
    /// Habilita o deshabilita un botón. Los botones usan sprites (no cambian
    /// de color solos), así que deshabilitado se ve semitransparente.
    /// </summary>
    private static void SetUsable(Button button, bool usable)
    {
        if (button == null)
            return;

        button.interactable = usable;

        if (button.targetGraphic != null)
        {
            Color color = button.targetGraphic.color;
            color.a = usable ? 1f : 0.35f;
            button.targetGraphic.color = color;
        }
    }
}
