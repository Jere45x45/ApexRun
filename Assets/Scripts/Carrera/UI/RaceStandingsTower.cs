using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Tabla de posiciones en carrera, como la torre de la F1: a la izquierda,
/// debajo de la posición y la vuelta.
/// - Arriba, la vuelta del líder (VUELTA 2/3).
/// - Una fila por kart, en orden: posición, nombre y diferencia con el líder
///   ("Líder", "+1.254", "+1 V" si le sacaron una vuelta). La fila propia va
///   resaltada.
/// - La diferencia se mide en los checkpoints: cuánto después que el líder pasó
///   cada kart por el último checkpoint que cruzó (como los parciales de la F1).
///   Cuando los dos terminaron, es la diferencia de tiempo final.
/// - La tecla T la muestra u oculta; la elección queda guardada (PlayerPrefs).
/// Todo sale de RaceTimingManager, así que anda igual en el host y en los
/// clientes. Se arma sola al cargar una escena con RaceHUD (no hace falta tocar
/// la escena) y se oculta junto con el HUD en los resultados.
/// </summary>
public class RaceStandingsTower : MonoBehaviour
{
    private const string PrefsKey = "ApexRun.TablaPosiciones";

    [Tooltip("Tecla para mostrar u ocultar la tabla.")]
    [SerializeField] private Key toggleKey = Key.T;

    [SerializeField, Min(8f)] private float rowHeight = 17f;
    [SerializeField, Min(60f)] private float width = 150f;
    [SerializeField, Min(0.05f)] private float refreshInterval = 0.2f;

    [SerializeField] private Color rowColor = new Color(0f, 0f, 0f, 0.45f);
    [SerializeField] private Color headerColor = new Color(0f, 0f, 0f, 0.65f);
    [SerializeField] private Color playerRowColor = new Color(0.63f, 0f, 0f, 0.85f);
    [SerializeField] private Color gapColor = new Color(1f, 1f, 1f, 0.75f);

    private static readonly FieldInfo HudKartField =
        typeof(RaceHUD).GetField("playerKart", BindingFlags.Instance | BindingFlags.NonPublic);

    private class Row
    {
        public GameObject Root;
        public Image Background;
        public TextMeshProUGUI Position;
        public TextMeshProUGUI Name;
        public TextMeshProUGUI Gap;
    }

    private readonly List<Row> rows = new List<Row>();

    // Momento (reloj de la carrera) en que cada kart llegó a cada avance:
    // avance = (vuelta - 1) * checkpoints + último checkpoint + 1.
    private readonly Dictionary<Rigidbody, Dictionary<int, double>> passTimes = new Dictionary<Rigidbody, Dictionary<int, double>>();
    private readonly Dictionary<Rigidbody, int> progress = new Dictionary<Rigidbody, int>();

    private RaceHUD hud;
    private RaceManager raceManager;
    private RaceTimingManager timing;
    private RaceCheckpointManager checkpoints;
    private TMP_FontAsset font;
    private TMP_FontAsset boldFont;
    private Sprite panelSprite;
    private Row header;
    private RectTransform rect;
    private bool visible = true;
    private float nextRefresh;

    public bool Visible
    {
        get => visible;
        set
        {
            visible = value;
            PlayerPrefs.SetInt(PrefsKey, value ? 1 : 0);
            PlayerPrefs.Save();
            ApplyVisible();
        }
    }

    private void Awake()
    {
        rect = (RectTransform)transform;
        visible = PlayerPrefs.GetInt(PrefsKey, 1) == 1;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
            Visible = !visible;

        if (raceManager == null)
            raceManager = FindFirstObjectByType<RaceManager>();

        if (timing == null)
            timing = FindFirstObjectByType<RaceTimingManager>();

        if (checkpoints == null)
            checkpoints = FindFirstObjectByType<RaceCheckpointManager>();

        if (raceManager == null || timing == null)
            return;

        TrackProgress();

        if (Time.unscaledTime < nextRefresh)
            return;

        nextRefresh = Time.unscaledTime + refreshInterval;
        Refresh();
    }

    private int CheckpointCount => checkpoints != null ? Mathf.Max(1, checkpoints.CheckpointCount) : 1;

    /// <summary>Anota cuándo llega cada kart a cada checkpoint (en todas las computadoras).</summary>
    private void TrackProgress()
    {
        if (!timing.HasStarted)
            return;

        double now = raceManager.Now;
        int count = CheckpointCount;

        foreach (KartBehaviour kart in raceManager.RaceKarts)
        {
            Rigidbody body = kart != null ? kart.GetComponent<Rigidbody>() : null;

            if (body == null || timing.IsFinished(body) && progress.ContainsKey(body))
                continue;

            int key = (timing.GetLap(body) - 1) * count + timing.GetLastCheckpoint(body) + 1;

            if (!progress.TryGetValue(body, out int last))
                last = 0;

            if (key <= last)
                continue;

            if (!passTimes.TryGetValue(body, out Dictionary<int, double> times))
            {
                times = new Dictionary<int, double>();
                passTimes[body] = times;
            }

            for (int k = last + 1; k <= key; k++)
                times[k] = now;

            progress[body] = key;
        }
    }

