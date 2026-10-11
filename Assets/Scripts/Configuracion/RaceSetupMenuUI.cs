using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pantalla de configuración de la carrera del menú de Inicio. La abren
/// Un jugador y Crear servidor; el que crea la partida decide:
/// - Vueltas (de 1 a 10), con − y +.
/// - Clima (WeatherChoiceStore), con &lt; y &gt;.
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

    [Header("Abajo")]
    [SerializeField] private Button confirmButton;
    [SerializeField] private TMP_Text confirmText;
    [SerializeField] private Button backButton;

    private bool multiplayer;

    private void Awake()
    {
        lapsMinus.onClick.AddListener(() => ChangeLaps(-1));
        lapsPlus.onClick.AddListener(() => ChangeLaps(+1));

        weatherPrevious.onClick.AddListener(() => ChangeWeather(false));
        weatherNext.onClick.AddListener(() => ChangeWeather(true));

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

    private void ChangeWeather(bool next)
    {
        WeatherChoice current = WeatherChoiceStore.Current;
        WeatherChoiceStore.Current = next ? WeatherChoiceStore.Next(current) : WeatherChoiceStore.Previous(current);
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
    }
}
