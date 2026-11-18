using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Panel de la cuenta (PlayerAccount): entrar a una cuenta que ya existe, crear
/// una (el invitado de ahora pasa a tenerla, con sus marcas), cambiar la
/// contraseña o salir. Se arma por código (Create) y lo abren la tabla de la
/// contrarreloj (botón CUENTA) y el menú principal (MainMenuAccount). Si todavía
/// no hay conexión con los servicios, se conecta al abrirse.
///
/// Diseño (18/11/2026): a la izquierda, quién sos (invitado o tu usuario); a la
/// derecha, dos pestañas para el invitado (ENTRAR y CREAR CUENTA) o el cambio de
/// contraseña si ya tenés cuenta. Al crear la cuenta se ve qué reglas de la
/// contraseña ya se cumplen. Tab pasa de un campo al otro y Enter confirma. Al
/// entrar o crear la cuenta, el panel se cierra solo.
///
/// Seguridad: las contraseñas solo pasan por los campos de este panel y van
/// directo a PlayerAccount; los campos se vacían apenas se aprieta un botón, al
/// fallar y al cerrar. Los de contraseña se ven con asteriscos y no dejan copiar
/// ni cortar (pegar sí, para los gestores de contraseñas).
/// </summary>
public class AccountPanelUI : MonoBehaviour
{
    public const float Width = 736f;
    public const float Height = 188f;

    // En el menú, cuánto del ancho de la pantalla puede ocupar como máximo (el lienzo del menú mide 800 de ancho).
    private const float ModalMaxWidth = 0.95f;

    // Columna de la izquierda (quién sos) y zona de la derecha (formularios).
    private const float SideWidth = 216f;
    private const float FormX = 236f;
    private const float FormRight = 722f;

    private static readonly Color SideColor = new Color(0.12f, 0.12f, 0.145f, 1f);
    private static readonly Color FieldColor = new Color(0.17f, 0.17f, 0.2f, 1f);
    private static readonly Color LineColor = new Color(1f, 1f, 1f, 0.08f);
    private static readonly Color RuleOff = new Color(0.36f, 0.37f, 0.42f, 1f);
    private static readonly Color RuleOn = new Color(0.32f, 0.86f, 0.5f, 1f);
    private static readonly Color ErrorColor = new Color(1f, 0.55f, 0.5f, 1f);

    private enum Mode { SignIn, Create }

    private TMP_FontAsset font;
    private RectTransform blocker;
    private RectTransform panel;

    // Izquierda
    private TMP_Text stateText;
    private TMP_Text sideInfo;
    private TMP_Text sideNote;
    private Image stateBar;

    // Invitado
    private RectTransform guestForm;
    private Mode mode = Mode.SignIn;
    private Button signInTab;
    private Button createTab;
    private Image tabUnderline;
    private TMP_Text userLabel;
    private TMP_Text passwordLabel;
    private TMP_Text repeatLabel;
    private TMP_InputField userInput;
    private TMP_InputField passwordInput;
    private TMP_InputField repeatInput;
    private RectTransform rulesRow;
    private readonly List<(Image dot, TMP_Text text)> rules = new List<(Image, TMP_Text)>();
    private Button primaryButton;
    private TMP_Text primaryText;
    private TMP_Text guestNote;

    // Con cuenta
    private RectTransform signedForm;
    private TMP_InputField currentInput;
    private TMP_InputField newPasswordInput;
    private TMP_InputField repeatNewInput;
    private Button changeButton;
    private Button signOutButton;

    private TMP_Text status;

    private bool busy;
    private bool connecting;

    /// <summary>Cambió la cuenta (después de crearla, entrar a otra o salir, con el nombre ya traído).</summary>
    public event Action Changed;

    public bool IsOpen => Root != null && Root.gameObject.activeSelf;

    /// <summary>True mientras espera al servidor (no se puede cerrar).</summary>
    public bool IsBusy => busy || connecting;

    private RectTransform Root => blocker != null ? blocker : panel;

