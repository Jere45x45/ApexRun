using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Pantalla de carga entre escenas. Se arma sola por código (no hay que tocar
/// ninguna escena) y vive mientras dura el juego (DontDestroyOnLoad).
///
/// - Cargas de Netcode (Inicio → Catálogo → Race, Reiniciar, en un jugador y
///   en red): aparece sola, en todas las computadoras, al empezar la carga.
/// - Volver al menú (GameSession.LeaveToMenu): aparece antes de cargar.
/// - Esperas antes de cargar (bajar fantasmas de la contrarreloj o vueltas para
///   VER): la muestra quien espera, con Show.
///
/// Para entrar a la pista (Race desde otra escena) muestra la pista, el modo,
/// el clima y los rivales; en las demás, solo el logo y "Cargando". Se va
/// cuando la escena nueva ya anda fluida. En Race además espera la largada:
/// RaceManager no avisa "listo" hasta que esta pantalla terminó (AllowsRaceStart),
/// así la cuenta regresiva no arranca tapada, y en red muestra "Esperando a
/// los demás jugadores…" mientras faltan otros.
/// </summary>
public class LoadingScreen : MonoBehaviour
{
    public enum Style
    {
        /// <summary>Logo y "Cargando" (menú, taller, reiniciar).</summary>
        Plain,

        /// <summary>Con la pista, el modo, el clima y los rivales.</summary>
        Race
    }

    private const string RaceScene = "Race";

    private const float FadeInTime = 0.15f;
    private const float FadeOutTime = 0.35f;
    private const float MinRaceTime = 1.6f;     // para alcanzar a leer la pista y el clima
    private const float MinPlainTime = 0.3f;
    private const float SettleFallback = 4f;     // en una PC muy lenta, se da por fluida igual
    private const float WaitForLoadTimeout = 20f;
    private const float MaxVisibleTime = 45f;

    private static LoadingScreen instance;

    // Lo que se toma del menú (logo, cartel, fondo y la fuente en negrita).
    private static Sprite logoSprite;
    private static Sprite cartelSprite;
    private static Sprite backgroundSprite;
    private static TMP_FontAsset boldFont;
    private static TMP_FontAsset regularFont;

    private static string trackSpanish = "Circuito 1";
    private static string trackEnglish = "Circuit 1";

    // Interfaz.
    private Canvas canvas;
    private CanvasGroup group;
    private Image photo;
    private Image shade;
    private Image cornerLogo;
    private Image cornerCartel;
    private Image centerLogo;
    private RectTransform infoBlock;
    private TMP_Text modeText;
    private TMP_Text trackText;
    private TMP_Text weatherText;
    private TMP_Text rivalsText;
    private TMP_Text tipText;
    private TMP_Text statusText;
    private RectTransform barTrack;
    private RectTransform barSegment;

    // Estado.
    private bool active;
    private bool hiding;
    private Style style;
    private string pendingScene;     // la escena que se está cargando (null: todavía no empezó)
    private bool arrived;            // ya cargó la escena nueva
    private float shownAt;
    private float arrivedAt;
    private float waitingSince;      // Show sin carga: desde cuándo se espera que empiece
    private int calmFrames;
    private bool settled;
    private string customStatus;
    private RaceManager raceManager;
    private RaceReplay replay;
    private NetworkSceneManager hookedSceneManager;

    private static readonly string[] TipsSpanish =
    {
        "Con lluvia, frená antes y doblá más suave.",
        "Salir al pasto cuesta tiempo: tratá de quedarte en el asfalto.",
        "Pegado atrás de otro kart en la recta, el rebufo te da más velocidad.",
        "En la contrarreloj, la R vuelve a largar al instante.",
        "En la tabla de la contrarreloj, VER muestra la vuelta de cualquier piloto.",
        "Con una cuenta, tu récord te sigue a cualquier computadora."
    };

    private static readonly string[] TipsEnglish =
    {
        "In the rain, brake earlier and steer smoother.",
        "Running onto the grass costs time: try to stay on the tarmac.",
        "Tucked in behind another kart on a straight, the slipstream gives you more speed.",
        "In time trial, R restarts instantly.",
        "On the time trial leaderboard, VIEW shows anyone's lap.",
        "With an account, your record follows you to any computer."
    };

    // ------------------------------------------------------------------
    // API
    // ------------------------------------------------------------------

    /// <summary>True mientras se ve (o se está yendo).</summary>
    public static bool IsShowing => instance != null && instance.active;

    /// <summary>
    /// Si el jugador de esta computadora ya puede avisar que está listo para
    /// largar: sin pantalla de carga, o con la pista ya cargada, fluida y vista
    /// el tiempo mínimo.
    /// </summary>
    public static bool AllowsRaceStart => instance == null || !instance.active || instance.hiding || instance.ReadyForRace;

    /// <summary>El nombre de la pista que se muestra (lo pone la configuración de la partida).</summary>
    public static void SetTrack(string spanish, string english)
    {
        if (!string.IsNullOrEmpty(spanish))
            trackSpanish = spanish;

        trackEnglish = !string.IsNullOrEmpty(english) ? english : trackSpanish;
    }

    /// <summary>
    /// Empieza una carga de escena (la llama GameSession.LoadSceneForAll antes
    /// de cargar; las cargas de Netcode que llegan de otra computadora, o las
    /// que no pasan por ahí, se ven por OnSceneEvent).
    /// </summary>
    public static void BeginLoad(string sceneName)
    {
        bool toRace = sceneName == RaceScene && SceneManager.GetActiveScene().name != RaceScene;
        Ensure().Begin(toRace ? Style.Race : Style.Plain, sceneName, true);
    }

    /// <summary>
    /// La muestra antes de una espera que termina en una carga (bajar fantasmas,
    /// bajar vueltas). Si en 20 s no empieza ninguna carga, se va sola.
    /// </summary>
    public static void Show(Style style, string status = null)
    {
        LoadingScreen screen = Ensure();
        screen.Begin(style, null, false);
        screen.waitingSince = Time.unscaledTime;
        screen.customStatus = status;
        screen.RefreshStatus();
    }

    /// <summary>Cambia el texto de abajo a la derecha (null: el de siempre).</summary>
    public static void SetStatus(string status)
    {
        if (instance == null || !instance.active)
            return;

        instance.customStatus = status;
        instance.RefreshStatus();
    }

    /// <summary>La saca (por ejemplo, si la espera terminó sin cargar nada).</summary>
    public static void Hide()
    {
        if (instance != null && instance.active)
            instance.StartHiding();
    }

    /// <summary>
    /// La muestra y espera a que se vea entera, para cargar después una escena
    /// de golpe (SceneManager.LoadScene congela la pantalla mientras carga).
    /// </summary>
    public static async Task ShowBeforeLoadAsync(string sceneName)
    {
        LoadingScreen screen = Ensure();
        screen.Begin(Style.Plain, sceneName, false);

        float until = Time.unscaledTime + FadeInTime + 0.05f;

        while (screen != null && Time.unscaledTime < until)
            await Task.Yield();

        // Un cuadro más, para que se dibuje con la opacidad completa.
        await Task.Yield();
    }

    // ------------------------------------------------------------------
    // Armado
    // ------------------------------------------------------------------

    private static LoadingScreen Ensure()
    {
        if (instance != null)
            return instance;

        CaptureMenuAssets(SceneManager.GetActiveScene());

        var go = new GameObject("PantallaDeCarga");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<LoadingScreen>();
        instance.Build();
        return instance;
    }

    private void Build()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;
        canvas.enabled = false;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600f, 900f);
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();

        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        RectTransform root = (RectTransform)transform;

        // Fondo: negro (también tapa los clics mientras carga).
        Image black = CodeUI.AddImage(CodeUI.NewRect("Negro", root), new Color(0.03f, 0.03f, 0.04f, 1f));
        CodeUI.Fill(black.rectTransform);
        black.raycastTarget = true;

        // La foto del menú, oscurecida (solo para la pista).
        photo = CodeUI.AddImage(CodeUI.NewRect("Foto", root), new Color(1f, 1f, 1f, 1f));
        CodeUI.Fill(photo.rectTransform);
        photo.raycastTarget = false;

        shade = CodeUI.AddImage(CodeUI.NewRect("Sombra", root), new Color(0f, 0f, 0f, 0.62f));
        CodeUI.Fill(shade.rectTransform);
        shade.raycastTarget = false;

        // Franja de abajo, más oscura, donde va la información.
        Image band = CodeUI.AddImage(CodeUI.NewRect("Franja", shade.rectTransform), new Color(0f, 0f, 0f, 0.45f));
        Place(band.rectTransform, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 330f));
        band.rectTransform.anchorMin = new Vector2(0f, 0f);
        band.rectTransform.anchorMax = new Vector2(1f, 0f);
        band.raycastTarget = false;

        // Logo y cartel arriba a la izquierda (pista) o logo en el medio (lo demás).
        cornerLogo = CodeUI.AddImage(CodeUI.NewRect("Logo", root), Color.white);
        Place(cornerLogo.rectTransform, new Vector2(0f, 1f), new Vector2(48f, -40f), new Vector2(100f, 100f));
        cornerLogo.preserveAspect = true;
        cornerLogo.raycastTarget = false;

        cornerCartel = CodeUI.AddImage(CodeUI.NewRect("Cartel", root), Color.white);
        Place(cornerCartel.rectTransform, new Vector2(0f, 1f), new Vector2(162f, -48f), new Vector2(560f, 84f));
        cornerCartel.preserveAspect = true;
        cornerCartel.raycastTarget = false;

        centerLogo = CodeUI.AddImage(CodeUI.NewRect("LogoCentro", root), Color.white);
        Place(centerLogo.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), new Vector2(150f, 150f));
        centerLogo.preserveAspect = true;
        centerLogo.raycastTarget = false;

        // La información de la carrera, abajo a la izquierda.
        infoBlock = CodeUI.NewRect("Info", root);
        Place(infoBlock, new Vector2(0f, 0f), new Vector2(64f, 56f), new Vector2(1000f, 280f));

        tipText = NewLine("Consejo", infoBlock, 23f, regularFont, 0f, new Color(0.78f, 0.79f, 0.83f, 1f));
        tipText.fontStyle = FontStyles.Italic;
        tipText.textWrappingMode = TextWrappingModes.Normal;
        tipText.rectTransform.sizeDelta = new Vector2(900f, 34f);

        rivalsText = NewLine("Rivales", infoBlock, 27f, regularFont, 50f, Color.white);
        rivalsText.richText = true;
        rivalsText.overflowMode = TextOverflowModes.Ellipsis;

        weatherText = NewLine("Clima", infoBlock, 29f, regularFont, 88f, new Color(0.9f, 0.91f, 0.94f, 1f));

        trackText = NewLine("Pista", infoBlock, 76f, boldFont, 128f, Color.white);
        trackText.rectTransform.sizeDelta = new Vector2(1000f, 90f);

        modeText = NewLine("Modo", infoBlock, 27f, boldFont, 222f, CodeUI.AccentRed);
        modeText.characterSpacing = 4f;

        // Abajo a la derecha: qué está pasando y una barra que se mueve.
        statusText = CodeUI.NewText("Estado", root, 26f, TextAlignmentOptions.Right, regularFont);
        Place(statusText.rectTransform, new Vector2(1f, 0f), new Vector2(-64f, 80f), new Vector2(700f, 36f));
        statusText.color = new Color(0.9f, 0.91f, 0.94f, 1f);

        barTrack = CodeUI.NewRect("Barra", root);
        Place(barTrack, new Vector2(1f, 0f), new Vector2(-64f, 62f), new Vector2(320f, 6f));
        CodeUI.AddImage(barTrack, new Color(1f, 1f, 1f, 0.16f)).raycastTarget = false;
        barTrack.gameObject.AddComponent<RectMask2D>();

        barSegment = CodeUI.NewRect("Avance", barTrack);
        barSegment.anchorMin = new Vector2(0f, 0f);
        barSegment.anchorMax = new Vector2(0f, 1f);
        barSegment.pivot = new Vector2(0f, 0.5f);
        barSegment.sizeDelta = new Vector2(96f, 0f);
        CodeUI.AddImage(barSegment, CodeUI.AccentRed).raycastTarget = false;
    }

    private static TMP_Text NewLine(string name, RectTransform parent, float size, TMP_FontAsset font, float y, Color color)
    {
        TMP_Text text = CodeUI.NewText(name, parent, size, TextAlignmentOptions.BottomLeft, font);
        Place(text.rectTransform, new Vector2(0f, 0f), new Vector2(0f, y), new Vector2(1000f, size * 1.3f));
        text.color = color;
        return text;
    }

    private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    /// <summary>Toma del menú el logo, el cartel, el fondo y las fuentes (si la escena es la del menú).</summary>
    private static void CaptureMenuAssets(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Image image in root.GetComponentsInChildren<Image>(true))
            {
                if (image.sprite == null)
                    continue;

                string name = image.sprite.name;

                if (logoSprite == null && name == "ApexRun_Logo")
                    logoSprite = image.sprite;
                else if (cartelSprite == null && name == "ApexRun_Cartel")
                    cartelSprite = image.sprite;
                else if (backgroundSprite == null && name == "Fondo1")
                    backgroundSprite = image.sprite;
            }

            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.font == null)
                    continue;

                string name = text.font.name;

                if (boldFont == null && name.Contains("Bold"))
                    boldFont = text.font;
                else if (regularFont == null && name.Contains("Regular"))
                    regularFont = text.font;
            }
        }

        if (instance != null)
            instance.ApplyAssets();
    }

    private void ApplyAssets()
    {
        photo.sprite = backgroundSprite;
        cornerLogo.sprite = logoSprite;
        cornerCartel.sprite = cartelSprite;
        centerLogo.sprite = logoSprite;

        // Sin la fuente en negrita del menú (por ejemplo, Play desde Race), negrita simulada.
        trackText.fontStyle = boldFont != null ? FontStyles.Normal : FontStyles.Bold;
        modeText.fontStyle = boldFont != null ? FontStyles.Normal : FontStyles.Bold;

        if (boldFont != null)
        {
            trackText.font = boldFont;
            modeText.font = boldFont;
        }

        if (regularFont != null)
        {
            weatherText.font = regularFont;
            rivalsText.font = regularFont;
            tipText.font = regularFont;
            statusText.font = regularFont;
        }
    }

    // ------------------------------------------------------------------
    // Mostrar y sacar
    // ------------------------------------------------------------------

    private void Begin(Style newStyle, string sceneName, bool instant)
    {
        bool wasActive = active && !hiding;

        style = newStyle;
        pendingScene = sceneName;
        arrived = false;
        settled = false;
        calmFrames = 0;
        hiding = false;
        raceManager = null;
        replay = null;
        waitingSince = -1f;
        customStatus = null;

        if (!wasActive)
        {
            shownAt = Time.unscaledTime;
            tipText.text = PickTip();
        }

        active = true;
        canvas.enabled = true;
        group.blocksRaycasts = true;

        if (instant)
            group.alpha = 1f;

        ApplyAssets();
        Fill();
    }

    private void StartHiding()
    {
        hiding = true;
        group.blocksRaycasts = false;
    }

    private void Finish()
    {
        active = false;
        hiding = false;
        pendingScene = null;
        customStatus = null;
        group.alpha = 0f;
        canvas.enabled = false;
    }

    /// <summary>Lo que dice la pantalla (la pista, el modo, el clima y los rivales).</summary>
    private void Fill()
    {
        bool race = style == Style.Race;

        photo.enabled = race && backgroundSprite != null;
        shade.gameObject.SetActive(race);
        cornerLogo.enabled = race && logoSprite != null;
        cornerCartel.enabled = race && cartelSprite != null;
        centerLogo.enabled = !race && logoSprite != null;
        infoBlock.gameObject.SetActive(race);

        if (race)
        {
            trackText.text = (GameLanguage.Text(trackSpanish, trackEnglish)
                              + (RaceSession.Reversed ? GameLanguage.Text(" · al revés", " · reversed") : "")).ToUpperInvariant();
            modeText.text = DescribeMode();
            weatherText.text = DescribeWeather();
            rivalsText.text = DescribeRivals();
        }

        RefreshStatus();
    }

    private void RefreshStatus()
    {
        if (statusText == null)
            return;

        if (!string.IsNullOrEmpty(customStatus))
            statusText.text = customStatus;
        else if (style == Style.Race && RaceSession.GhostViewer)
            statusText.text = GameLanguage.Text("Preparando la repetición…", "Preparing the replay…");
        else
            statusText.text = GameLanguage.Text("Cargando…", "Loading…");
    }

    private static string DescribeMode()
    {
        if (RaceSession.GhostViewer)
            return GameLanguage.Text("REPETICIÓN DE LA TABLA", "LEADERBOARD REPLAY");

        if (RaceSession.TimeTrial)
            return GameLanguage.Text("CONTRARRELOJ  ·  1 VUELTA", "TIME TRIAL  ·  1 LAP");

        GameSession session = GameSession.Instance;

        // Un invitado de una partida en red: lo que publicó el anfitrión.
        if (session != null && session.Session != null && !session.IsHost)
        {
            string laps = SessionValue(GameSession.LapsProperty);
            return GameLanguage.Text("PARTIDA EN RED", "ONLINE RACE")
                   + (string.IsNullOrEmpty(laps) ? "" : "  ·  " + laps + GameLanguage.Text(" VUELTAS", " LAPS"));
        }

        var text = new StringBuilder(RaceSession.Title());

        if (RaceSession.IsQualifying)
        {
            text.Append("  ·  ").Append(Mathf.RoundToInt(RaceSession.QualifyingSeconds / 60f)).Append(" MIN");
        }
        else
        {
            int laps = RaceSetupStore.Laps;
            text.Append("  ·  ").Append(laps).Append(laps == 1 ? GameLanguage.Text(" VUELTA", " LAP") : GameLanguage.Text(" VUELTAS", " LAPS"));
        }

        int bots = RaceSetupStore.RaceBots;

        if (bots > 0)
            text.Append("  ·  ").Append(bots).Append(bots == 1 ? " BOT" : " BOTS");

        return text.ToString();
    }

    private static string DescribeWeather()
    {
        if (RaceSession.GhostViewer)
            return Capitalize(WeatherChoiceStore.Label(RaceSession.ViewerWeather)) + "  ·  " + WeatherChoiceStore.Label(RaceSession.ViewerTimeOfDay);

        GameSession session = GameSession.Instance;

        if (session != null && session.Session != null && !session.IsHost)
            return SessionValue(GameSession.WeatherProperty) ?? "";

        return WeatherChoiceStore.Summary();
    }

    private static string DescribeRivals()
    {
        List<TimeTrialRecords.Opponent> list = RaceSession.GhostViewer ? TimeTrialRecords.ViewerGhosts
                                             : RaceSession.TimeTrial ? TimeTrialRecords.Opponents : null;

        if (list == null)
            return "";

        var names = new StringBuilder();

        foreach (TimeTrialRecords.Opponent opponent in list)
        {
            if (opponent == null || opponent.Entry == null)
                continue;

            if (names.Length > 0)
                names.Append(",  ");

            // El nombre ya viene sin < ni > (CleanName), pero por las dudas.
            string name = (opponent.Entry.Name ?? "").Replace("<", "").Replace(">", "");
            names.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(opponent.Color)).Append('>').Append(name).Append("</color>");
        }

        if (RaceSession.GhostViewer)
            return names.Length > 0 ? GameLanguage.Text("Vueltas de ", "Laps by ") + names : "";

        return names.Length > 0
            ? GameLanguage.Text("Contra tu fantasma y ", "Against your ghost and ") + names
            : GameLanguage.Text("Contra tu fantasma", "Against your ghost");
    }

    private static string SessionValue(string key)
    {
        var session = GameSession.Instance != null ? GameSession.Instance.Session : null;

        if (session == null || session.Properties == null || !session.Properties.TryGetValue(key, out var property) || property == null)
            return null;

        return property.Value;
    }

    private static string Capitalize(string text) =>
        string.IsNullOrEmpty(text) ? text : char.ToUpper(text[0]) + text.Substring(1);

    private static string PickTip()
    {
        int index = Random.Range(0, TipsSpanish.Length);
        return GameLanguage.Text(TipsSpanish[index], TipsEnglish[index]);
    }

    // ------------------------------------------------------------------
    // Cuándo se va
    // ------------------------------------------------------------------

    // En VER, la repetición arranca sola: no se la tapa más de lo necesario.
    private bool ReadyForRace =>
        arrived && settled && Time.unscaledTime - shownAt >= (style == Style.Race && !RaceSession.GhostViewer ? MinRaceTime : MinPlainTime);

    private void Update()
    {
        HookNetcode();

        // La barra: un tramo rojo que cruza una y otra vez.
        if (active && barSegment != null)
        {
            float width = barTrack.rect.width;
            float segment = barSegment.sizeDelta.x;
            float t = Mathf.Repeat(Time.unscaledTime * 0.7f, 1f);
            barSegment.anchoredPosition = new Vector2(Mathf.Lerp(-segment, width, t), 0f);
        }

        if (!active)
            return;

        // Aparecer y desaparecer.
        if (hiding)
        {
            group.alpha = Mathf.MoveTowards(group.alpha, 0f, Time.unscaledDeltaTime / FadeOutTime);

            if (group.alpha <= 0f)
                Finish();

            return;
        }

        group.alpha = Mathf.MoveTowards(group.alpha, 1f, Time.unscaledDeltaTime / FadeInTime);

        float now = Time.unscaledTime;

        // Por si algo sale mal: nunca queda tapando el juego.
        if (now - shownAt > MaxVisibleTime)
        {
            Debug.LogWarning("LoadingScreen: se sacó por tiempo (la escena no terminó de prepararse).");
            StartHiding();
            return;
        }

        // Se mostró para una espera y nunca empezó una carga.
        if (pendingScene == null && !arrived && waitingSince >= 0f && now - waitingSince > WaitForLoadTimeout)
        {
            StartHiding();
            return;
        }

        if (!arrived)
            return;

        // Fluida: tres cuadros seguidos de menos de 0,1 s (o, en una PC muy lenta, al rato).
        if (!settled)
        {
            calmFrames = Time.unscaledDeltaTime < 0.1f ? calmFrames + 1 : 0;

            if ((calmFrames >= 3 && now - arrivedAt > 0.2f) || now - arrivedAt > SettleFallback)
                settled = true;
            else
                return;
        }

        if (!ReadyForRace)
            return;

        if (SceneManager.GetActiveScene().name != RaceScene)
        {
            StartHiding();
            return;
        }

        // En la pista: se va con la largada (o cuando arranca la repetición de la tabla).
        if (RaceSession.GhostViewer)
        {
            if (replay == null)
                replay = FindFirstObjectByType<RaceReplay>();

            if (replay == null || replay.IsOpen)
                StartHiding();

            return;
        }

        if (raceManager == null)
            raceManager = FindFirstObjectByType<RaceManager>();

        if (raceManager == null || !raceManager.IsWaiting())
        {
            StartHiding();
            return;
        }

        // Listo acá, pero faltan otros jugadores.
        if (string.IsNullOrEmpty(customStatus) && now - arrivedAt > 1f && IsNetworkGame())
        {
            customStatus = GameLanguage.Text("Esperando a los demás jugadores…", "Waiting for the other players…");
            RefreshStatus();
        }
    }

    private static bool IsNetworkGame()
    {
        GameSession session = GameSession.Instance;
        return session != null && session.Session != null;
    }

    // ------------------------------------------------------------------
    // Escenas
    // ------------------------------------------------------------------

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;

        if (hookedSceneManager != null)
            hookedSceneManager.OnSceneEvent -= HandleSceneEvent;

        hookedSceneManager = null;
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    /// <summary>Se engancha a las cargas de Netcode (el NetworkManager se crea y se destruye en cada partida).</summary>
    private void HookNetcode()
    {
        NetworkManager manager = NetworkManager.Singleton;
        NetworkSceneManager sceneManager = manager != null && manager.IsListening ? manager.SceneManager : null;

        if (sceneManager == hookedSceneManager)
            return;

        if (hookedSceneManager != null)
            hookedSceneManager.OnSceneEvent -= HandleSceneEvent;

        hookedSceneManager = sceneManager;

        if (hookedSceneManager != null)
            hookedSceneManager.OnSceneEvent += HandleSceneEvent;
    }

    private void HandleSceneEvent(SceneEvent sceneEvent)
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (manager == null || sceneEvent.SceneEventType != SceneEventType.Load || sceneEvent.ClientId != manager.LocalClientId)
            return;

        // Ya avisada por BeginLoad (el anfitrión, desde GameSession).
        if (active && !hiding && pendingScene == sceneEvent.SceneName && !arrived)
            return;

        // Entrar a la pista desde otra escena: con la información. Lo demás (y reiniciar), simple.
        BeginLoad(sceneEvent.SceneName);
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!active || hiding)
            return;

        if (pendingScene != null && scene.name != pendingScene)
            return;

        // Una espera que terminó en una carga que no avisó antes (sin red): vale igual.
        if (pendingScene == null && mode != LoadSceneMode.Single)
            return;

        arrived = true;
        arrivedAt = Time.unscaledTime;
        calmFrames = 0;
    }

    // ------------------------------------------------------------------
    // Armado automático
    // ------------------------------------------------------------------

    private static bool hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void HookScenes()
    {
        if (!hooked)
        {
            SceneManager.sceneLoaded += (scene, mode) => CaptureMenuAssets(scene);
            hooked = true;
        }

        CaptureMenuAssets(SceneManager.GetActiveScene());

        // Se crea desde el arranque, así escucha las cargas de Netcode desde la primera.
        Ensure();
    }
}
