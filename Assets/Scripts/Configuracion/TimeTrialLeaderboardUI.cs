using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tabla de clasificación de la contrarreloj (como PolyTrack). Se abre en la
/// pantalla de configuración (Panel_RaceSetup) al confirmar Un jugador sin
/// bots: tapa las filas y usa el mismo fondo, cartel y botones (Volver vuelve a
/// la configuración; ¡A CORRER! arranca). La arma RaceSetupMenuUI la primera
/// vez (Create): no hay que tocar la escena.
/// - Izquierda: la tabla, de a 5 filas (puesto, piloto, tiempo, cuándo y con
///   qué clima). Tu fila se ve marcada. &lt; y &gt; pasan de página; MI PUESTO
///   salta a la tuya.
/// - Derecha: tu mejor marca (puesto y porcentaje) y los rivales elegidos.
/// - Tocar una fila la elige (o la saca) como rival, hasta MaxOpponents. Al
///   arrancar se bajan sus fantasmas (PrepareRaceAsync) y KartGhost los corre.
/// - Sin internet se puede correr igual contra tu fantasma de esta computadora.
/// </summary>
public class TimeTrialLeaderboardUI : MonoBehaviour
{
    private const int RowsPerPage = 5;

    // Colores de la pantalla de configuración.
    private static readonly Color RowColor = new Color(0.16f, 0.16f, 0.18f, 1f);
    private static readonly Color BoxColor = new Color(0.11f, 0.11f, 0.13f, 0.96f);
    private static readonly Color MineColor = new Color(0.17f, 0.22f, 0.32f, 1f);
    private static readonly Color AccentRed = new Color(0.86f, 0.1f, 0.12f, 1f);
    private static readonly Color MutedText = new Color(0.62f, 0.63f, 0.68f, 1f);
    private static readonly Color MineAccent = new Color(0.55f, 0.85f, 1f, 1f);

    private class RowView
    {
        public RectTransform Rect;
        public Image Background;
        public Image Accent;
        public Button Button;
        public TMP_Text Rank;
        public TMP_Text Name;
        public TMP_Text Time;
        public TMP_Text When;
        public TimeTrialRecords.Entry Entry;
    }

    private RectTransform root;
    private TMP_FontAsset font;

    private TMP_Text headerText;
    private TMP_Text statusText;
    private readonly List<RowView> rows = new List<RowView>();
    private Button previousButton;
    private Button nextButton;
    private Button mineButton;
    private TMP_Text pageText;

    private TMP_Text myTimeText;
    private TMP_Text myRankText;
    private TMP_Text rivalsTitle;
    private TMP_Text rivalsText;

    private readonly List<GameObject> hidden = new List<GameObject>();

    private string boardId;
    private string trackLabel;
    private string playerName;
    private bool online;
    private bool signedIn;
    private int offset;
    private int total;
    private TimeTrialRecords.Entry mine;
    private readonly List<TimeTrialRecords.Entry> selected = new List<TimeTrialRecords.Entry>();

    // Cada pedido lleva un número: si llega uno viejo (se cerró o se pidió otra página), se ignora.
    private int request;

    public bool IsOpen => root != null && root.gameObject.activeSelf;

    /// <summary>True mientras se conecta o baja fantasmas (los botones esperan).</summary>
    public bool IsBusy { get; private set; }

    /// <summary>Arma la tabla dentro de la pantalla de configuración (cerrada).</summary>
    public static TimeTrialLeaderboardUI Create(RectTransform panel, TMP_FontAsset font)
    {
        var ui = panel.gameObject.AddComponent<TimeTrialLeaderboardUI>();
        ui.font = font;
        ui.Build(panel);
        ui.root.gameObject.SetActive(false);
        return ui;
    }

    // ------------------------------------------------------------------
    // Abrir y cerrar
    // ------------------------------------------------------------------

    /// <summary>Abre la tabla de esa pista y sentido. Tapa las filas "Fila_*" de la configuración.</summary>
    public void Open(string board, string track, string name)
    {
        boardId = board;
        trackLabel = track;
        playerName = name;
        offset = 0;
        total = 0;
        mine = null;
        online = false;
        signedIn = false;
        selected.Clear();

        hidden.Clear();

        foreach (Transform child in root.parent)
        {
            if (child.name.StartsWith("Fila_") && child.gameObject.activeSelf)
            {
                hidden.Add(child.gameObject);
                child.gameObject.SetActive(false);
            }
        }

        root.gameObject.SetActive(true);
        root.SetAsLastSibling();

        ShowPage(null);
        RefreshSide();
        Connect();
    }

