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
/// - Hay dos tablas por pista: SECO y LLUVIA (pestañas arriba). Abre la que
///   va con el clima elegido en el menú (al azar o dinámico: la última que se miró).
/// - CUENTA (arriba a la derecha): crear una cuenta con usuario y contraseña
///   (el invitado de ahora pasa a tenerla, con sus marcas), entrar a una que ya
///   existe o salir (PlayerAccount). Con cuenta, el récord te sigue a cualquier computadora.
/// - Arriba a la derecha se elige el nombre de la tabla (aparte del de
///   multijugador; TimeTrialRecords.LocalName).
/// - VER (en cada fila) y VER... (abajo a la derecha): la repetición de esas
///   vueltas con karts normales, sin correr (ViewRequested; la arranca
///   RaceSetupMenuUI con PrepareViewerAsync).
/// </summary>
public class TimeTrialLeaderboardUI : MonoBehaviour
{
    private const int RowsPerPage = 5;

    // Colores de la pantalla de configuración.
    private static readonly Color RowColor = CodeUI.RowColor;
    private static readonly Color BoxColor = CodeUI.BoxColor;
    private static readonly Color MineColor = new Color(0.17f, 0.22f, 0.32f, 1f);
    private static readonly Color AccentRed = CodeUI.AccentRed;
    private static readonly Color MutedText = CodeUI.MutedText;
    private static readonly Color MineAccent = CodeUI.LightBlue;

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
        public Button View;
        public TimeTrialRecords.Entry Entry;
    }

    private RectTransform root;
    private TMP_FontAsset font;

    private TMP_Text headerText;
    private Button dryTab;
    private Button wetTab;
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
    private TMP_InputField nameInput;

    // Cuenta: el botón y su panel (AccountPanelUI).
    private Button accountButton;
    private TMP_Text accountButtonText;
    private AccountPanelUI account;
    private Button viewAllButton;
    private TMP_Text viewAllText;

    /// <summary>Se tocó VER: las vueltas a mostrar (la primera es la que sigue la cámara).</summary>
    public event System.Action<List<TimeTrialRecords.Entry>> ViewRequested;

    private readonly List<GameObject> hidden = new List<GameObject>();

    private string boardId;
    private bool reversed;
    private bool wet;
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

    /// <summary>True con el panel de la cuenta abierto (Empezar no hace nada y Volver lo cierra).</summary>
    public bool IsAccountOpen => account != null && account.IsOpen;

    /// <summary>Cierra el panel de la cuenta (si no está esperando al servidor). False si no se pudo.</summary>
    public bool CloseAccount() => account == null || account.Hide();

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

    /// <summary>Abre la tabla de esa pista y sentido (seca o mojada). Tapa las filas "Fila_*" de la configuración.</summary>
    public void Open(bool reversedTrack, bool wetTrack, string track, string name)
    {
        reversed = reversedTrack;
        trackLabel = track;
        playerName = name;
        signedIn = false;
        SelectBoard(wetTrack);

        // La primera vez, el nombre de la tabla arranca igual al del menú; desde
        // ahí queda aparte (cambiar el de multijugador no lo cambia).
        if (string.IsNullOrEmpty(TimeTrialRecords.LocalName) && !string.IsNullOrWhiteSpace(playerName))
            TimeTrialRecords.SetLocalName(playerName);

        nameInput.SetTextWithoutNotify(TimeTrialRecords.LocalNameOr(playerName));

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

    /// <summary>Pasa a la tabla seca o mojada (sin mostrar ni conectar).</summary>
    private void SelectBoard(bool wetTrack)
    {
        wet = wetTrack;
        boardId = TimeTrialRecords.BoardId(reversed, wet);
        TimeTrialRecords.LastTabWet = wet;

        offset = 0;
        total = 0;
        mine = null;
        online = false;
        selected.Clear();

        // Los rivales que había elegido la última vez en esta tabla.
        if (TimeTrialRecords.LastSelectionBoard == boardId)
            selected.AddRange(TimeTrialRecords.LastSelection);

        PaintTabs();
    }

    /// <summary>Pestaña SECO o LLUVIA.</summary>
    private void SwitchBoard(bool wetTrack)
    {
        if (wetTrack == wet || IsBusy && signedIn)
            return;

        // Los rivales de la tabla que se deja quedan guardados.
        if (selected.Count > 0)
            RememberSelection();

        request++;
        SelectBoard(wetTrack);
        SetStatus("");
        ShowPage(null);
        RefreshSide();
        Connect();
    }

    private void PaintTabs()
    {
        if (dryTab == null || wetTab == null)
            return;

        dryTab.GetComponent<Image>().color = wet ? RowColor : AccentRed;
        wetTab.GetComponent<Image>().color = wet ? AccentRed : RowColor;
    }

    public void Close()
    {
        request++;
        IsBusy = false;

        if (account != null)
            account.Hide();

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

        // ¿Invitado o con cuenta?
        if (signedIn)
            await PlayerAccount.RefreshAsync();

        // Se cerró el menú (o se salió de Play) mientras esperaba: nada que mostrar.
        if (id != request || this == null)
            return;

        RefreshAccountButton();

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
        RememberSelection();
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
            string ago = TimeTrialRecords.HowLongAgo(entry.Date);
            row.When.text = string.IsNullOrEmpty(ago)
                ? TimeTrialRecords.ConditionsLabel(entry)
                : ago + " · " + TimeTrialRecords.ConditionsLabel(entry);
            SetUsable(row.View, online);
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
        if (accountButton != null)
            RefreshAccountButton();

        viewAllText.text = selected.Count > 0
            ? (mine != null ? GameLanguage.Text("VER RIVALES Y TU VUELTA", "WATCH OPPONENTS AND YOU") : GameLanguage.Text("VER RIVALES", "WATCH OPPONENTS"))
            : GameLanguage.Text("VER TU VUELTA", "WATCH YOUR LAP");
        SetUsable(viewAllButton, online && (mine != null || selected.Count > 0));

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

    // ------------------------------------------------------------------
    // Nombre
    // ------------------------------------------------------------------

    private async void HandleNameEdited(string text)
    {
        string name = TimeTrialRecords.CleanName(text);

        // Vacío: vuelve al que tenía.
        if (string.IsNullOrEmpty(name))
        {
            nameInput.SetTextWithoutNotify(TimeTrialRecords.LocalNameOr(playerName));
            return;
        }

        bool changed = name != TimeTrialRecords.LocalName;
        name = TimeTrialRecords.SetLocalName(name);
        nameInput.SetTextWithoutNotify(name);

        if (!changed)
            return;

        // Mi fila, con el nombre nuevo.
        if (mine != null)
            mine.Name = name;

        foreach (RowView row in rows)
        {
            if (row.Entry != null && row.Entry.IsMine)
            {
                row.Entry.Name = name;
                row.Name.text = name + GameLanguage.Text("  (vos)", "  (you)");
            }
        }

        // Que los demás lo vean (si no tengo marca, se manda con la primera).
        if (signedIn && mine != null)
        {
            bool ok = await TimeTrialRecords.PublishNameAsync(name);

            if (this != null)
                SetStatus(ok ? GameLanguage.Text("Nombre guardado.", "Name saved.") : GameLanguage.Text("No se pudo guardar el nombre en la tabla.", "Couldn't save the name online."));
        }
    }

    // ------------------------------------------------------------------
    // Repetición
    // ------------------------------------------------------------------

    private void RequestView(List<TimeTrialRecords.Entry> entries)
    {
        if (IsBusy || !online || entries == null || entries.Count == 0)
            return;

        RememberSelection();
        ViewRequested?.Invoke(entries);
    }

    /// <summary>VER de abajo: mi vuelta (primero) y los rivales elegidos.</summary>
    private void RequestViewAll()
    {
        var entries = new List<TimeTrialRecords.Entry>();

        if (mine != null)
            entries.Add(mine);

        entries.AddRange(selected);
        RequestView(entries);
    }

    private void RememberSelection()
    {
        TimeTrialRecords.LastSelection.Clear();
        TimeTrialRecords.LastSelection.AddRange(selected);
        TimeTrialRecords.LastSelectionBoard = boardId;
    }

    /// <summary>
    /// Baja las vueltas a ver y las deja en TimeTrialRecords.ViewerGhosts (cada una
    /// con el color de su nombre). Devuelve cuántas se pudieron bajar.
    /// </summary>
    public async Task<int> PrepareViewerAsync(List<TimeTrialRecords.Entry> entries)
    {
        TimeTrialRecords.ViewerGhosts.Clear();

        int id = ++request;
        IsBusy = true;
        SetStatus(GameLanguage.Text("Bajando vueltas...", "Downloading laps..."));

        var downloads = new List<Task<GhostRecording>>();

        foreach (TimeTrialRecords.Entry entry in entries)
            downloads.Add(TimeTrialRecords.LoadGhostAsync(boardId, entry.PlayerId));

        await Task.WhenAll(downloads);

        for (int i = 0; i < entries.Count; i++)
        {
            GhostRecording ghost = downloads[i].Result;

            if (ghost == null)
                continue;

            TimeTrialRecords.ViewerGhosts.Add(new TimeTrialRecords.Opponent
            {
                Entry = entries[i],
                Ghost = ghost,
                Color = ColorFor(entries[i])
            });
        }

        if (id == request && this != null)
        {
            IsBusy = false;
            SetStatus(TimeTrialRecords.ViewerGhosts.Count == 0
                ? GameLanguage.Text("No se pudo bajar la vuelta.", "Couldn't download the lap.")
                : "");
        }

        return TimeTrialRecords.ViewerGhosts.Count;
    }

    /// <summary>Color del nombre: el mío celeste, los rivales con su color, el resto blanco.</summary>
    private Color ColorFor(TimeTrialRecords.Entry entry)
    {
        if (entry.IsMine)
            return MineAccent;

        int index = selected.FindIndex(e => e.PlayerId == entry.PlayerId);

        if (index < 0)
            return Color.white;

        Color color = TimeTrialRecords.OpponentColors[index % TimeTrialRecords.OpponentColors.Length];
        color.a = 1f;
        return color;
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
        SetBox(headerText.rectTransform, 10f, 4f, 326f, 20f);
        headerText.overflowMode = TextOverflowModes.Ellipsis;

        // Pestañas: tabla con la pista seca o mojada.
        dryTab = NewButton("Seco", left, GameLanguage.Text("SECO", "DRY"), 342f, 5f, 58f, 17f, () => SwitchBoard(false));
        wetTab = NewButton("Lluvia", left, GameLanguage.Text("LLUVIA", "WET"), 404f, 5f, 58f, 17f, () => SwitchBoard(true));

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

        TMP_Text nameTitle = NewText("NombreTitulo", right, 10f, TextAlignmentOptions.Left);
        SetBox(nameTitle.rectTransform, 12f, 7f, 150f, 13f);

        // CUENTA: invitado o con usuario (abre el panel de la cuenta).
        accountButton = NewButton("Cuenta", right, GameLanguage.Text("CUENTA", "ACCOUNT"), 160f, 4f, 84f, 15f, () => account.Show());
        accountButtonText = accountButton.GetComponentInChildren<TMP_Text>();
        accountButtonText.fontSize = 8.5f;
        accountButtonText.overflowMode = TextOverflowModes.Ellipsis;
        nameTitle.color = MutedText;
        nameTitle.text = GameLanguage.Text("TU NOMBRE EN LA TABLA", "YOUR LEADERBOARD NAME");

        nameInput = BuildInput(right, 12f, 20f, 232f, 20f, GameLanguage.Text("Escribí tu nombre", "Type your name"),
                               TimeTrialRecords.MaxNameLength, false, HandleNameEdited);

        TMP_Text bestTitle = NewText("MiMarcaTitulo", right, 10f, TextAlignmentOptions.Left);
        SetBox(bestTitle.rectTransform, 12f, 46f, 232f, 13f);
        bestTitle.color = MutedText;
        bestTitle.text = GameLanguage.Text("TU MEJOR MARCA", "YOUR BEST");

        myTimeText = NewText("MiTiempo", right, 19f, TextAlignmentOptions.Left);
        SetBox(myTimeText.rectTransform, 12f, 58f, 120f, 24f);

        myRankText = NewText("MiPuesto", right, 10f, TextAlignmentOptions.Left);
        SetBox(myRankText.rectTransform, 12f, 82f, 232f, 14f);
        myRankText.color = MutedText;

        Image line = AddImage(NewRect("Linea", right), new Color(1f, 1f, 1f, 0.08f));
        SetBox((RectTransform)line.transform, 12f, 100f, 232f, 1f);

        rivalsTitle = NewText("RivalesTitulo", right, 10f, TextAlignmentOptions.Left);
        SetBox(rivalsTitle.rectTransform, 12f, 104f, 232f, 13f);
        rivalsTitle.color = MutedText;

        rivalsText = NewText("Rivales", right, 9.5f, TextAlignmentOptions.TopLeft);
        SetBox(rivalsText.rectTransform, 12f, 117f, 232f, 44f);
        rivalsText.textWrappingMode = TextWrappingModes.Normal;
        rivalsText.lineSpacing = -14f;
        rivalsText.enableAutoSizing = true;
        rivalsText.fontSizeMin = 6.5f;
        rivalsText.fontSizeMax = 9.5f;

        viewAllButton = NewButton("VerTodos", right, "", 12f, 164f, 232f, 20f, RequestViewAll);
        viewAllText = viewAllButton.GetComponentInChildren<TMP_Text>();

        // El panel de la cuenta, encima de la tabla.
        account = AccountPanelUI.Create(root, font, false);
        account.Changed += AfterAccountChange;
    }

    // ------------------------------------------------------------------
    // Cuenta
    // ------------------------------------------------------------------

    /// <summary>El botón CUENTA: el usuario, o CUENTA si es invitado.</summary>
    private void RefreshAccountButton()
    {
        string username = PlayerAccount.Username;
        accountButtonText.text = username ?? GameLanguage.Text("CUENTA", "ACCOUNT");
        SetUsable(accountButton, signedIn && (account == null || !account.IsBusy));
    }

    /// <summary>Otra cuenta: la tabla se vuelve a cargar (otra fila "vos", otra marca).</summary>
    private void AfterAccountChange()
    {
        // El nombre de la tabla puede ser otro (el de la cuenta a la que se entró).
        nameInput.SetTextWithoutNotify(TimeTrialRecords.LocalNameOr(playerName));

        TimeTrialRecords.MyOnlineBest = -1f;
        TimeTrialRecords.MyOnlineGhost = null;
        signedIn = false;

        // Los rivales elegidos siguen (son otras filas), salvo que ahora sea uno mismo.
        Connect();
        RefreshAccountButton();
    }

    private TMP_InputField BuildInput(RectTransform parent, float x, float y, float width, float height, string hint,
                                      int characterLimit, bool password, UnityEngine.Events.UnityAction<string> onEndEdit)
        => CodeUI.NewInput(parent, x, y, width, height, hint, characterLimit, password, onEndEdit, font);

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
        SetBox(row.When.rectTransform, 340f, 0f, 74f, 23f);
        row.When.color = MutedText;
        row.When.overflowMode = TextOverflowModes.Ellipsis;

        // VER: la repetición de esa vuelta (encima de la fila, no la elige como rival).
        row.View = NewButton("Ver", row.Rect, GameLanguage.Text("VER", "WATCH"), 416f, 3f, 37f, 17f,
            () => { if (row.Entry != null) RequestView(new List<TimeTrialRecords.Entry> { row.Entry }); });
        row.View.GetComponent<Image>().color = new Color(0.24f, 0.24f, 0.27f, 1f);
        row.View.GetComponentInChildren<TMP_Text>().fontSize = 9f;

        row.Rect.gameObject.SetActive(false);
        return row;
    }

    // ---------- Piezas de UI (CodeUI) ----------

    private static RectTransform NewRect(string name, Transform parent) => CodeUI.NewRect(name, parent);

    private static void SetBox(RectTransform rect, float x, float y, float width, float height) => CodeUI.SetBox(rect, x, y, width, height);

    private static Image AddImage(RectTransform rect, Color color) => CodeUI.AddImage(rect, color);

    private TMP_Text NewText(string name, Transform parent, float size, TextAlignmentOptions alignment)
        => CodeUI.NewText(name, parent, size, alignment, font);

    private Button NewButton(string name, RectTransform parent, string label, float x, float y, float width, float height, UnityEngine.Events.UnityAction action)
        => CodeUI.NewButton(name, parent, label, x, y, width, height, action, font);

    private static void SetUsable(Button button, bool usable) => CodeUI.SetUsable(button, usable);
}