    /// <summary>
    /// Arma el panel (cerrado). modal: centrado, más grande y con un fondo oscuro
    /// que tapa todo el padre (menú principal); si no, ocupa los 736 × 188 px de
    /// arriba a la izquierda del padre (encima de la tabla).
    /// </summary>
    public static AccountPanelUI Create(RectTransform parent, TMP_FontAsset font, bool modal)
    {
        var holder = CodeUI.NewRect("Cuenta", parent);
        var ui = holder.gameObject.AddComponent<AccountPanelUI>();
        ui.font = font;

        if (modal)
        {
            CodeUI.Fill(holder);
            ui.blocker = holder;
            Image dim = CodeUI.AddImage(holder, new Color(0f, 0f, 0f, 0.82f));
            dim.raycastTarget = true;

            ui.panel = CodeUI.NewRect("Panel", holder);
            ui.panel.anchorMin = ui.panel.anchorMax = ui.panel.pivot = new Vector2(0.5f, 0.5f);
            ui.panel.sizeDelta = new Vector2(Width, Height);
            ui.panel.anchoredPosition = Vector2.zero;
        }
        else
        {
            CodeUI.SetBox(holder, 0f, 0f, Width, Height);
            ui.panel = holder;
        }

        ui.Build();
        ui.Root.gameObject.SetActive(false);
        return ui;
    }

    // ------------------------------------------------------------------
    // Abrir y cerrar
    // ------------------------------------------------------------------

    public void Show()
    {
        if (busy)
            return;

        ClearPasswords();
        Root.gameObject.SetActive(true);

        // En el menú: que entre en la pantalla (sin agrandarlo).
        if (blocker != null && blocker.rect.width > 0f)
            panel.localScale = Vector3.one * Mathf.Min(1f, blocker.rect.width * ModalMaxWidth / Width);

        Root.SetAsLastSibling();
        SetStatus("", false);
        Refresh();
        FocusFirst();

        if (!PlayerAccount.IsReady)
            Connect();
    }

    /// <summary>Cierra el panel (si no está esperando al servidor). False si no se pudo.</summary>
    public bool Hide()
    {
        if (busy)
            return false;

        ClearPasswords();

        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
            EventSystem.current.currentSelectedGameObject.transform.IsChildOf(Root))
            EventSystem.current.SetSelectedGameObject(null);