    public void Close()
    {
        request++;
        IsBusy = false;

        foreach (GameObject row in hidden)
        {
            if (row != null)
                row.SetActive(true);
        }

        hidden.Clear();
        root.gameObject.SetActive(false);
    }

    private async void Connect()
    {
        int id = ++request;
        IsBusy = true;
        SetStatus(GameLanguage.Text("Conectando con la tabla...", "Connecting to the leaderboard..."));

        signedIn = await TimeTrialRecords.SignInAsync(playerName);

        // Se cerró el menú (o se salió de Play) mientras esperaba: nada que mostrar.
        if (id != request || this == null)
            return;

        if (!signedIn)
        {
            IsBusy = false;
            SetStatus(TimeTrialRecords.LastError + GameLanguage.Text(" Podés correr igual contra tu fantasma.", " You can still race your own ghost."));
            return;
        }

        mine = await TimeTrialRecords.GetMineAsync(boardId);

        // Se cerró el menú (o se salió de Play) mientras esperaba: nada que mostrar.
        if (id != request || this == null)
            return;

        string mineError = TimeTrialRecords.LastError;
        TimeTrialRecords.Page page = await TimeTrialRecords.GetPageAsync(boardId, 0, RowsPerPage);

        // Se cerró el menú (o se salió de Play) mientras esperaba: nada que mostrar.
        if (id != request || this == null)
            return;

        IsBusy = false;

        if (page == null)
        {
            SetStatus(TimeTrialRecords.LastError ?? mineError);
            RefreshSide();
            return;
        }

        online = true;
        SetStatus(page.Total == 0 ? GameLanguage.Text("Todavía no hay marcas: ¡sé el primero!", "No times yet: be the first!") : "");
        ShowPage(page);
        RefreshSide();
    }

    private async void LoadPage(int newOffset)
    {
        if (!online)
            return;

        int id = ++request;
        IsBusy = true;
        SetStatus(GameLanguage.Text("Cargando...", "Loading..."));

        TimeTrialRecords.Page page = await TimeTrialRecords.GetPageAsync(boardId, Mathf.Max(0, newOffset), RowsPerPage);

        // Se cerró el menú (o se salió de Play) mientras esperaba: nada que mostrar.
        if (id != request || this == null)
            return;

        IsBusy = false;

        if (page == null)
        {
            SetStatus(TimeTrialRecords.LastError);
            return;
        }

        SetStatus("");
        ShowPage(page);
    }

    /// <summary>
    /// Antes de largar: baja los fantasmas de los rivales elegidos y el mío (por si
    /// en esta computadora no está) y deja todo en TimeTrialRecords para KartGhost.
    /// </summary>
    public async Task PrepareRaceAsync()
    {
        TimeTrialRecords.Opponents.Clear();
        TimeTrialRecords.ActiveBoardId = online ? boardId : null;
        TimeTrialRecords.MyOnlineBest = mine != null ? mine.Time : -1f;
        TimeTrialRecords.MyOnlineGhost = null;

        if (!online)
            return;

        int id = ++request;
        IsBusy = true;
        SetStatus(GameLanguage.Text("Bajando fantasmas...", "Downloading ghosts..."));

        var downloads = new List<Task<GhostRecording>>();

        foreach (TimeTrialRecords.Entry entry in selected)
            downloads.Add(TimeTrialRecords.LoadGhostAsync(boardId, entry.PlayerId));

        Task<GhostRecording> myGhost = mine != null ? TimeTrialRecords.LoadGhostAsync(boardId, mine.PlayerId) : null;

        await Task.WhenAll(downloads);

        if (myGhost != null)
            TimeTrialRecords.MyOnlineGhost = await myGhost;

        for (int i = 0; i < selected.Count; i++)
        {
            GhostRecording ghost = downloads[i].Result;

            if (ghost == null)
                continue;

            TimeTrialRecords.Opponents.Add(new TimeTrialRecords.Opponent
            {
                Entry = selected[i],
                Ghost = ghost,
                Color = TimeTrialRecords.OpponentColors[i % TimeTrialRecords.OpponentColors.Length]
            });
        }

        if (id == request && this != null)
        {
            IsBusy = false;
            SetStatus("");
        }
    }

