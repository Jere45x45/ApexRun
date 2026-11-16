using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Panel de la cuenta (PlayerAccount): crear una cuenta con usuario y contraseña
/// (el invitado de ahora pasa a tenerla, con sus marcas), entrar a una que ya
/// existe, cambiar la contraseña o salir. Se arma por código (Create) y lo
/// abren la tabla de la contrarreloj (botón CUENTA) y el menú principal
/// (MainMenuAccount). Si todavía no hay conexión con los servicios, se conecta al abrirse.
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

    private TMP_FontAsset font;
    private RectTransform blocker;
    private RectTransform panel;

    private TMP_Text info;
    private RectTransform guestForm;
    private RectTransform signedForm;
    private TMP_InputField userInput;
    private TMP_InputField passwordInput;
    private TMP_InputField repeatInput;
    private TMP_InputField currentInput;
    private TMP_InputField newPasswordInput;
    private TMP_InputField repeatNewInput;
    private Button createButton;
    private Button signInButton;
    private Button changeButton;
    private Button signOutButton;
    private TMP_Text note;
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
    /// Arma el panel (cerrado). modal: centrado y con un fondo oscuro que tapa
    /// todo el padre (menú principal); si no, ocupa los 736 × 188 px de arriba a
    /// la izquierda del padre (encima de la tabla).
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
            Image dim = CodeUI.AddImage(holder, new Color(0f, 0f, 0f, 0.7f));
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
        Root.SetAsLastSibling();
        status.text = "";
        Refresh();

        if (!PlayerAccount.IsReady)
            Connect();
    }

    /// <summary>Cierra el panel (si no está esperando al servidor). False si no se pudo.</summary>
    public bool Hide()
    {
        if (busy)
            return false;

        ClearPasswords();
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
            status.text = connectingText;

        Refresh();

        bool ok = await GameSession.Instance.EnsureSignedInAsync(SessionMenuUI.SavedName);

        if (ok)
            await PlayerAccount.RefreshAsync();

        connecting = false;

        if (this == null)
            return;

        if (!ok)
            status.text = GameLanguage.Text("Sin conexión con el servidor.", "Can't reach the server.");
        else if (status.text == connectingText)
            status.text = "";

        Refresh();
    }

    // ------------------------------------------------------------------
    // Armado
    // ------------------------------------------------------------------

    private void Build()
    {
        CodeUI.AddImage(panel, CodeUI.PanelColor).raycastTarget = true;

        Image accent = CodeUI.AddImage(CodeUI.NewRect("Acento", panel), CodeUI.AccentRed);
        CodeUI.SetBox((RectTransform)accent.transform, 0f, 0f, Width, 3f);

        TMP_Text title = CodeUI.NewText("Titulo", panel, 14f, TextAlignmentOptions.Left, font);
        CodeUI.SetBox(title.rectTransform, 14f, 8f, 400f, 20f);
        title.text = GameLanguage.Text("CUENTA", "ACCOUNT");

        CodeUI.NewButton("Cerrar", panel, GameLanguage.Text("CERRAR", "CLOSE"), 650f, 8f, 74f, 18f, () => Hide(), font);

        info = CodeUI.NewText("Info", panel, 10f, TextAlignmentOptions.TopLeft, font);
        CodeUI.SetBox(info.rectTransform, 14f, 29f, 708f, 30f);
        info.textWrappingMode = TextWrappingModes.Normal;
        info.color = new Color(0.85f, 0.86f, 0.9f, 1f);

        // Invitado: usuario, contraseña dos veces, CREAR CUENTA y ENTRAR.
        guestForm = CodeUI.NewRect("Invitado", panel);
        CodeUI.SetBox(guestForm, 0f, 0f, Width, Height);

        userInput = Field(guestForm, 14f, GameLanguage.Text("USUARIO", "USERNAME"), GameLanguage.Text("Tu usuario", "Your username"), PlayerAccount.MaxUsername, false);
        userInput.onValidateInput += (text, index, c) => PlayerAccount.IsUsernameChar(c) ? c : '\0';
        passwordInput = Field(guestForm, 222f, GameLanguage.Text("CONTRASEÑA", "PASSWORD"), GameLanguage.Text("Tu contraseña", "Your password"), PlayerAccount.MaxPassword, true);
        repeatInput = Field(guestForm, 430f, GameLanguage.Text("REPETIR (SOLO PARA CREAR)", "REPEAT (ONLY TO CREATE)"), GameLanguage.Text("Otra vez", "Again"), PlayerAccount.MaxPassword, true);

        createButton = CodeUI.NewButton("Crear", guestForm, GameLanguage.Text("CREAR CUENTA", "CREATE ACCOUNT"), 14f, 118f, 150f, 22f, CreateAccount, font);
        createButton.GetComponent<Image>().color = CodeUI.AccentRed;
        signInButton = CodeUI.NewButton("Entrar", guestForm, GameLanguage.Text("ENTRAR", "SIGN IN"), 172f, 118f, 110f, 22f, SignIn, font);

        // Con cuenta: cambiar la contraseña y salir.
        signedForm = CodeUI.NewRect("ConCuenta", panel);
        CodeUI.SetBox(signedForm, 0f, 0f, Width, Height);

        currentInput = Field(signedForm, 14f, GameLanguage.Text("CONTRASEÑA DE AHORA", "CURRENT PASSWORD"), "", PlayerAccount.MaxPassword, true);
        newPasswordInput = Field(signedForm, 222f, GameLanguage.Text("CONTRASEÑA NUEVA", "NEW PASSWORD"), "", PlayerAccount.MaxPassword, true);
        repeatNewInput = Field(signedForm, 430f, GameLanguage.Text("REPETIR LA NUEVA", "REPEAT NEW"), "", PlayerAccount.MaxPassword, true);

        changeButton = CodeUI.NewButton("Cambiar", signedForm, GameLanguage.Text("CAMBIAR CONTRASEÑA", "CHANGE PASSWORD"), 14f, 118f, 170f, 22f, ChangePassword, font);
        signOutButton = CodeUI.NewButton("Salir", signedForm, GameLanguage.Text("SALIR DE LA CUENTA", "SIGN OUT"), 192f, 118f, 160f, 22f, SignOut, font);

        TMP_Text rules = CodeUI.NewText("Reglas", panel, 8.5f, TextAlignmentOptions.TopLeft, font);
        CodeUI.SetBox(rules.rectTransform, 14f, 101f, 708f, 14f);
        rules.color = CodeUI.MutedText;
        rules.text = GameLanguage.Text(
            "Usuario: 3 a 20 (letras, números y . - @ _). Contraseña: 8 a 30, con mayúscula, minúscula, número y símbolo, sin tu usuario.",
            "Username: 3 to 20 (letters, numbers and . - @ _). Password: 8 to 30, upper and lower case, a number and a symbol, without your username.");

        status = CodeUI.NewText("Estado", panel, 10f, TextAlignmentOptions.Left, font);
        CodeUI.SetBox(status.rectTransform, 362f, 118f, 360f, 22f);
        status.color = CodeUI.Warning;
        status.textWrappingMode = TextWrappingModes.Normal;
        status.enableAutoSizing = true;
        status.fontSizeMin = 7f;
        status.fontSizeMax = 10f;

        note = CodeUI.NewText("Nota", panel, 8.5f, TextAlignmentOptions.TopLeft, font);
        CodeUI.SetBox(note.rectTransform, 14f, 146f, 708f, 38f);
        note.textWrappingMode = TextWrappingModes.Normal;
        note.color = CodeUI.MutedText;
    }

    /// <summary>Título chico y campo de texto (una de las tres columnas).</summary>
    private TMP_InputField Field(RectTransform parent, float x, string title, string hint, int limit, bool password)
    {
        TMP_Text label = CodeUI.NewText("Titulo", parent, 9f, TextAlignmentOptions.Left, font);
        CodeUI.SetBox(label.rectTransform, x, 62f, 200f, 12f);
        label.color = CodeUI.MutedText;
        label.text = title;

        return CodeUI.NewInput(parent, x, 75f, 200f, 22f, hint, limit, password, null, font);
    }

    /// <summary>Vacía todos los campos de contraseña (el usuario queda).</summary>
    private void ClearPasswords()
    {
        foreach (TMP_InputField field in new[] { passwordInput, repeatInput, currentInput, newPasswordInput, repeatNewInput })
        {
            if (field != null)
                field.SetTextWithoutNotify("");
        }
    }

    /// <summary>Invitado o con cuenta, y qué botones se pueden usar.</summary>
    private void Refresh()
    {
        string username = PlayerAccount.Username;
        bool hasAccount = username != null;

        info.text = hasAccount
            ? GameLanguage.Text("Entraste como <b>" + username + "</b>. Tu récord y tus fantasmas te siguen a cualquier computadora donde entres con esta cuenta.",
                                "Signed in as <b>" + username + "</b>. Your times and ghosts follow you to any computer where you sign in.")
            : GameLanguage.Text("Ahora jugás como <b>invitado</b>: tu récord queda solo en esta computadora (el editor y el juego compilado cuentan como computadoras distintas). Con una cuenta lo tenés en cualquier lado.",
                                "You're playing as a <b>guest</b>: your times stay on this computer only. With an account they follow you anywhere.");

        note.text = hasAccount
            ? GameLanguage.Text("Cambiar la contraseña cierra tu sesión en las otras computadoras. En una computadora que no es tuya, salí de la cuenta al terminar. " +
                                "Tu usuario no se muestra en la tabla: ahí sale tu nombre de la contrarreloj.",
                                "Changing the password signs you out on other computers. On a shared computer, sign out when you're done. " +
                                "Your username isn't shown on the leaderboard: your time trial name is.")
            : GameLanguage.Text("CREAR CUENTA: tus marcas de ahora pasan a la cuenta nueva. ENTRAR: a una cuenta que ya tenés (las de este invitado quedan como otro piloto). " +
                                "Usá una contraseña que no uses en otro lado y anotala: no se puede recuperar. El juego no la guarda.",
                                "CREATE ACCOUNT: your current times move to the new account. SIGN IN: to an account you already have (this guest's times stay as another driver). " +
                                "Use a password you don't use anywhere else and write it down: it can't be recovered. The game never stores it.");

        guestForm.gameObject.SetActive(!hasAccount);
        signedForm.gameObject.SetActive(hasAccount);

        bool usable = PlayerAccount.IsReady && !IsBusy;
        CodeUI.SetUsable(createButton, usable);
        CodeUI.SetUsable(signInButton, usable);
        CodeUI.SetUsable(changeButton, usable);
        CodeUI.SetUsable(signOutButton, usable);
    }

    // ------------------------------------------------------------------
    // Acciones
    // ------------------------------------------------------------------

    private async void CreateAccount()
    {
        if (IsBusy)
            return;

        string username = userInput.text;
        string password = passwordInput.text;
        string repeat = repeatInput.text;
        ClearPasswords();

        busy = true;
        status.text = GameLanguage.Text("Creando la cuenta...", "Creating the account...");
        Refresh();

        string error = await PlayerAccount.CreateAsync(username, password, repeat);
        password = repeat = null;

        if (this == null)
            return;

        if (error != null)
        {
            busy = false;
            status.text = error;
            Refresh();
            return;
        }

        // El nombre de la tabla queda guardado con la cuenta.
        await TimeTrialRecords.PublishNameAsync(TimeTrialRecords.LocalNameOr(SessionMenuUI.SavedName));

        if (this == null)
            return;

        busy = false;
        status.text = GameLanguage.Text("¡Cuenta creada! Tus marcas ya son de la cuenta.", "Account created! Your times are now in it.");
        Refresh();
        Changed?.Invoke();
    }

    private async void SignIn()
    {
        if (IsBusy)
            return;

        string username = userInput.text;
        string password = passwordInput.text;
        ClearPasswords();

        busy = true;
        status.text = GameLanguage.Text("Entrando...", "Signing in...");
        Refresh();

        string error = await PlayerAccount.SignInAsync(username, password);
        password = null;

        if (this == null)
            return;

        if (error != null)
        {
            busy = false;
            status.text = error;
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
        status.text = GameLanguage.Text("¡Listo! Ya estás en tu cuenta.", "Done! You're signed in.");
        Refresh();
        Changed?.Invoke();
    }

    private async void ChangePassword()
    {
        if (IsBusy)
            return;

        string current = currentInput.text;
        string password = newPasswordInput.text;
        string repeat = repeatNewInput.text;
        ClearPasswords();

        busy = true;
        status.text = GameLanguage.Text("Cambiando la contraseña...", "Changing the password...");
        Refresh();

        string error = await PlayerAccount.ChangePasswordAsync(current, password, repeat);
        current = password = repeat = null;

        if (this == null)
            return;

        busy = false;
        status.text = error ?? GameLanguage.Text("Contraseña cambiada. Se cerró tu sesión en las otras computadoras.", "Password changed. You were signed out on other computers.");
        Refresh();
    }

    private void SignOut()
    {
        if (IsBusy)
            return;

        ClearPasswords();
        userInput.SetTextWithoutNotify("");
        PlayerAccount.SignOut();
        status.text = GameLanguage.Text("Saliste de la cuenta y se borró la sesión de esta computadora. Ahora sos un invitado nuevo.",
                                        "Signed out and this computer's session was deleted. You're now a new guest.");
        Changed?.Invoke();

        // Vuelve a conectarse como un invitado nuevo.
        Connect();
    }
}