        Root.gameObject.SetActive(false);
        return true;
    }

    /// <summary>Se conecta a los servicios (como invitado, o con la cuenta que quedó guardada).</summary>
    private async void Connect()
    {
        if (connecting || GameSession.Instance == null)
            return;

        connecting = true;
        string connectingText = GameLanguage.Text("Conectando...", "Connecting...");

        if (string.IsNullOrEmpty(status.text))
            SetStatus(connectingText, false);

        Refresh();

        bool ok = await GameSession.Instance.EnsureSignedInAsync(SessionMenuUI.SavedName);

        if (ok)
            await PlayerAccount.RefreshAsync();

        connecting = false;

        if (this == null)
            return;

        if (!ok)
            SetStatus(GameLanguage.Text("Sin conexión con el servidor.", "Can't reach the server."), true);
        else if (status.text == connectingText)
            SetStatus("", false);

        Refresh();
    }

    // ------------------------------------------------------------------
    // Armado
    // ------------------------------------------------------------------

    private void Build()
    {
        CodeUI.AddImage(panel, CodeUI.PanelColor).raycastTarget = true;
        Outline border = panel.gameObject.AddComponent<Outline>();
        border.effectColor = new Color(1f, 1f, 1f, 0.07f);
        border.effectDistance = new Vector2(1f, -1f);

        // Izquierda: quién sos.
        RectTransform side = CodeUI.NewRect("Lado", panel);
        CodeUI.SetBox(side, 0f, 0f, SideWidth, Height);
        CodeUI.AddImage(side, SideColor);

        Image accent = CodeUI.AddImage(CodeUI.NewRect("Acento", panel), CodeUI.AccentRed);
        CodeUI.SetBox((RectTransform)accent.transform, 0f, 0f, Width, 3f);

        TMP_Text title = Label(side, GameLanguage.Text("CUENTA", "ACCOUNT"), 16f, 16f, 180f);
        title.characterSpacing = 6f;

        stateBar = CodeUI.AddImage(CodeUI.NewRect("Marca", side), CodeUI.MutedText);
        CodeUI.SetBox((RectTransform)stateBar.transform, 16f, 36f, 4f, 24f);

        stateText = CodeUI.NewText("Estado", side, 19f, TextAlignmentOptions.MidlineLeft, font);
        CodeUI.SetBox(stateText.rectTransform, 27f, 34f, 180f, 28f);
        stateText.fontStyle = FontStyles.Bold;
        stateText.overflowMode = TextOverflowModes.Ellipsis;

        sideInfo = CodeUI.NewText("Info", side, 9.5f, TextAlignmentOptions.TopLeft, font);
        CodeUI.SetBox(sideInfo.rectTransform, 16f, 72f, 186f, 64f);
        sideInfo.textWrappingMode = TextWrappingModes.Normal;
        sideInfo.color = new Color(0.82f, 0.83f, 0.87f, 1f);
        sideInfo.lineSpacing = -8f;

        sideNote = CodeUI.NewText("Nota", side, 8.5f, TextAlignmentOptions.BottomLeft, font);
        CodeUI.SetBox(sideNote.rectTransform, 16f, 138f, 186f, 38f);
        sideNote.textWrappingMode = TextWrappingModes.Normal;
        sideNote.color = CodeUI.MutedText;
        sideNote.lineSpacing = -10f;

        // Cerrar (arriba a la derecha).
        Button close = CodeUI.NewButton("Cerrar", panel, GameLanguage.Text("CERRAR", "CLOSE"), FormRight - 70f, 12f, 70f, 20f, () => Hide(), font);
        close.GetComponentInChildren<TMP_Text>().fontSize = 9f;

        BuildGuestForm();
        BuildSignedForm();

        status = CodeUI.NewText("Mensaje", panel, 9.5f, TextAlignmentOptions.MidlineLeft, font);
        status.textWrappingMode = TextWrappingModes.Normal;
        status.enableAutoSizing = true;
        status.fontSizeMin = 7f;
        status.fontSizeMax = 9.5f;
        status.lineSpacing = -10f;

        ApplyMode();
    }

    private void BuildGuestForm()
    {
        guestForm = CodeUI.NewRect("Invitado", panel);
        CodeUI.SetBox(guestForm, 0f, 0f, Width, Height);

        signInTab = Tab(guestForm, GameLanguage.Text("ENTRAR", "SIGN IN"), FormX, 96f, () => SetMode(Mode.SignIn));
        createTab = Tab(guestForm, GameLanguage.Text("CREAR CUENTA", "CREATE ACCOUNT"), FormX + 104f, 128f, () => SetMode(Mode.Create));

        Image line = CodeUI.AddImage(CodeUI.NewRect("Linea", guestForm), LineColor);
        CodeUI.SetBox((RectTransform)line.transform, FormX, 38f, FormRight - FormX, 1f);

        tabUnderline = CodeUI.AddImage(CodeUI.NewRect("Subrayado", guestForm), CodeUI.AccentRed);

        userLabel = Label(guestForm, GameLanguage.Text("USUARIO", "USERNAME"), 0f, 50f, 150f);
        passwordLabel = Label(guestForm, GameLanguage.Text("CONTRASEÑA", "PASSWORD"), 0f, 50f, 150f);
        repeatLabel = Label(guestForm, GameLanguage.Text("REPETIR CONTRASEÑA", "REPEAT PASSWORD"), 0f, 50f, 160f);

        userInput = Input(guestForm, GameLanguage.Text("Tu usuario", "Your username"), PlayerAccount.MaxUsername, false);
        userInput.onValidateInput += (text, index, c) => PlayerAccount.IsUsernameChar(c) ? c : '\0';
        passwordInput = Input(guestForm, GameLanguage.Text("Tu contraseña", "Your password"), PlayerAccount.MaxPassword, true);
        repeatInput = Input(guestForm, GameLanguage.Text("Otra vez", "Again"), PlayerAccount.MaxPassword, true);

        passwordInput.onValueChanged.AddListener(_ => RefreshRules());
        repeatInput.onValueChanged.AddListener(_ => RefreshRules());
        userInput.onValueChanged.AddListener(_ => RefreshRules());

        // Las reglas de la contraseña (solo al crear): se prenden a medida que se cumplen.
        rulesRow = CodeUI.NewRect("Reglas", guestForm);
        CodeUI.SetBox(rulesRow, FormX, 94f, FormRight - FormX, 14f);
        string[] names =
        {
            GameLanguage.Text("8 a 30", "8 to 30"), GameLanguage.Text("Mayúscula", "Uppercase"), GameLanguage.Text("Minúscula", "Lowercase"),
            GameLanguage.Text("Número", "Number"), GameLanguage.Text("Símbolo", "Symbol"), GameLanguage.Text("Sin tu usuario", "No username"),
            GameLanguage.Text("Coinciden", "Match")
        };
        float x = 0f;

        foreach (string name in names)
        {
            Image dot = CodeUI.AddImage(CodeUI.NewRect("Punto", rulesRow), RuleOff);
            CodeUI.SetBox((RectTransform)dot.transform, x, 4f, 6f, 6f);
            TMP_Text text = CodeUI.NewText("Regla", rulesRow, 8.5f, TextAlignmentOptions.MidlineLeft, font);
            CodeUI.SetBox(text.rectTransform, x + 9f, 0f, 70f, 14f);
            text.text = name;
            text.color = RuleOff;
            rules.Add((dot, text));
            x += 9f + text.GetPreferredValues(name).x + 12f;
        }

        primaryButton = PrimaryButton(guestForm, "", Submit);
        primaryText = primaryButton.GetComponentInChildren<TMP_Text>();

        guestNote = CodeUI.NewText("Nota", guestForm, 8.5f, TextAlignmentOptions.TopLeft, font);
        CodeUI.SetBox(guestNote.rectTransform, FormX, 150f, FormRight - FormX, 32f);
        guestNote.textWrappingMode = TextWrappingModes.Normal;
        guestNote.color = CodeUI.MutedText;
        guestNote.lineSpacing = -10f;

        foreach (TMP_InputField field in new[] { userInput, passwordInput, repeatInput })
            field.onSubmit.AddListener(_ => Submit());
    }

    private void BuildSignedForm()
    {
        signedForm = CodeUI.NewRect("ConCuenta", panel);
        CodeUI.SetBox(signedForm, 0f, 0f, Width, Height);

        TMP_Text heading = CodeUI.NewText("Titulo", signedForm, 12f, TextAlignmentOptions.MidlineLeft, font);
        CodeUI.SetBox(heading.rectTransform, FormX, 12f, 300f, 22f);
        heading.fontStyle = FontStyles.Bold;
        heading.text = GameLanguage.Text("CAMBIAR CONTRASEÑA", "CHANGE PASSWORD");

        Image line = CodeUI.AddImage(CodeUI.NewRect("Linea", signedForm), LineColor);
        CodeUI.SetBox((RectTransform)line.transform, FormX, 38f, FormRight - FormX, 1f);

        float w = (FormRight - FormX - 16f) / 3f;
        Label(signedForm, GameLanguage.Text("LA DE AHORA", "CURRENT"), FormX, 50f, w);
        Label(signedForm, GameLanguage.Text("NUEVA", "NEW"), FormX + w + 8f, 50f, w);
        Label(signedForm, GameLanguage.Text("REPETIR LA NUEVA", "REPEAT NEW"), FormX + 2f * (w + 8f), 50f, w);

        currentInput = Input(signedForm, "", PlayerAccount.MaxPassword, true);
        newPasswordInput = Input(signedForm, "", PlayerAccount.MaxPassword, true);
        repeatNewInput = Input(signedForm, "", PlayerAccount.MaxPassword, true);
        CodeUI.SetBox((RectTransform)currentInput.transform, FormX, 63f, w, 26f);
        CodeUI.SetBox((RectTransform)newPasswordInput.transform, FormX + w + 8f, 63f, w, 26f);
        CodeUI.SetBox((RectTransform)repeatNewInput.transform, FormX + 2f * (w + 8f), 63f, w, 26f);

        foreach (TMP_InputField field in new[] { currentInput, newPasswordInput, repeatNewInput })
            field.onSubmit.AddListener(_ => ChangePassword());

        changeButton = PrimaryButton(signedForm, GameLanguage.Text("CAMBIAR CONTRASEÑA", "CHANGE PASSWORD"), ChangePassword);
        CodeUI.SetBox((RectTransform)changeButton.transform, FormX, 104f, 186f, 28f);

        signOutButton = CodeUI.NewButton("Salir", signedForm, GameLanguage.Text("SALIR DE LA CUENTA", "SIGN OUT"), FormRight - 150f, 104f, 150f, 28f, SignOut, font);
        signOutButton.GetComponentInChildren<TMP_Text>().fontSize = 10f;

        TMP_Text note = CodeUI.NewText("Nota", signedForm, 8.5f, TextAlignmentOptions.TopLeft, font);
        CodeUI.SetBox(note.rectTransform, FormX, 150f, FormRight - FormX, 32f);
        note.textWrappingMode = TextWrappingModes.Normal;
        note.color = CodeUI.MutedText;
        note.lineSpacing = -10f;
        note.text = GameLanguage.Text("Cambiar la contraseña cierra tu sesión en las otras computadoras. Salir borra la sesión de esta: después sos un invitado nuevo.",
                                      "Changing the password signs you out on other computers. Signing out deletes this computer's session: you become a new guest.");
    }

    // ------------------------------------------------------------------
    // Piezas
    // ------------------------------------------------------------------

    private TMP_Text Label(RectTransform parent, string text, float x, float y, float width)
    {
        TMP_Text label = CodeUI.NewText("Titulo", parent, 8.5f, TextAlignmentOptions.MidlineLeft, font);
        CodeUI.SetBox(label.rectTransform, x, y, width, 12f);
        label.color = CodeUI.MutedText;
        label.characterSpacing = 3f;
        label.text = text;
        return label;
    }

    private TMP_InputField Input(RectTransform parent, string hint, int limit, bool password)
    {
        TMP_InputField input = CodeUI.NewInput(parent, 0f, 0f, 100f, 26f, hint, limit, password, null, font);
        input.onValidateInput = (text, index, c) => c == '\t' ? '\0' : c;   // Tab pasa de campo (Update), no se escribe
        input.GetComponent<Image>().color = FieldColor;
        input.textComponent.fontSize = 12.5f;

        // Una línea fina abajo, que se pone roja cuando el campo está activo.
        Image line = CodeUI.AddImage(CodeUI.NewRect("Linea", input.transform), new Color(1f, 1f, 1f, 0.14f));
        RectTransform rect = (RectTransform)line.transform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(0f, 1.5f);
        rect.anchoredPosition = Vector2.zero;
        input.onSelect.AddListener(_ => line.color = CodeUI.AccentRed);
        input.onDeselect.AddListener(_ => line.color = new Color(1f, 1f, 1f, 0.14f));
        return input;
    }

    private Button Tab(RectTransform parent, string text, float x, float width, UnityEngine.Events.UnityAction action)
    {
        Button tab = CodeUI.NewButton("Pestana", parent, text, x, 12f, width, 24f, action, font);
        Image image = tab.GetComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0f);
        ColorBlock colors = tab.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
        tab.colors = colors;
        TMP_Text label = tab.GetComponentInChildren<TMP_Text>();
        label.fontSize = 11f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.characterSpacing = 3f;
        return tab;
    }

    private Button PrimaryButton(RectTransform parent, string text, UnityEngine.Events.UnityAction action)
    {
        Button button = CodeUI.NewButton("Principal", parent, text, 0f, 0f, 100f, 28f, action, font);
        button.GetComponent<Image>().color = CodeUI.AccentRed;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.25f, 1.15f, 1.15f, 1f);
        colors.pressedColor = new Color(0.8f, 0.75f, 0.75f, 1f);
        button.colors = colors;
        TMP_Text label = button.GetComponentInChildren<TMP_Text>();
        label.fontSize = 11.5f;
        label.fontStyle = FontStyles.Bold;
        label.characterSpacing = 4f;
        return button;
    }

    // ------------------------------------------------------------------
    // Estado
    // ------------------------------------------------------------------

    private void SetMode(Mode value)
    {
        if (busy || mode == value)
            return;

        mode = value;
        ClearPasswords();
        SetStatus("", false);
        ApplyMode();
        Refresh();
        FocusFirst();
    }

    /// <summary>Acomoda los campos según la pestaña (ENTRAR: dos campos; CREAR CUENTA: tres y las reglas).</summary>
    private void ApplyMode()
    {
        bool create = mode == Mode.Create;
        float formWidth = FormRight - FormX;

        if (create)
        {
            float w = (formWidth - 16f) / 3f;
            Place(userLabel, userInput, FormX, w);
            Place(passwordLabel, passwordInput, FormX + w + 8f, w);
            Place(repeatLabel, repeatInput, FormX + 2f * (w + 8f), w);
            CodeUI.SetBox((RectTransform)primaryButton.transform, FormX, 114f, 160f, 28f);
            CodeUI.SetBox(status.rectTransform, FormX + 172f, 114f, formWidth - 172f, 28f);
        }
        else
        {
            float w = (formWidth - 8f) / 2f;
            Place(userLabel, userInput, FormX, w);
            Place(passwordLabel, passwordInput, FormX + w + 8f, w);
            CodeUI.SetBox((RectTransform)primaryButton.transform, FormX, 104f, 160f, 28f);
            CodeUI.SetBox(status.rectTransform, FormX + 172f, 104f, formWidth - 172f, 28f);
        }

        repeatLabel.gameObject.SetActive(create);
        repeatInput.gameObject.SetActive(create);
        rulesRow.gameObject.SetActive(create);

        primaryText.text = create ? GameLanguage.Text("CREAR CUENTA", "CREATE ACCOUNT") : GameLanguage.Text("ENTRAR", "SIGN IN");
        passwordInput.placeholder.GetComponent<TMP_Text>().text = create
            ? GameLanguage.Text("Elegí una", "Pick one")
            : GameLanguage.Text("Tu contraseña", "Your password");

        guestNote.text = create
            ? GameLanguage.Text("Tus marcas de ahora pasan a la cuenta nueva. Usuario: 3 a 20 (letras, números y . - @ _). Usá una contraseña que no uses en otro lado y anotala: no se puede recuperar.",
                                "Your current times move to the new account. Username: 3 to 20 (letters, numbers and . - @ _). Use a password you don't use anywhere else and write it down: it can't be recovered.")
            : GameLanguage.Text("Entrá a una cuenta que ya tenés. Las marcas de este invitado quedan en la tabla como otro piloto. El juego no guarda tu contraseña.",
                                "Sign in to an account you already have. This guest's times stay on the board as another driver. The game never stores your password.");

        // Pestaña activa: texto blanco y subrayado rojo; la otra, apagada.
        Button active = create ? createTab : signInTab;
        Button other = create ? signInTab : createTab;
        active.GetComponentInChildren<TMP_Text>().color = Color.white;
        other.GetComponentInChildren<TMP_Text>().color = CodeUI.MutedText;
        RectTransform tabRect = (RectTransform)active.transform;
        TMP_Text activeLabel = active.GetComponentInChildren<TMP_Text>();
        CodeUI.SetBox((RectTransform)tabUnderline.transform, tabRect.anchoredPosition.x, 36f, activeLabel.GetPreferredValues(activeLabel.text).x, 3f);

        RefreshRules();
    }

    private static void Place(TMP_Text label, TMP_InputField input, float x, float width)
    {
        CodeUI.SetBox(label.rectTransform, x, 50f, width, 12f);
        CodeUI.SetBox((RectTransform)input.transform, x, 63f, width, 26f);
    }

    /// <summary>Prende las reglas de la contraseña que ya se cumplen (solo se miran acá, no se guardan).</summary>
    private void RefreshRules()
    {
        if (rules.Count == 0 || mode != Mode.Create)
            return;

        string password = passwordInput.text ?? "";
        string username = (userInput.text ?? "").Trim();
        bool hasUpper = false, hasLower = false, hasDigit = false, hasSymbol = false;

        foreach (char c in password)
        {
            if (char.IsUpper(c)) hasUpper = true;
            else if (char.IsLower(c)) hasLower = true;
            else if (char.IsDigit(c)) hasDigit = true;
            else if (!char.IsWhiteSpace(c)) hasSymbol = true;
        }

        bool[] ok =
        {
            password.Length >= PlayerAccount.MinPassword && password.Length <= PlayerAccount.MaxPassword,
            hasUpper, hasLower, hasDigit, hasSymbol,
            password.Length > 0 && (username.Length == 0 || password.IndexOf(username, StringComparison.OrdinalIgnoreCase) < 0),
            password.Length > 0 && password == repeatInput.text
        };

        for (int i = 0; i < rules.Count && i < ok.Length; i++)
        {
            Color color = ok[i] ? RuleOn : RuleOff;
            rules[i].dot.color = color;
            rules[i].text.color = ok[i] ? new Color(0.85f, 0.95f, 0.88f, 1f) : RuleOff;
        }
    }

    /// <summary>Invitado o con cuenta, y qué botones se pueden usar.</summary>
    private void Refresh()
    {
        string username = PlayerAccount.Username;
        bool hasAccount = username != null;

        stateText.text = hasAccount ? username : GameLanguage.Text("Invitado", "Guest");
        stateText.color = hasAccount ? CodeUI.LightBlue : Color.white;
        stateBar.color = hasAccount ? CodeUI.LightBlue : CodeUI.MutedText;

        sideInfo.text = hasAccount
            ? GameLanguage.Text("Tu récord y tus fantasmas te siguen a cualquier computadora donde entres con esta cuenta.",
                                "Your times and ghosts follow you to any computer where you sign in with this account.")
            : GameLanguage.Text("Tu récord queda solo en esta computadora. Con una cuenta lo tenés en cualquier lado.",
                                "Your times stay on this computer only. With an account they follow you anywhere.");

        sideNote.text = hasAccount
            ? GameLanguage.Text("Tu usuario no se muestra en la tabla: ahí sale tu nombre de la contrarreloj.",
                                "Your username isn't shown on the leaderboard: your time trial name is.")
            : GameLanguage.Text("La contraseña no se puede recuperar: anotala en un lugar seguro.",
                                "Passwords can't be recovered: keep yours somewhere safe.");

        guestForm.gameObject.SetActive(!hasAccount);
        signedForm.gameObject.SetActive(hasAccount);

        if (hasAccount)
            CodeUI.SetBox(status.rectTransform, FormX + 196f, 104f, FormRight - 150f - FormX - 204f, 28f);
        else
            ApplyMode();

        bool usable = PlayerAccount.IsReady && !IsBusy;
        CodeUI.SetUsable(primaryButton, usable);
        CodeUI.SetUsable(changeButton, usable);
        CodeUI.SetUsable(signOutButton, usable);
        signInTab.interactable = createTab.interactable = !IsBusy;
    }

    private void SetStatus(string text, bool error)
    {
        status.text = text;
        status.color = error ? ErrorColor : CodeUI.Warning;
    }

    // ------------------------------------------------------------------
    // Teclado: Tab pasa al campo siguiente (Shift+Tab, al anterior)
    // ------------------------------------------------------------------

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null || !keyboard.tabKey.wasPressedThisFrame || EventSystem.current == null)
            return;

        List<TMP_InputField> fields = VisibleFields();

        if (fields.Count == 0)
            return;

        GameObject selected = EventSystem.current.currentSelectedGameObject;
        int index = fields.FindIndex(f => f.gameObject == selected);
        int step = keyboard.shiftKey.isPressed ? -1 : 1;
        int next = index < 0 ? 0 : (index + step + fields.Count) % fields.Count;
        Select(fields[next]);
    }

    private List<TMP_InputField> VisibleFields()
    {
        var fields = new List<TMP_InputField>();
        TMP_InputField[] all = PlayerAccount.HasAccount
            ? new[] { currentInput, newPasswordInput, repeatNewInput }
            : new[] { userInput, passwordInput, repeatInput };

        foreach (TMP_InputField field in all)
        {
            if (field != null && field.gameObject.activeInHierarchy)
                fields.Add(field);
        }

        return fields;
    }

    private void FocusFirst()
    {
        List<TMP_InputField> fields = VisibleFields();

        if (fields.Count == 0)
            return;

        // Si ya escribió el usuario, va directo a la contraseña.
        TMP_InputField target = fields[0] == userInput && !string.IsNullOrEmpty(userInput.text) && fields.Count > 1 ? fields[1] : fields[0];
        Select(target);
    }

    private static void Select(TMP_InputField field)
    {
        if (EventSystem.current == null)
            return;

        EventSystem.current.SetSelectedGameObject(field.gameObject);
        field.ActivateInputField();
    }

    /// <summary>Vacía todos los campos de contraseña (el usuario queda).</summary>
    private void ClearPasswords()
    {
        foreach (TMP_InputField field in new[] { passwordInput, repeatInput, currentInput, newPasswordInput, repeatNewInput })
        {
            if (field != null)
                field.SetTextWithoutNotify("");
        }

        RefreshRules();
    }

    // ------------------------------------------------------------------
    // Acciones
    // ------------------------------------------------------------------

    private void Submit()
    {
        if (!IsOpen || PlayerAccount.HasAccount)
            return;

        if (mode == Mode.Create)
            CreateAccount();
        else
            SignIn();
    }

    private async void CreateAccount()
    {
        if (IsBusy || !PlayerAccount.IsReady)
            return;

        string username = userInput.text;
        string password = passwordInput.text;
        string repeat = repeatInput.text;
        ClearPasswords();

        busy = true;
        SetStatus(GameLanguage.Text("Creando la cuenta...", "Creating the account..."), false);
        Refresh();

        string error = await PlayerAccount.CreateAsync(username, password, repeat);
        password = repeat = null;

        if (this == null)
            return;

        if (error != null)
        {
            busy = false;
            SetStatus(error, true);
            Refresh();
            return;
        }

        // El nombre de la tabla queda guardado con la cuenta.
        await TimeTrialRecords.PublishNameAsync(TimeTrialRecords.LocalNameOr(SessionMenuUI.SavedName));

        if (this == null)
            return;

        busy = false;
        Finish();
    }

    private async void SignIn()
    {
        if (IsBusy || !PlayerAccount.IsReady)
            return;

        string username = userInput.text;
        string password = passwordInput.text;
        ClearPasswords();

        busy = true;
        SetStatus(GameLanguage.Text("Entrando...", "Signing in..."), false);
        Refresh();

        string error = await PlayerAccount.SignInAsync(username, password);
        password = null;

        if (this == null)
            return;

        if (error != null)
        {
            busy = false;
            SetStatus(error, true);
            Refresh();
            return;
        }

        // El nombre de la tabla de esa cuenta (si tiene uno guardado).
        string name = await TimeTrialRecords.LoadMyNameAsync();

        if (!string.IsNullOrEmpty(name))
            TimeTrialRecords.SetLocalName(name);

        if (this == null)
            return;

        busy = false;
        Finish();
    }

    /// <summary>Después de crear la cuenta o entrar: avisa del cambio y se cierra.</summary>
    private void Finish()
    {
        SetStatus("", false);
        mode = Mode.SignIn;
        ApplyMode();
        Refresh();
        Changed?.Invoke();
        Hide();
    }

    private async void ChangePassword()
    {
        if (IsBusy || !PlayerAccount.HasAccount)
            return;

        string current = currentInput.text;
        string password = newPasswordInput.text;
        string repeat = repeatNewInput.text;
        ClearPasswords();

        busy = true;
        SetStatus(GameLanguage.Text("Cambiando la contraseña...", "Changing the password..."), false);
        Refresh();

        string error = await PlayerAccount.ChangePasswordAsync(current, password, repeat);
        current = password = repeat = null;

        if (this == null)
            return;

        busy = false;
        SetStatus(error ?? GameLanguage.Text("Contraseña cambiada. Se cerró tu sesión en las otras computadoras.", "Password changed. You were signed out on other computers."), error != null);
        Refresh();
    }

    private void SignOut()
    {
        if (IsBusy)
            return;

        ClearPasswords();
        userInput.SetTextWithoutNotify("");
        PlayerAccount.SignOut();
        mode = Mode.SignIn;
        ApplyMode();
        SetStatus(GameLanguage.Text("Saliste de la cuenta. Ahora sos un invitado nuevo.", "Signed out. You're now a new guest."), false);
        Changed?.Invoke();

        // Vuelve a conectarse como un invitado nuevo.
        Connect();
    }
}