    // ------------------------------------------------------------------
    // Mostrar
    // ------------------------------------------------------------------

    private void ShowPage(TimeTrialRecords.Page page)
    {
        offset = page != null ? page.Offset : 0;
        total = page != null ? page.Total : 0;

        string players = total == 1 ? GameLanguage.Text("1 piloto", "1 driver") : total + GameLanguage.Text(" pilotos", " drivers");
        headerText.text = GameLanguage.Text("TABLA · ", "LEADERBOARD · ") + trackLabel.ToUpperInvariant() +
                          (online ? "   <size=75%><color=#9EA0AD>" + players + "</color></size>" : "");

        for (int i = 0; i < rows.Count; i++)
        {
            TimeTrialRecords.Entry entry = page != null && i < page.Entries.Count ? page.Entries[i] : null;
            RowView row = rows[i];
            row.Entry = entry;
            row.Rect.gameObject.SetActive(entry != null);

            if (entry == null)
                continue;

            row.Rank.text = entry.Rank + ".º";
            row.Name.text = entry.IsMine ? entry.Name + GameLanguage.Text("  (vos)", "  (you)") : entry.Name;
            row.Time.text = TimeTrialRecords.FormatTime(entry.Time);
            row.When.text = TimeTrialRecords.HowLongAgo(entry.Date) + " · " + TimeTrialRecords.ConditionsLabel(entry);
        }

        int pages = Mathf.Max(1, Mathf.CeilToInt(total / (float)RowsPerPage));
        int current = offset / RowsPerPage + 1;
        pageText.text = online ? current + " / " + pages : "";

        SetUsable(previousButton, online && offset > 0);
        SetUsable(nextButton, online && offset + RowsPerPage < total);
        SetUsable(mineButton, online && mine != null);

        PaintRows();
    }

    private void PaintRows()
    {
        foreach (RowView row in rows)
        {
            if (row.Entry == null)
                continue;

            int index = selected.FindIndex(e => e.PlayerId == row.Entry.PlayerId);

            row.Background.color = row.Entry.IsMine ? MineColor : RowColor;

            if (index >= 0)
            {
                Color color = TimeTrialRecords.OpponentColors[index % TimeTrialRecords.OpponentColors.Length];
                color.a = 1f;
                row.Accent.color = color;
                row.Accent.gameObject.SetActive(true);
            }
            else if (row.Entry.IsMine)
            {
                row.Accent.color = MineAccent;
                row.Accent.gameObject.SetActive(true);
            }
            else
            {
                row.Accent.gameObject.SetActive(false);
            }
        }
    }

    private void RefreshSide()
    {
        if (mine != null)
        {
            myTimeText.text = TimeTrialRecords.FormatTime(mine.Time);

            float top = total > 0 ? 100f * mine.Rank / total : 0f;
            myRankText.text = GameLanguage.Text("Puesto ", "Rank ") + mine.Rank + GameLanguage.Text(" de ", " of ") + total +
                              "  ·  top " + Mathf.Max(1, Mathf.CeilToInt(top)) + " %";
        }
        else
        {
            myTimeText.text = "-:--.---";
            myRankText.text = signedIn
                ? GameLanguage.Text("Todavía no tenés marca", "No time yet")
                : GameLanguage.Text("Sin conexión", "Offline");
        }

        rivalsTitle.text = GameLanguage.Text("RIVALES ", "OPPONENTS ") + selected.Count + "/" + TimeTrialRecords.MaxOpponents;

        if (selected.Count == 0)
        {
            rivalsText.text = "<color=#9EA0AD>" + GameLanguage.Text(
                "Tocá filas de la tabla para correr contra sus fantasmas. Tu fantasma (celeste) corre siempre.",
                "Click rows to race their ghosts. Your own ghost (light blue) always races.") + "</color>";
            return;
        }

        var text = new System.Text.StringBuilder();

        for (int i = 0; i < selected.Count; i++)
        {
            Color color = TimeTrialRecords.OpponentColors[i % TimeTrialRecords.OpponentColors.Length];
            color.a = 1f;
            text.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(color)).Append(">")
                .Append(selected[i].Name).Append("</color>  <color=#9EA0AD>")
                .Append(TimeTrialRecords.FormatTime(selected[i].Time)).Append("</color>\n");
        }

