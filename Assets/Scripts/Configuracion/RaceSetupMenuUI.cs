using System.Collections.Generic;
using TMPro;
using UnityEngine;
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
/// Al confirmar se arma RaceSession (qualy, campeonato y sentido).
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

    /// <summary>Lo más que se suben las filas para que entren las nuevas (px).</summary>
    private const float MaxRowRaise = 16f;

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

        RectTransform modeRow = CloneRow(fogRow, "Fila_MODO", "Modo", "Mode", out modePrevious, out modeNext, out modeValue);
        RectTransform qualifyingRow = CloneRow(fogRow, "Fila_QUALY", "Qualy", "Qualifying", out qualifyingPrevious, out qualifyingNext, out qualifyingValue);

        SetupTrackRow(parent.Find("Fila_PISTA"));
        PlaceRows(rows, lapsRow.anchoredPosition.x, fogRow.anchoredPosition.x, modeRow, qualifyingRow);
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
    /// El renglón nuevo va abajo de todo. Si no entra arriba de los botones,
    /// las filas se suben un poco (hasta MaxRowRaise) y después se juntan.
    /// </summary>
    private void PlaceRows(List<RectTransform> rows, float leftX, float rightX, RectTransform newLeft, RectTransform newRight)
    {
        if (rows.Count == 0)
            return;

        float rowHeight = rows[0].rect.height;

        // Los renglones que hay (por altura), de arriba para abajo.
        var levels = new List<float>();

        foreach (RectTransform row in rows)
        {
            float y = row.anchoredPosition.y;

            if (!levels.Exists(level => Mathf.Abs(level - y) < 1f))
                levels.Add(y);
        }

        levels.Sort((a, b) => b.CompareTo(a));

        float step = levels.Count > 1 ? levels[0] - levels[1] : rowHeight + 8f;
        int count = levels.Count + 1;
        float top = levels[0] + rowHeight * 0.5f;
        float bottomLimit = ButtonsTop(newLeft.parent) + BottomMargin;

        float bottom = top - rowHeight - (count - 1) * step;

        if (bottom < bottomLimit)
        {
            top += Mathf.Min(MaxRowRaise, bottomLimit - bottom);
            step = Mathf.Min(step, (top - rowHeight - bottomLimit) / (count - 1));
            step = Mathf.Max(step, rowHeight + 2f);
        }

        foreach (RectTransform row in rows)
        {
            int level = levels.FindIndex(y => Mathf.Abs(y - row.anchoredPosition.y) < 1f);
            row.anchoredPosition = new Vector2(row.anchoredPosition.x, top - rowHeight * 0.5f - level * step);
        }

        float newY = top - rowHeight * 0.5f - (count - 1) * step;
        newLeft.anchoredPosition = new Vector2(leftX, newY);
        newRight.anchoredPosition = new Vector2(rightX, newY);
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

    private void ToggleDirection()
    {
        RaceSetupStore.Reversed = !RaceSetupStore.Reversed;
        Refresh();
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

        // Qualy, campeonato y sentido (desde la fecha 1).
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
        SetValue(lapsValue, laps.ToString());
        SetUsable(lapsMinus, laps > RaceSetupStore.MinLaps);
        SetUsable(lapsPlus, laps < RaceSetupStore.MaxLaps);

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

        int minutes = RaceSetupStore.QualifyingMinutes;
        int[] options = RaceSetupStore.QualifyingOptions;
        int index = Mathf.Max(0, System.Array.IndexOf(options, minutes));

        SetValue(qualifyingValue, minutes > 0 ? minutes + " min" : GameLanguage.Text("Sin qualy", "No qualifying"));
        SetUsable(qualifyingPrevious, index > 0);
        SetUsable(qualifyingNext, index < options.Length - 1);

        // Pista: el nombre y, si va al revés, dicho (en el campeonato, la primera fecha).
        string track = GameLanguage.Text(trackSpanish, trackEnglish);
        SetValue(trackValue, RaceSetupStore.Reversed ? track + GameLanguage.Text(" al revés", " reversed") : track);
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