    private void Refresh()
    {
        IReadOnlyList<RaceTimingManager.Result> results = timing.GetClassification();
        Rigidbody own = hud != null && HudKartField != null ? HudKartField.GetValue(hud) as Rigidbody : null;

        if (header == null)
            header = CreateRow("Encabezado", -1);

        int leaderLap = results.Count > 0 ? Mathf.Min(results[0].Lap, timing.TotalLaps) : 1;
        header.Name.text = GameLanguage.Text("VUELTA ", "LAP ") + leaderLap + "/" + timing.TotalLaps;

        string previousGap = null;
        float previousSeconds = 0f;

        for (int i = 0; i < results.Count; i++)
        {
            RaceTimingManager.Result result = results[i];
            Row row = i < rows.Count ? rows[i] : CreateRow("Fila" + (i + 1), i);

            if (i >= rows.Count)
                rows.Add(row);

            bool isPlayer = result.Kart == own;

            row.Position.text = result.Position.ToString(CultureInfo.InvariantCulture);
            row.Name.text = DisplayName(result.Kart);
            row.Gap.text = i == 0 ? GameLanguage.Text("Líder", "Leader") : FormatGap(results[0], result, ref previousSeconds, ref previousGap);
            row.Background.color = isPlayer ? playerRowColor : rowColor;
            row.Gap.color = isPlayer ? Color.white : gapColor;
            row.Root.SetActive(visible);
        }

        for (int i = results.Count; i < rows.Count; i++)
            rows[i].Root.SetActive(false);

        header.Root.SetActive(visible);
    }

    /// <summary>
    /// Diferencia con el líder. Nunca es menor que la del de adelante: entre dos
    /// checkpoints un kart puede pasar a otro antes de que se actualice su tiempo.
    /// </summary>
    private string FormatGap(RaceTimingManager.Result leader, RaceTimingManager.Result kart, ref float previousSeconds, ref string previousGap)
    {
        string gap = RawGap(leader, kart, out float seconds, out bool isTime);

        if (isTime)
        {
            seconds = Mathf.Max(seconds, previousSeconds);
            previousSeconds = seconds;
            gap = FormatSeconds(seconds);
        }

        previousGap = gap;
        return gap;
    }

    private string RawGap(RaceTimingManager.Result leader, RaceTimingManager.Result kart, out float seconds, out bool isTime)
    {
        seconds = 0f;
        isTime = false;

        if (!timing.HasStarted)
            return "-";

        if (leader.Finished && kart.Finished)
        {
            seconds = kart.FinalTime - leader.FinalTime;
            isTime = true;
            return FormatSeconds(seconds);
        }

        if (!progress.TryGetValue(kart.Kart, out int kartKey) || !progress.TryGetValue(leader.Kart, out int leaderKey))
            return "-";

        int behind = leaderKey - kartKey;

        if (behind >= CheckpointCount)
        {
            int laps = behind / CheckpointCount;
            return "+" + laps + GameLanguage.Text(" V", " L");
        }

        if (kartKey <= 0 ||
            !passTimes.TryGetValue(kart.Kart, out Dictionary<int, double> kartTimes) ||
            !passTimes.TryGetValue(leader.Kart, out Dictionary<int, double> leaderTimes) ||
            !kartTimes.TryGetValue(kartKey, out double kartTime) ||
            !leaderTimes.TryGetValue(kartKey, out double leaderTime))
        {
            return "-";
        }

        seconds = (float)(kartTime - leaderTime);
        isTime = true;
        return FormatSeconds(seconds);
    }

    private static string FormatSeconds(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);

        if (seconds < 60f)
            return "+" + seconds.ToString("0.000", CultureInfo.InvariantCulture);