        rivalsText.text = text.ToString();
    }

    private void SetStatus(string message)
    {
        statusText.text = message ?? "";
    }

    private void ToggleRival(RowView row)
    {
        if (row.Entry == null || row.Entry.IsMine || IsBusy)
            return;

        int index = selected.FindIndex(e => e.PlayerId == row.Entry.PlayerId);

        if (index >= 0)
            selected.RemoveAt(index);
        else if (selected.Count < TimeTrialRecords.MaxOpponents)
            selected.Add(row.Entry);
        else
            SetStatus(GameLanguage.Text("Hasta " + TimeTrialRecords.MaxOpponents + " rivales.", "Up to " + TimeTrialRecords.MaxOpponents + " opponents."));

        PaintRows();
        RefreshSide();
    }

    // ------------------------------------------------------------------
    // Armado (lugar de las filas de la configuración: 736 × 186 px)
    // ------------------------------------------------------------------

    private void Build(RectTransform panel)
    {
        root = NewRect("TablaContrarreloj", panel);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
        root.sizeDelta = new Vector2(736f, 188f);
        root.anchoredPosition = new Vector2(0f, -28f);

        // Izquierda: la tabla.
        RectTransform left = NewRect("Tabla", root);
        SetBox(left, 0f, 0f, 472f, 188f);
        AddImage(left, BoxColor);

        headerText = NewText("Titulo", left, 13f, TextAlignmentOptions.Left);
        SetBox(headerText.rectTransform, 10f, 4f, 452f, 20f);

        TMP_Text columns = NewText("Columnas", left, 9.5f, TextAlignmentOptions.Left);
        SetBox(columns.rectTransform, 10f, 24f, 452f, 12f);
        columns.color = MutedText;
        columns.text = GameLanguage.Text("PUESTO      PILOTO", "RANK         DRIVER") +
                       "<pos=58%>" + GameLanguage.Text("TIEMPO", "TIME") + "<pos=75%>" + GameLanguage.Text("CUÁNDO", "WHEN");

        for (int i = 0; i < RowsPerPage; i++)
            rows.Add(BuildRow(left, 38f + i * 25f));

        // Abajo de la tabla: páginas y mi puesto.
        previousButton = NewButton("Anterior", left, "<", 8f, 164f, 30f, 20f, () => LoadPage(offset - RowsPerPage));
        pageText = NewText("Pagina", left, 11f, TextAlignmentOptions.Center);
        SetBox(pageText.rectTransform, 40f, 164f, 52f, 20f);
        nextButton = NewButton("Siguiente", left, ">", 94f, 164f, 30f, 20f, () => LoadPage(offset + RowsPerPage));
        mineButton = NewButton("MiPuesto", left, GameLanguage.Text("MI PUESTO", "MY RANK"), 132f, 164f, 84f, 20f,
            () => { if (mine != null) LoadPage((mine.Rank - 1) / RowsPerPage * RowsPerPage); });

        statusText = NewText("Estado", left, 10f, TextAlignmentOptions.Right);
        SetBox(statusText.rectTransform, 222f, 162f, 242f, 24f);
        statusText.color = new Color(1f, 0.78f, 0.35f);
        statusText.textWrappingMode = TextWrappingModes.Normal;
        statusText.enableAutoSizing = true;
        statusText.fontSizeMin = 7f;
        statusText.fontSizeMax = 10f;

        // Derecha: mi marca y rivales.
        RectTransform right = NewRect("Lado", root);
        SetBox(right, 480f, 0f, 256f, 188f);
        AddImage(right, BoxColor);

        Image accent = AddImage(NewRect("Acento", right), AccentRed);
        SetBox((RectTransform)accent.transform, 0f, 0f, 256f, 3f);

        TMP_Text bestTitle = NewText("MiMarcaTitulo", right, 10f, TextAlignmentOptions.Left);
        SetBox(bestTitle.rectTransform, 12f, 8f, 232f, 14f);
        bestTitle.color = MutedText;
        bestTitle.text = GameLanguage.Text("TU MEJOR MARCA", "YOUR BEST");

        myTimeText = NewText("MiTiempo", right, 22f, TextAlignmentOptions.Left);
        SetBox(myTimeText.rectTransform, 12f, 21f, 232f, 28f);

        myRankText = NewText("MiPuesto", right, 10.5f, TextAlignmentOptions.Left);
        SetBox(myRankText.rectTransform, 12f, 49f, 232f, 16f);
        myRankText.color = MutedText;

        Image line = AddImage(NewRect("Linea", right), new Color(1f, 1f, 1f, 0.08f));
        SetBox((RectTransform)line.transform, 12f, 70f, 232f, 1f);

        rivalsTitle = NewText("RivalesTitulo", right, 10f, TextAlignmentOptions.Left);
        SetBox(rivalsTitle.rectTransform, 12f, 76f, 232f, 14f);
        rivalsTitle.color = MutedText;

        rivalsText = NewText("Rivales", right, 10.5f, TextAlignmentOptions.TopLeft);
        SetBox(rivalsText.rectTransform, 12f, 92f, 232f, 92f);
        rivalsText.textWrappingMode = TextWrappingModes.Normal;
        rivalsText.lineSpacing = -8f;
    }

    private RowView BuildRow(RectTransform parent, float top)
    {
        var row = new RowView();
        row.Rect = NewRect("Fila", parent);
        SetBox(row.Rect, 8f, top, 456f, 23f);
        row.Background = AddImage(row.Rect, RowColor);

        row.Button = row.Rect.gameObject.AddComponent<Button>();
        row.Button.targetGraphic = row.Background;
        ColorBlock colors = row.Button.colors;
        colors.highlightedColor = new Color(1.25f, 1.25f, 1.3f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.85f, 1f);
        row.Button.colors = colors;
        row.Button.onClick.AddListener(() => ToggleRival(row));

        row.Accent = AddImage(NewRect("Marca", row.Rect), AccentRed);
        SetBox((RectTransform)row.Accent.transform, 0f, 0f, 4f, 23f);
        row.Accent.gameObject.SetActive(false);

        row.Rank = NewText("Puesto", row.Rect, 12f, TextAlignmentOptions.Left);
        SetBox(row.Rank.rectTransform, 10f, 0f, 44f, 23f);

        row.Name = NewText("Piloto", row.Rect, 12f, TextAlignmentOptions.Left);
        SetBox(row.Name.rectTransform, 56f, 0f, 200f, 23f);
        row.Name.overflowMode = TextOverflowModes.Ellipsis;

        row.Time = NewText("Tiempo", row.Rect, 12f, TextAlignmentOptions.Left);
        SetBox(row.Time.rectTransform, 262f, 0f, 74f, 23f);

        row.When = NewText("Cuando", row.Rect, 9f, TextAlignmentOptions.Left);
        SetBox(row.When.rectTransform, 340f, 0f, 112f, 23f);
        row.When.color = MutedText;
        row.When.overflowMode = TextOverflowModes.Ellipsis;

        row.Rect.gameObject.SetActive(false);
        return row;
    }

    // ---------- Piezas de UI ----------

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    /// <summary>Ubica un rectángulo desde la esquina de arriba a la izquierda del padre (px).</summary>
    private static void SetBox(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static Image AddImage(RectTransform rect, Color color)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private TMP_Text NewText(string name, Transform parent, float size, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);

        var text = go.GetComponent<TextMeshProUGUI>();

        if (font != null)
            text.font = font;

        text.fontSize = size;
        text.color = Color.white;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    private Button NewButton(string name, RectTransform parent, string label, float x, float y, float width, float height, UnityEngine.Events.UnityAction action)
    {
        RectTransform rect = NewRect(name, parent);
        SetBox(rect, x, y, width, height);
        Image image = AddImage(rect, RowColor);

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.3f, 1.3f, 1.35f, 1f);
        colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
        button.colors = colors;
        button.onClick.AddListener(action);

        TMP_Text text = NewText("Texto", rect, 10.5f, TextAlignmentOptions.Center);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
        text.text = label;
        return button;
    }

    private static void SetUsable(Button button, bool usable)
    {
        button.interactable = usable;

        TMP_Text label = button.GetComponentInChildren<TMP_Text>();

        if (label != null)
            label.alpha = usable ? 1f : 0.35f;
    }
}
