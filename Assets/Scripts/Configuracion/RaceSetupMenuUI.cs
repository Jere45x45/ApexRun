using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Pantalla de configuración de la carrera del menú de Inicio. La abren
/// Un jugador y Crear servidor; el que crea la partida decide:
/// - Vueltas (de 1 a 10), con − y +.
/// - Clima (despejado, nublado, lluvia, tormenta...), Hora (mañana,
///   mediodía, atardecer, noche) y Niebla (sin, leve, densa), con &lt; y &gt;.
///   Cada una puede quedar "Al azar" (WeatherChoiceStore).
/// - Partida (solo al crear un servidor): "Pública" aparece en la lista de
///   servidores; "Privada" no, y se entra solo con el código (RaceSetupStore).
/// - Bots (de 0 a 6), con − y +, y su Nivel (fácil, medio, difícil), con
///   &lt; y &gt;. Un jugador y Crear servidor guardan la cantidad aparte: al
///   crear un servidor arranca en 0, así solo hay bots si se ponen.
///   En un jugador, sin bots dice "Contrarreloj": se corre contra el fantasma
///   de la mejor vuelta (KartGhost).
/// - Pista: por ahora hay una sola; con &lt; y &gt; se elige el sentido
///   (normal o al revés, RaceSetupStore.Reversed).
/// - Modo: carrera suelta o campeonato de 2 a 6 fechas (cada fecha alterna el
///   sentido), y Qualy: sin qualy o una tanda de 3, 5 o 10 minutos antes de
///   cada carrera (la mejor vuelta arma la grilla). Estas dos filas se arman
///   solas copiando la de Niebla (no hay que tocar la escena); para que
///   entren, las filas se juntan un poco.
/// - Boxes: parada libre u obligatoria (una, en carreras de 2 vueltas o
///   más; ver PitStops). También se arma sola.
/// - Contrarreloj (un jugador sin bots): es de una sola vuelta, sin qualy,
///   campeonato ni parada obligatoria (esas filas se ven fijas). ¡A CORRER!
///   abre primero la tabla de clasificación de la pista (TimeTrialLeaderboardUI),
///   donde se eligen los rivales; ahí ¡A CORRER! larga y Volver vuelve acá.
/// Al confirmar se arma RaceSession (qualy, campeonato, sentido y boxes).
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

    // Tabla de la contrarreloj (se arma la primera vez que hace falta).
    private TimeTrialLeaderboardUI leaderboard;

    // Al volver de la repetición de la tabla: abrir la misma pestaña (seca o mojada).
    private bool reopenLastTab;

    /// <summary>Un jugador sin bots: contrarreloj de una vuelta con tabla.</summary>
    private bool IsTimeTrial => !multiplayer && RaceSetupStore.GetBots(false) == 0;

    // Filas que se arman solas (copias de la de Niebla) y la de Pista.
    private Button modePrevious;
    private Button modeNext;
    private TMP_Text modeValue;
    private Button qualifyingPrevious;
    private Button qualifyingNext;
    private TMP_Text qualifyingValue;
    private Button directionPrevious;
    private Button directionNext;
    private TMP_Text trackValue;
    private string trackSpanish = "Circuito 1";
    private string trackEnglish = "Circuit 1";

    private Button pitPrevious;
    private Button pitNext;
    private TMP_Text pitValue;

    // Para acomodar las filas (LayoutRows).
    private readonly List<RectTransform> layoutRows = new List<RectTransform>();
    private readonly List<int> layoutLevels = new List<int>();
    private readonly List<RectTransform> buttonRects = new List<RectTransform>();
    private readonly List<Vector2> buttonPositions = new List<Vector2>();
    private RectTransform modeRow;
    private RectTransform qualifyingRow;
    private RectTransform pitRow;
    private RectTransform titleRect;
    private Vector2 titlePosition;
    private float titleBottom;
    private float buttonsTop;
    private float rowHeight;
    private float originalStep;
    private int originalLevels;
    private int partidaLevel = -1;
    private float leftX;
    private float rightX;

    /// <summary>Lo más que puede subir el título para que entren las filas (px).</summary>
    private const float MaxTitleRaise = 22f;

    /// <summary>Lo más que pueden bajar los botones de abajo para que entren las filas (px).</summary>
    private const float MaxButtonsDrop = 18f;

    /// <summary>Lo mínimo entre la última fila y los botones de abajo (px).</summary>
    private const float BottomMargin = 8f;

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

        BuildSessionRows();

        if (modePrevious != null)
            modePrevious.onClick.AddListener(() => ChangeMode(-1));

        if (modeNext != null)
            modeNext.onClick.AddListener(() => ChangeMode(+1));

        if (qualifyingPrevious != null)
            qualifyingPrevious.onClick.AddListener(() => ChangeQualifying(-1));

        if (qualifyingNext != null)
            qualifyingNext.onClick.AddListener(() => ChangeQualifying(+1));

        if (directionPrevious != null)
            directionPrevious.onClick.AddListener(ToggleDirection);

        if (directionNext != null)
            directionNext.onClick.AddListener(ToggleDirection);

        if (pitPrevious != null)
            pitPrevious.onClick.AddListener(TogglePitStop);

        if (pitNext != null)
            pitNext.onClick.AddListener(TogglePitStop);
    }

    // ------------------------------------------------------------------
    // Filas de modo, qualy y sentido
    // ------------------------------------------------------------------

    /// <summary>
    /// Arma Modo (a la izquierda) y Qualy (a la derecha) en un renglón nuevo,
    /// abajo de todo, copiando la fila de Niebla; le pone flechas a Pista para
    /// el sentido, y acomoda las filas para que no se pisen con los botones.
    /// </summary>
    private void BuildSessionRows()
    {
        RectTransform fogRow = RowOf(fogValue);
        RectTransform lapsRow = RowOf(lapsValue);

        if (fogRow == null || lapsRow == null || fogPrevious == null || fogNext == null)
            return;

        Transform parent = fogRow.parent;
        var rows = new List<RectTransform>();

        foreach (Transform child in parent)
        {
            if (child.name.StartsWith("Fila_") && child is RectTransform)
                rows.Add((RectTransform)child);
        }

        // Los renglones que hay (por altura), de arriba para abajo.
        var levels = new List<float>();

        foreach (RectTransform row in rows)
        {
            float y = row.anchoredPosition.y;

            if (!levels.Exists(level => Mathf.Abs(level - y) < 1f))
                levels.Add(y);
        }

        levels.Sort((a, b) => b.CompareTo(a));

        rowHeight = fogRow.rect.height;
        originalLevels = levels.Count;
        originalStep = levels.Count > 1 ? levels[0] - levels[1] : rowHeight + 8f;
        leftX = lapsRow.anchoredPosition.x;
        rightX = fogRow.anchoredPosition.x;

        foreach (RectTransform row in rows)
        {
            layoutRows.Add(row);
            layoutLevels.Add(levels.FindIndex(y => Mathf.Abs(y - row.anchoredPosition.y) < 1f));
        }

        RectTransform visibility = visibilityRow != null ? visibilityRow.transform as RectTransform : null;
        int visibilityIndex = visibility != null ? layoutRows.IndexOf(visibility) : -1;
        partidaLevel = visibilityIndex >= 0 ? layoutLevels[visibilityIndex] : -1;

        // El título (el cartel rojo) y los botones de abajo: lo que se puede mover.
        Transform title = titleText != null ? titleText.transform : null;

        while (title != null && title.parent != parent)
            title = title.parent;

        titleRect = title as RectTransform;

        if (titleRect != null)
        {
            titlePosition = titleRect.anchoredPosition;
            titleBottom = BottomOf(titleRect, parent);
        }
        else
        {
            titleBottom = levels[0] + rowHeight * 0.5f + BottomMargin;
        }

        buttonsTop = ButtonsTop(parent);

        foreach (Button button in new[] { confirmButton, backButton })
        {
            if (button != null && button.transform.parent == parent)
            {
                buttonRects.Add((RectTransform)button.transform);
                buttonPositions.Add(((RectTransform)button.transform).anchoredPosition);
            }
        }

        modeRow = CloneRow(fogRow, "Fila_MODO", "Modo", "Mode", out modePrevious, out modeNext, out modeValue);
        qualifyingRow = CloneRow(fogRow, "Fila_QUALY", "Qualy", "Qualifying", out qualifyingPrevious, out qualifyingNext, out qualifyingValue);
        pitRow = CloneRow(fogRow, "Fila_BOXES", "Boxes", "Pit stop", out pitPrevious, out pitNext, out pitValue);

        SetupTrackRow(parent.Find("Fila_PISTA"));
        LayoutRows();
    }

    /// <summary>La fila (hijo directo del panel) que contiene un texto.</summary>
    private static RectTransform RowOf(TMP_Text value)
    {
        if (value == null)
            return null;

        Transform current = value.transform;

        while (current.parent != null && !current.name.StartsWith("Fila_"))
            current = current.parent;

        return current.name.StartsWith("Fila_") ? current as RectTransform : null;
    }

    /// <summary>Copia de una fila, con su etiqueta, sus flechas (sin acciones) y su valor.</summary>
    private RectTransform CloneRow(RectTransform template, string name, string spanish, string english,
                                   out Button previous, out Button next, out TMP_Text value)
    {
        GameObject clone = Instantiate(template.gameObject, template.parent);
        clone.name = name;

        previous = Same(template, clone.transform, fogPrevious.transform).GetComponent<Button>();
        next = Same(template, clone.transform, fogNext.transform).GetComponent<Button>();
        value = Same(template, clone.transform, fogValue.transform).GetComponent<TMP_Text>();

        // Las acciones de la de Niebla se ponen por código: la copia no las trae,
        // pero por las dudas se limpian.
        previous.onClick = new Button.ButtonClickedEvent();
        next.onClick = new Button.ButtonClickedEvent();

        LocalizedText valueLocalized = value.GetComponent<LocalizedText>();

        if (valueLocalized != null)
            Destroy(valueLocalized);

        foreach (TMP_Text text in clone.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text == value || text.GetComponentInParent<Button>(true) != null)
                continue;

            LocalizedText localized = text.GetComponent<LocalizedText>();

            if (localized != null)
                localized.Set(spanish, english);
            else
                text.text = GameLanguage.Text(spanish, english);

            break;
        }

        return (RectTransform)clone.transform;
    }

    /// <summary>En la copia, el objeto que está en el mismo lugar que target en la fila original.</summary>
    private static Transform Same(Transform originalRow, Transform cloneRow, Transform target)
    {
        var path = new List<int>();

        for (Transform current = target; current != originalRow && current != null; current = current.parent)
            path.Insert(0, current.GetSiblingIndex());

        Transform result = cloneRow;

        foreach (int index in path)
            result = result.GetChild(index);

        return result;
    }

    /// <summary>Pista: se ve habilitada y con flechas (las de Niebla) para el sentido.</summary>
    private void SetupTrackRow(Transform row)
    {
        if (row == null)
            return;

        TMP_Text label = null;

        foreach (TMP_Text text in row.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.name == "Valor")
                trackValue = text;
            else if (label == null)
                label = text;
        }

        if (trackValue == null)
            return;

        // El nombre de la pista, del texto que tenía (y desde ahora lo pone el código).
        LocalizedText localized = trackValue.GetComponent<LocalizedText>();

        if (localized != null)
        {
            if (!string.IsNullOrEmpty(localized.Spanish))
                trackSpanish = localized.Spanish;

            trackEnglish = !string.IsNullOrEmpty(localized.English) ? localized.English : trackSpanish;
            Destroy(localized);
        }
        else if (!string.IsNullOrEmpty(trackValue.text))
        {
            trackSpanish = trackValue.text;
            trackEnglish = trackValue.text;
        }

        trackValue.alpha = 1f;

        if (label != null)
            label.alpha = 1f;

        Image background = row.GetComponent<Image>();

        if (background != null && rowSprite != null)
            background.sprite = rowSprite;

        // Las flechas, en el mismo lugar que en la fila de Niebla.
        Transform control = trackValue.transform.parent;
        directionPrevious = Instantiate(fogPrevious.gameObject, control).GetComponent<Button>();
        directionNext = Instantiate(fogNext.gameObject, control).GetComponent<Button>();
        directionPrevious.name = fogPrevious.name;
        directionNext.name = fogNext.name;
        directionPrevious.onClick = new Button.ButtonClickedEvent();
        directionNext.onClick = new Button.ButtonClickedEvent();
    }

    /// <summary>
    /// Acomoda las filas en renglones de dos columnas, de arriba para abajo:
    /// - Un jugador (5 renglones): Boxes va en el lugar de Partida (que es
    ///   solo para crear servidor). Modo y Qualy, en el renglón de abajo.
    /// - Crear servidor (6 renglones): Boxes va abajo de Modo.
    /// Si no entran entre el título y los botones de abajo, se juntan un poco
    /// y, si hace falta, el título sube y los botones bajan (hasta MaxTitleRaise
    /// y MaxButtonsDrop).
    /// </summary>
    private void LayoutRows()
    {
        if (layoutRows.Count == 0 || modeRow == null)
            return;

        int levelCount = multiplayer ? originalLevels + 2 : originalLevels + 1;
        float available = titleBottom - buttonsTop - 2f * BottomMargin;
        float minStep = rowHeight + 6f;
        float needed = rowHeight + (levelCount - 1) * minStep;
        float titleRaise = 0f;
        float buttonsDrop = 0f;

        if (needed > available)
        {
            float deficit = needed - available;
            titleRaise = Mathf.Min(MaxTitleRaise, deficit * 0.5f);
            buttonsDrop = Mathf.Min(MaxButtonsDrop, deficit - titleRaise);
            titleRaise = Mathf.Min(MaxTitleRaise, deficit - buttonsDrop);
            available += titleRaise + buttonsDrop;
        }

        float step = Mathf.Min(originalStep, (available - rowHeight) / (levelCount - 1));
        float top = titleBottom + titleRaise - BottomMargin;

        if (titleRect != null)
            titleRect.anchoredPosition = titlePosition + new Vector2(0f, titleRaise);

        for (int i = 0; i < buttonRects.Count; i++)
            buttonRects[i].anchoredPosition = buttonPositions[i] - new Vector2(0f, buttonsDrop);

        for (int i = 0; i < layoutRows.Count; i++)
            layoutRows[i].anchoredPosition = new Vector2(layoutRows[i].anchoredPosition.x, LevelY(top, step, layoutLevels[i]));

        modeRow.anchoredPosition = new Vector2(leftX, LevelY(top, step, originalLevels));
        qualifyingRow.anchoredPosition = new Vector2(rightX, LevelY(top, step, originalLevels));

        if (pitRow != null)
        {
            int pitLevel = multiplayer || partidaLevel < 0 ? originalLevels + 1 : partidaLevel;
            pitRow.anchoredPosition = new Vector2(leftX, LevelY(top, step, pitLevel));
        }
    }

    private float LevelY(float top, float step, int level)
    {
        return top - rowHeight * 0.5f - level * step;
    }

    /// <summary>Lo más bajo de un objeto, en el espacio del panel.</summary>
    private static float BottomOf(RectTransform rect, Transform panel)
    {
        float bottom = float.MaxValue;
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);

        foreach (Vector3 corner in corners)
            bottom = Mathf.Min(bottom, panel.InverseTransformPoint(corner).y);

        return bottom;
    }

    /// <summary>Dónde empiezan los botones de abajo (Volver y Confirmar), en el espacio del panel.</summary>
    private float ButtonsTop(Transform panel)
    {
        float top = float.MinValue;
        var corners = new Vector3[4];

        foreach (Button button in new[] { confirmButton, backButton })
        {
            if (button == null)
                continue;

            ((RectTransform)button.transform).GetWorldCorners(corners);

            foreach (Vector3 corner in corners)
                top = Mathf.Max(top, panel.InverseTransformPoint(corner).y);
        }

        return top > float.MinValue ? top : float.MinValue / 2f;
    }

    /// <summary>Carrera suelta, o campeonato de 2 a 6 fechas (en ese orden).</summary>
    private void ChangeMode(int direction)
    {
        int index = RaceSetupStore.Championship ? RaceSetupStore.Rounds - RaceSetupStore.MinRounds + 1 : 0;
        int last = RaceSetupStore.MaxRounds - RaceSetupStore.MinRounds + 1;
        index = Mathf.Clamp(index + direction, 0, last);

        RaceSetupStore.Championship = index > 0;

        if (index > 0)
            RaceSetupStore.Rounds = RaceSetupStore.MinRounds + index - 1;

        Refresh();
    }

    private void ChangeQualifying(int direction)
    {
        int[] options = RaceSetupStore.QualifyingOptions;
        int index = Mathf.Max(0, System.Array.IndexOf(options, RaceSetupStore.QualifyingMinutes));
        RaceSetupStore.QualifyingMinutes = options[Mathf.Clamp(index + direction, 0, options.Length - 1)];
        Refresh();
    }

    private void TogglePitStop()
    {
        RaceSetupStore.MandatoryPitStop = !RaceSetupStore.MandatoryPitStop;
        Refresh();
    }

    private void ToggleDirection()
    {
        RaceSetupStore.Reversed = !RaceSetupStore.Reversed;
        Refresh();
    }

    /// <summary>Muestra la pantalla para un jugador (false) o para crear un servidor (true).</summary>
    public void Open(bool forMultiplayer)
    {
        if (leaderboard != null && leaderboard.IsOpen)
            leaderboard.Close();

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
        if (IsTimeTrial)
        {
            ConfirmTimeTrial();
            return;
        }

        RaceSession.TimeTrial = false;

        // Los bots de esta carrera (los pone el host al cargar la pista).
        RaceSetupStore.BotsForRace = RaceSetupStore.GetBots(multiplayer);

        // Qualy, campeonato y sentido (desde la fecha 1), y boxes.
        RaceSession.MandatoryPitStop = RaceSetupStore.MandatoryPitStop;
        RaceSession.StartNew(RaceSetupStore.Championship ? RaceSetupStore.Rounds : 0,
                             RaceSetupStore.QualifyingMinutes * 60f,
                             RaceSetupStore.Reversed);

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
        // Con el panel de la cuenta abierto, Volver lo cierra.
        if (leaderboard != null && leaderboard.IsOpen && leaderboard.IsAccountOpen)
        {
            leaderboard.CloseAccount();
            return;
        }

        // Desde la tabla se vuelve a la configuración.
        if (leaderboard != null && leaderboard.IsOpen)
        {
            leaderboard.Close();
            Refresh();
            return;
        }

        menuManager.Show(multiplayer ? MenuPanel.Multiplayer : MenuPanel.Main);
    }

    /// <summary>Contrarreloj: el primer ¡A CORRER! abre la tabla; el de la tabla larga.</summary>
    private void ConfirmTimeTrial()
    {
        if (leaderboard == null)
        {
            leaderboard = TimeTrialLeaderboardUI.Create((RectTransform)transform, confirmText != null ? confirmText.font : null);
            leaderboard.ViewRequested += StartViewer;
        }

        // Con el panel de la cuenta abierto no se larga (se está entrando o creando la cuenta).
        if (leaderboard.IsOpen && leaderboard.IsAccountOpen)
            return;

        if (!leaderboard.IsOpen)
        {
            string track = GameLanguage.Text(trackSpanish, trackEnglish) +
                           (RaceSetupStore.Reversed ? GameLanguage.Text(" al revés", " reversed") : "");

            // Seca o mojada: la del clima elegido; al azar o dinámico (o al volver
            // de la repetición), la última que se miró.
            bool? menuWet = TimeTrialRecords.WetForMenuWeather();
            bool wet = !reopenLastTab && menuWet.HasValue ? menuWet.Value : TimeTrialRecords.LastTabWet;
            reopenLastTab = false;

            leaderboard.Open(RaceSetupStore.Reversed, wet, track, sessionMenu.PlayerName);

            if (titleText != null)
                titleText.text = GameLanguage.Text("Contrarreloj", "Time trial");

            return;
        }

        StartTimeTrial();
    }

    private async void StartTimeTrial()
    {
        confirmButton.interactable = false;
        backButton.interactable = false;

        // Baja los fantasmas de los rivales elegidos (sin internet, no hace nada).
        await leaderboard.PrepareRaceAsync();

        if (this == null)
            return;

        backButton.interactable = true;

        RaceSetupStore.BotsForRace = 0;
        RaceSession.MandatoryPitStop = false;
        RaceSession.StartNew(0, 0f, RaceSetupStore.Reversed);
        RaceSession.TimeTrial = true;

        sessionMenu.StartSinglePlayerFromSetup();
    }

    /// <summary>
    /// VER de la tabla: baja esas vueltas y carga la pista solo para verlas
    /// (RaceSession.GhostViewer, la muestra RaceReplay). Sin Catálogo: el kart
    /// propio solo sirve de molde y no se ve.
    /// </summary>
    private async void StartViewer(List<TimeTrialRecords.Entry> entries)
    {
        if (GameSession.Instance == null)
            return;

        confirmButton.interactable = false;
        backButton.interactable = false;

        int count = await leaderboard.PrepareViewerAsync(entries);

        if (this == null)
            return;

        if (count == 0)
        {
            confirmButton.interactable = true;
            backButton.interactable = true;
            return;
        }

        RaceSetupStore.BotsForRace = 0;
        RaceSession.MandatoryPitStop = false;
        RaceSession.StartNew(0, 0f, RaceSetupStore.Reversed);
        RaceSession.TimeTrial = true;
        RaceSession.GhostViewer = true;

        // Con el clima y la hora de la vuelta (la primera, la que sigue la cámara).
        TimeTrialRecords.Entry first = entries[0];
        bool lapWet = TimeTrialRecords.LastTabWet; // las vueltas son de la pestaña que se está mirando
        RaceSession.ViewerWeather = lapWet
            ? (first.Weather == WeatherScenario.Storm ? WeatherScenario.Storm : WeatherScenario.Rain)
            : (first.Weather == WeatherScenario.Cloudy ? WeatherScenario.Cloudy : WeatherScenario.Clear);
        RaceSession.ViewerTimeOfDay = first.TimeOfDay;

        // Que no quede nada de la última contrarreloj.
        TimeTrialRecords.Opponents.Clear();
        TimeTrialRecords.ActiveBoardId = null;

        GameSession.Instance.StartSinglePlayerIn(sessionMenu.PlayerName, GameSession.Instance.RaceScene);
    }

    /// <summary>Abre la tabla de la contrarreloj (al volver de su repetición).</summary>
    public void OpenTimeTrialBoard()
    {
        Open(false);
        reopenLastTab = true;

        if (IsTimeTrial)
            ConfirmTimeTrial();
    }

    // Al volver de la repetición de la tabla, el menú abre la tabla otra vez.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void HookReturnToBoard()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (TimeTrialRecords.ReturnToBoard && mode == LoadSceneMode.Single)
            ReturnToBoardSoon(scene);
    }

    private static async void ReturnToBoardSoon(Scene scene)
    {
        // Después de que el menú muestre su primera pantalla (MenuManager.Start).
        int frame = Time.frameCount;

        while (Time.frameCount < frame + 2)
            await Task.Yield();

        if (!scene.isLoaded || !TimeTrialRecords.ReturnToBoard)
            return;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            RaceSetupMenuUI setup = root.GetComponentInChildren<RaceSetupMenuUI>(true);

            if (setup == null)
                continue;

            TimeTrialRecords.ReturnToBoard = false;
            setup.OpenTimeTrialBoard();
            return;
        }
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

        // La contrarreloj es de una vuelta (lo elegido queda guardado para las carreras).
        int laps = RaceSetupStore.Laps;
        bool timeTrial = IsTimeTrial;
        SetValue(lapsValue, timeTrial ? "1" : laps.ToString());
        SetUsable(lapsMinus, !timeTrial && laps > RaceSetupStore.MinLaps);
        SetUsable(lapsPlus, !timeTrial && laps < RaceSetupStore.MaxLaps);

        SetValue(weatherValue, WeatherChoiceStore.Label(WeatherChoiceStore.Current));
        SetValue(timeValue, WeatherChoiceStore.Label(WeatherChoiceStore.Time));
        SetValue(fogValue, WeatherChoiceStore.Label(WeatherChoiceStore.Fog));

        int bots = RaceSetupStore.GetBots(multiplayer);

        // Un jugador sin bots es contrarreloj: corre contra su fantasma (KartGhost).
        string noBots = multiplayer ? GameLanguage.Text("Sin bots", "No bots") : GameLanguage.Text("Contrarreloj", "Time trial");
        SetValue(botsValue, bots == 0 ? noBots : bots.ToString());

        SetUsable(botsMinus, bots > 0);
        SetUsable(botsPlus, bots < RaceSetupStore.MaxBots);

        // Sin bots, el nivel no importa: se ve apagado.
        if (levelValue != null)
        {
            SetValue(levelValue, LevelLabel(RaceSetupStore.BotDifficulty));
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

        SetValue(visibilityValue, RaceSetupStore.PrivateServer ? GameLanguage.Text("Privada", "Private") : GameLanguage.Text("Pública", "Public"));

        RefreshSessionRows();
        LayoutRows();
    }

    private void RefreshSessionRows()
    {
        bool championship = RaceSetupStore.Championship;
        int rounds = RaceSetupStore.Rounds;

        SetValue(modeValue, championship
            ? GameLanguage.Text($"Campeonato, {rounds} fechas", $"Championship, {rounds} rounds")
            : GameLanguage.Text("Carrera suelta", "Single race"));

        SetUsable(modePrevious, championship);
        SetUsable(modeNext, !championship || rounds < RaceSetupStore.MaxRounds);

        // Contrarreloj: carrera suelta, sin qualy y sin parada obligatoria (fijo).
        bool timeTrial = IsTimeTrial;

        if (timeTrial)
        {
            SetValue(modeValue, GameLanguage.Text("Carrera suelta", "Single race"));
            SetUsable(modePrevious, false);
            SetUsable(modeNext, false);
        }

        SetValue(pitValue, RaceSetupStore.MandatoryPitStop ? GameLanguage.Text("Obligatoria", "Mandatory") : GameLanguage.Text("Libre", "Optional"));

        int minutes = RaceSetupStore.QualifyingMinutes;
        int[] options = RaceSetupStore.QualifyingOptions;
        int index = Mathf.Max(0, System.Array.IndexOf(options, minutes));

        SetValue(qualifyingValue, minutes > 0 && !timeTrial ? minutes + " min" : GameLanguage.Text("Sin qualy", "No qualifying"));
        SetUsable(qualifyingPrevious, !timeTrial && index > 0);
        SetUsable(qualifyingNext, !timeTrial && index < options.Length - 1);

        if (timeTrial)
        {
            SetValue(pitValue, GameLanguage.Text("Libre", "Optional"));
            SetUsable(pitPrevious, false);
            SetUsable(pitNext, false);
        }

        // Pista: el nombre y, si va al revés, dicho (en el campeonato, la primera fecha).
        // Al revés va en un segundo renglón (cortado a mano, así no queda "al" arriba).
        string track = GameLanguage.Text(trackSpanish, trackEnglish);
        SetValue(trackValue, track);

        if (trackValue != null && RaceSetupStore.Reversed)
        {
            trackValue.text = track + "\n" + GameLanguage.Text("al revés", "reversed");
            trackValue.textWrappingMode = TextWrappingModes.NoWrap;
            trackValue.alignment = TextAlignmentOptions.Center;
        }
    }

    /// <summary>
    /// Pone el valor de una fila. Todos los valores tienen el mismo tamaño de
    /// letra; los que no entran en un renglón ("Se larga a llover") van en dos.
    /// </summary>
    private static void SetValue(TMP_Text label, string text)
    {
        if (label == null)
            return;

        label.text = text;
        bool fits = label.GetPreferredValues(text, float.PositiveInfinity, float.PositiveInfinity).x <= label.rectTransform.rect.width;
        label.textWrappingMode = fits ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
        label.alignment = fits ? TextAlignmentOptions.Capline : TextAlignmentOptions.Center;
    }

    /// <summary>Nivel de los bots en el idioma del juego.</summary>
    private static string LevelLabel(RaceDifficulty difficulty)
    {
        switch (difficulty)
        {
            case RaceDifficulty.Easy: return GameLanguage.Text("Fácil", "Easy");
            case RaceDifficulty.Hard: return GameLanguage.Text("Difícil", "Hard");
            default: return GameLanguage.Text("Medio", "Medium");
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
