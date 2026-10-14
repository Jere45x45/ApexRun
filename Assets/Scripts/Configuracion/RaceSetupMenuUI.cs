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
/// - Bots y Pista se ven pero todavía no se pueden cambiar.
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

    /// <summary>Pública ⇄ privada (solo hay dos, así que &lt; y &gt; hacen lo mismo).</summary>
    private void ToggleVisibility()
    {
        RaceSetupStore.PrivateServer = !RaceSetupStore.PrivateServer;
        Refresh();
    }

    private void Confirm()
    {
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

        if (visibilityRow != null)
            visibilityRow.SetActive(multiplayer);

        if (visibilityValue != null)
            visibilityValue.text = RaceSetupStore.PrivateServer ? "PRIVADA" : "PÚBLICA";
    }
}