        int minutes = Mathf.FloorToInt(seconds / 60f);
        return "+" + minutes + ":" + (seconds - minutes * 60f).ToString("00.0", CultureInfo.InvariantCulture);
    }

    private void ApplyVisible()
    {
        if (!visible)
        {
            if (header != null)
                header.Root.SetActive(false);

            foreach (Row row in rows)
                row.Root.SetActive(false);
        }

        // Al mostrarla, la próxima actualización prende las filas que hacen falta.
        nextRefresh = 0f;
    }

    private Row CreateRow(string name, int index)
    {
        var row = new Row();

        row.Root = new GameObject(name, typeof(RectTransform), typeof(Image));
        row.Root.layer = gameObject.layer;

        var rowRect = (RectTransform)row.Root.transform;
        rowRect.SetParent(rect, false);
        rowRect.anchorMin = rowRect.anchorMax = new Vector2(0f, 1f);
        rowRect.pivot = new Vector2(0f, 1f);
        rowRect.sizeDelta = new Vector2(width, rowHeight);
        rowRect.anchoredPosition = new Vector2(0f, -(index + 1) * (rowHeight + 1f));

        row.Background = row.Root.GetComponent<Image>();
        row.Background.raycastTarget = false;
        row.Background.color = index < 0 ? headerColor : rowColor;

        if (panelSprite != null)
        {
            row.Background.sprite = panelSprite;
            row.Background.type = Image.Type.Sliced;
        }

        if (index < 0)
        {
            row.Name = CreateText(rowRect, "Vuelta", 6f, width - 6f, TextAlignmentOptions.Left, boldFont);
            return row;
        }

        row.Position = CreateText(rowRect, "Posicion", 2f, 20f, TextAlignmentOptions.Right, boldFont);
        row.Name = CreateText(rowRect, "Nombre", 30f, 70f, TextAlignmentOptions.Left, font);
        row.Gap = CreateText(rowRect, "Diferencia", width - 50f, 46f, TextAlignmentOptions.Right, font);

        return row;
    }

    private TextMeshProUGUI CreateText(RectTransform parent, string name, float x, float textWidth, TextAlignmentOptions alignment, TMP_FontAsset fontAsset)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.layer = gameObject.layer;

        var textRect = (RectTransform)go.transform;
        textRect.SetParent(parent, false);
        textRect.anchorMin = new Vector2(0f, 0f);
        textRect.anchorMax = new Vector2(0f, 1f);
        textRect.pivot = new Vector2(0f, 0.5f);
        textRect.anchoredPosition = new Vector2(x, 0f);
        textRect.sizeDelta = new Vector2(textWidth, 0f);

        var text = go.GetComponent<TextMeshProUGUI>();

        if (fontAsset != null)
            text.font = fontAsset;

        text.fontSize = rowHeight * 0.68f;
        text.color = Color.white;
        text.alignment = alignment == TextAlignmentOptions.Right ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;

        return text;
    }

    // ------------------------------------------------------------------
    // Ayudas compartidas (también las usa KartNameTags).
    // ------------------------------------------------------------------

    /// <summary>Nombre corto de un kart: el del jugador en red, o el del objeto (los bots).</summary>
    public static string DisplayName(Rigidbody kart)
    {
        if (kart == null)
            return string.Empty;

        NetworkKart networkKart = kart.GetComponent<NetworkKart>();
        string name = networkKart != null ? networkKart.DisplayName : null;

        return string.IsNullOrEmpty(name) ? kart.name.ToUpperInvariant() : name;
    }

    /// <summary>Fuente del HUD (la bold si hay y se pide).</summary>
    public static TMP_FontAsset FindHudFont(RaceHUD raceHud, bool bold)
    {
        TMP_FontAsset fallback = null;

        foreach (TMP_Text text in raceHud.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.font == null)
                continue;

            if (fallback == null)
                fallback = text.font;

            if (text.font.name.Contains("Bold") == bold)
                return text.font;
        }

        return fallback;
    }

    // ------------------------------------------------------------------
    // Armado automático en el HUD (así no hace falta tocar la escena).
    // ------------------------------------------------------------------

    private static bool installerHooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void HookInstaller()
    {
        if (!installerHooked)
        {
            SceneManager.sceneLoaded += (scene, mode) => InstallIn(scene);
            installerHooked = true;
        }

        InstallIn(SceneManager.GetActiveScene());
    }

    private static void InstallIn(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (RaceHUD raceHud in root.GetComponentsInChildren<RaceHUD>(true))
            {
                if (raceHud.GetComponentInChildren<RaceStandingsTower>(true) == null)
                    Create(raceHud);
            }
        }
    }

    private static void Create(RaceHUD raceHud)
    {
        // Debajo del recuadro de posición y vuelta (arriba a la izquierda).
        RectTransform anchor = null;
        foreach (Transform child in raceHud.transform)
        {
            if (child.name == "PosicionVuelta")
                anchor = child as RectTransform;
        }

        var go = new GameObject("TablaPosiciones", typeof(RectTransform), typeof(RaceStandingsTower));
        go.layer = raceHud.gameObject.layer;

        var rect = (RectTransform)go.transform;
        rect.SetParent(raceHud.transform, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);

        float top = anchor != null ? anchor.anchoredPosition.y - anchor.sizeDelta.y - 6f : -80f;
        float left = anchor != null ? anchor.anchoredPosition.x : 10f;
        rect.anchoredPosition = new Vector2(left, top);

        var tower = go.GetComponent<RaceStandingsTower>();
        rect.sizeDelta = new Vector2(tower.width, 0f);
        tower.hud = raceHud;
        tower.font = FindHudFont(raceHud, bold: false);
        tower.boldFont = FindHudFont(raceHud, bold: true);

        Image panel = anchor != null ? anchor.GetComponent<Image>() : null;
        tower.panelSprite = panel != null ? panel.sprite : null;
    }
}
