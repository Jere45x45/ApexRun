using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

/// <summary>
/// Cuenta del jugador en los servicios de Unity (la que usan la tabla de la
/// contrarreloj y el multijugador).
/// - Invitado: cuenta anónima, guardada solo en esta computadora (el editor y el
///   juego compilado cuentan como computadoras distintas).
/// - Con usuario y contraseña: la misma cuenta en cualquier computadora. Se crea
///   desde el invitado (CreateAsync: el invitado pasa a tener usuario, así sus
///   marcas quedan) o se entra a una que ya existe (SignInAsync).
///
/// Seguridad (revisado 11/11/2026):
/// - La contraseña va solo a Unity Authentication, por HTTPS (UnityWebRequest
///   valida el certificado; no hay que tocar eso). El juego NUNCA la guarda: ni en
///   PlayerPrefs, ni en archivos, ni en Cloud Save, ni en la tabla, ni en el log.
///   Lo único que queda en la computadora es el token de sesión que guarda Unity
///   (en PlayerPrefs): con él se vuelve a entrar sin escribir la contraseña, pero
///   no se puede sacar la contraseña de él. SignOut lo borra.
/// - El log solo lleva el código de error (nunca el usuario, la contraseña ni el
///   texto del servidor).
/// - Al entrar, el error es siempre "usuario o contraseña incorrectos" (no dice
///   cuál de los dos), para no confirmar qué usuarios existen.
/// - Después de varios intentos fallidos, el juego espera antes de dejar probar
///   de nuevo (cada vez más). Es una ayuda: el freno de verdad es el del servidor
///   (TooManyRequests), porque un juego modificado se puede saltear esto.
/// - Contraseña: las reglas de Unity (8 a 30, mayúscula, minúscula, número y
///   símbolo) más: que no contenga el usuario y que no sea de las más usadas.
///   Al crearla se escribe dos veces (no hay forma de recuperarla).
/// - El usuario es para entrar y no se muestra a los demás: la tabla muestra el
///   nombre de la contrarreloj y el multijugador el nombre del menú.
/// - Cambiar la contraseña (ChangePasswordAsync) cierra la sesión en las otras
///   computadoras (lo hace Unity).
/// - En C# no se puede borrar un string de la memoria: la contraseña dura lo
///   que tarda el pedido y después la junta el recolector. La interfaz vacía los
///   campos apenas se usan.
/// El proveedor "Username and Password" está habilitado en el proyecto de Unity
/// (Player Authentication), desde el 11/11/2026.
/// </summary>
public static class PlayerAccount
{
    public const int MinUsername = 3;
    public const int MaxUsername = 20;
    public const int MinPassword = 8;
    public const int MaxPassword = 30;

    // Intentos fallidos seguidos antes de hacer esperar, y la espera (se duplica, hasta el máximo).
    private const int FreeAttempts = 3;
    private const float FirstLockSeconds = 15f;
    private const float MaxLockSeconds = 300f;

    // Contraseñas que cumplen las reglas pero son de las primeras que se prueban.
    private static readonly string[] CommonPasswords =
    {
        "password1!", "password123!", "passw0rd!", "p@ssw0rd", "p@ssword1", "p@ssw0rd1", "qwerty123!", "qwerty1!",
        "abc12345!", "abc123456!", "12345678a!", "1234567a!", "a1234567!", "admin123!", "welcome1!", "letmein1!",
        "iloveyou1!", "football1!", "monkey123!", "dragon123!", "apexrun1!", "apexrun123!", "apexrun2026!",
        "contraseña1!", "contrasena1!", "contraseña123!", "contrasena123!", "argentina1!", "boca1234!", "river1234!"
    };

    /// <summary>Cambió la cuenta (se creó, se entró a otra o se salió).</summary>
    public static event Action Changed;

    private static int failedAttempts;
    private static float lockedUntil;

    public static bool IsReady =>
        UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn;

    /// <summary>Usuario de la cuenta, o null si es invitado (o no hay conexión). Solo para mostrarle al dueño.</summary>
    public static string Username
    {
        get
        {
            if (!IsReady)
                return null;

            PlayerInfo info = AuthenticationService.Instance.PlayerInfo;
            return info != null && !string.IsNullOrEmpty(info.Username) ? info.Username : null;
        }
    }

    public static bool HasAccount => Username != null;

    /// <summary>Segundos que faltan para poder volver a probar una contraseña (0 = ya se puede).</summary>
    public static float LockSecondsLeft => Mathf.Max(0f, lockedUntil - Time.realtimeSinceStartup);

    // ------------------------------------------------------------------
    // Reglas
    // ------------------------------------------------------------------

    /// <summary>True si el carácter puede ir en un usuario (letras sin acento, números y . - @ _).</summary>
    public static bool IsUsernameChar(char c) =>
        c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '.' || c == '-' || c == '@' || c == '_';

    /// <summary>null si el usuario sirve; si no, por qué.</summary>
    public static string CheckUsername(string username)
    {
        username = username != null ? username.Trim() : "";

        if (username.Length < MinUsername || username.Length > MaxUsername)
            return GameLanguage.Text("El usuario tiene que tener de 3 a 20 caracteres.", "The username must be 3 to 20 characters.");

        foreach (char c in username)
        {
            if (!IsUsernameChar(c))
                return GameLanguage.Text("El usuario solo puede tener letras sin acento, números y . - @ _", "The username can only have letters, numbers and . - @ _");
        }

        return null;
    }

    /// <summary>null si la contraseña sirve para ese usuario; si no, por qué.</summary>
    public static string CheckPassword(string password, string username)
    {
        password = password ?? "";

        if (password.Length < MinPassword || password.Length > MaxPassword)
            return GameLanguage.Text("La contraseña tiene que tener de 8 a 30 caracteres.", "The password must be 8 to 30 characters.");

        bool lower = false, upper = false, digit = false, symbol = false;

        foreach (char c in password)
        {
            if (char.IsWhiteSpace(c))
                return GameLanguage.Text("La contraseña no puede tener espacios.", "The password can't have spaces.");

            if (char.IsLower(c)) lower = true;
            else if (char.IsUpper(c)) upper = true;
            else if (char.IsDigit(c)) digit = true;
            else symbol = true;
        }

        if (!lower || !upper || !digit || !symbol)
            return GameLanguage.Text("La contraseña necesita una minúscula, una mayúscula, un número y un símbolo.", "The password needs a lowercase letter, an uppercase letter, a number and a symbol.");

        string user = username != null ? username.Trim() : "";

        if (user.Length >= MinUsername && password.IndexOf(user, StringComparison.OrdinalIgnoreCase) >= 0)
            return GameLanguage.Text("La contraseña no puede tener tu usuario adentro.", "The password can't contain your username.");

        string lowered = password.ToLowerInvariant();

        foreach (string common in CommonPasswords)
        {
            if (lowered == common)
                return GameLanguage.Text("Esa contraseña es de las más usadas: elegí otra.", "That password is too common, pick another.");
        }

        return null;
    }

    // ------------------------------------------------------------------
    // Acciones
    // ------------------------------------------------------------------

    /// <summary>Trae del servidor los datos de la cuenta (el usuario).</summary>
    public static async Task RefreshAsync()
    {
        if (!IsReady)
            return;

        try
        {
            await AuthenticationService.Instance.GetPlayerInfoAsync();
        }
        catch (Exception)
        {
            Debug.LogWarning("PlayerAccount: no se pudieron leer los datos de la cuenta.");
        }
    }

    /// <summary>
    /// Le pone usuario y contraseña al invitado de ahora (sus marcas quedan en la
    /// cuenta). repeat: la contraseña escrita otra vez. Devuelve null si salió
    /// bien, o el error para mostrar.
    /// </summary>
    public static async Task<string> CreateAsync(string username, string password, string repeat)
    {
        username = username != null ? username.Trim() : "";

        string problem = CheckUsername(username) ?? CheckPassword(password, username);

        if (problem != null)
            return problem;

        if (password != repeat)
            return GameLanguage.Text("Las dos contraseñas no son iguales.", "The passwords don't match.");

        if (!IsReady)
            return GameLanguage.Text("Sin conexión con el servidor.", "Can't reach the server.");

        if (HasAccount)
            return GameLanguage.Text("Ya tenés una cuenta.", "You already have an account.");

        try
        {
            await AuthenticationService.Instance.AddUsernamePasswordAsync(username, password);
            await RefreshAsync();
            Changed?.Invoke();
            return null;
        }
        catch (AuthenticationException exception)
        {
            return Describe(exception.ErrorCode, Operation.Create, exception.Message);
        }
        catch (RequestFailedException exception)
        {
            return Describe(exception.ErrorCode, Operation.Create, exception.Message);
        }
    }

    /// <summary>
    /// Entra a una cuenta que ya existe. Si falla, vuelve a la cuenta que había
    /// (el invitado no se pierde). Devuelve null si salió bien, o el error.
    /// </summary>
    public static async Task<string> SignInAsync(string username, string password)
    {
        username = username != null ? username.Trim() : "";

        if (username.Length == 0 || string.IsNullOrEmpty(password))
            return GameLanguage.Text("Escribí tu usuario y tu contraseña.", "Type your username and password.");

        string locked = LockMessage();

        if (locked != null)
            return locked;

        if (UnityServices.State != ServicesInitializationState.Initialized)
            return GameLanguage.Text("Sin conexión con el servidor.", "Can't reach the server.");

        // Un usuario que no puede existir no se manda (igual se responde lo mismo).
        if (CheckUsername(username) != null || password.Length > MaxPassword)
            return Failed(Operation.SignIn);

        // Sale sin borrar la sesión guardada: si la contraseña está mal, se vuelve a ella.
        if (AuthenticationService.Instance.IsSignedIn)
            AuthenticationService.Instance.SignOut(false);

        try
        {
            await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(username, password);
            failedAttempts = 0;
            await RefreshAsync();
            Changed?.Invoke();
            return null;
        }
        catch (AuthenticationException exception)
        {
            await RestoreAsync();
            return Describe(exception.ErrorCode, Operation.SignIn, exception.Message);
        }
        catch (RequestFailedException exception)
        {
            await RestoreAsync();
            return Describe(exception.ErrorCode, Operation.SignIn, exception.Message);
        }
    }

    /// <summary>
    /// Cambia la contraseña (hay que saber la de ahora). Unity cierra la sesión en
    /// las otras computadoras. Devuelve null si salió bien, o el error.
    /// </summary>
    public static async Task<string> ChangePasswordAsync(string current, string password, string repeat)
    {
        if (!HasAccount)
            return GameLanguage.Text("No estás en una cuenta.", "You're not signed in to an account.");

        if (string.IsNullOrEmpty(current))
            return GameLanguage.Text("Escribí tu contraseña de ahora.", "Type your current password.");

        string problem = CheckPassword(password, Username);

        if (problem != null)
            return problem;

        if (password != repeat)
            return GameLanguage.Text("Las dos contraseñas nuevas no son iguales.", "The new passwords don't match.");

        if (password == current)
            return GameLanguage.Text("La contraseña nueva tiene que ser distinta.", "The new password must be different.");

        string locked = LockMessage();

        if (locked != null)
            return locked;

        try
        {
            await AuthenticationService.Instance.UpdatePasswordAsync(current, password);
            failedAttempts = 0;
            return null;
        }
        catch (AuthenticationException exception)
        {
            return Describe(exception.ErrorCode, Operation.ChangePassword, exception.Message);
        }
        catch (RequestFailedException exception)
        {
            return Describe(exception.ErrorCode, Operation.ChangePassword, exception.Message);
        }
    }

    /// <summary>
    /// Sale de la cuenta en esta computadora y borra la sesión guardada (en una
    /// computadora compartida, nadie más puede entrar a tu cuenta). La próxima
    /// vez que se conecte, es un invitado nuevo.
    /// </summary>
    public static void SignOut()
    {
        if (UnityServices.State == ServicesInitializationState.Initialized)
        {
            if (AuthenticationService.Instance.IsSignedIn)
                AuthenticationService.Instance.SignOut(true);
            else
                AuthenticationService.Instance.ClearSessionToken();
        }

        Changed?.Invoke();
    }

    // ------------------------------------------------------------------
    // Ayudas
    // ------------------------------------------------------------------

    private enum Operation { Create, SignIn, ChangePassword }

    /// <summary>Vuelve a la cuenta que estaba guardada (después de un intento fallido).</summary>
    private static async Task RestoreAsync()
    {
        if (AuthenticationService.Instance.IsSignedIn)
            return;

        try
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
        catch (Exception)
        {
            Debug.LogWarning("PlayerAccount: no se pudo volver a la cuenta anterior.");
        }
    }

    private static string LockMessage()
    {
        float left = LockSecondsLeft;

        return left > 0f
            ? GameLanguage.Text("Demasiados intentos: esperá " + Mathf.CeilToInt(left) + " s.", "Too many attempts: wait " + Mathf.CeilToInt(left) + " s.")
            : null;
    }

    /// <summary>Un intento con la contraseña equivocada: cuenta para la espera.</summary>
    private static string Failed(Operation operation)
    {
        failedAttempts++;

        if (failedAttempts >= FreeAttempts)
        {
            float wait = Mathf.Min(MaxLockSeconds, FirstLockSeconds * Mathf.Pow(2f, failedAttempts - FreeAttempts));
            lockedUntil = Time.realtimeSinceStartup + wait;
        }

        return operation == Operation.ChangePassword
            ? GameLanguage.Text("La contraseña de ahora no es correcta.", "Your current password is wrong.")
            : GameLanguage.Text("Usuario o contraseña incorrectos.", "Wrong username or password.");
    }

    private static bool IsConnectionProblem(int code) =>
        code == CommonErrorCodes.TransportError || code == CommonErrorCodes.Timeout || code == CommonErrorCodes.ServiceUnavailable;

    private static string Describe(int code, Operation operation, string detail)
    {
        // Solo el código: el texto del servidor podría tener datos de la cuenta.
        Debug.LogWarning("PlayerAccount: falló " + operation + " (código " + code + ").");

        if (IsConnectionProblem(code))
            return GameLanguage.Text("Sin conexión con el servidor.", "Can't reach the server.");

        if (code == CommonErrorCodes.TooManyRequests)
        {
            lockedUntil = Mathf.Max(lockedUntil, Time.realtimeSinceStartup + 60f);
            return GameLanguage.Text("Demasiados intentos: probá en un rato.", "Too many attempts, try again later.");
        }

        if (code == AuthenticationErrorCodes.BannedUser)
            return GameLanguage.Text("Esta cuenta está bloqueada.", "This account is banned.");

        if (code == AuthenticationErrorCodes.InvalidProvider)
            return GameLanguage.Text("El juego no tiene habilitadas las cuentas.", "Accounts aren't enabled for this game.");

        if (operation != Operation.Create)
            return Failed(operation);

        if (code == CommonErrorCodes.Conflict || code == AuthenticationErrorCodes.AccountAlreadyLinked ||
            detail != null && detail.IndexOf("exist", StringComparison.OrdinalIgnoreCase) >= 0)
            return GameLanguage.Text("Ese usuario ya existe: elegí otro.", "That username is taken.");

        if (code == AuthenticationErrorCodes.AccountLinkLimitExceeded)
            return GameLanguage.Text("Esta cuenta ya tiene usuario.", "This account already has a username.");

        if (code == AuthenticationErrorCodes.InvalidParameters || code == CommonErrorCodes.InvalidRequest)
            return GameLanguage.Text("El usuario o la contraseña no sirven: revisá las reglas.", "Invalid username or password, check the rules.");

        return GameLanguage.Text("No se pudo crear la cuenta.", "Couldn't create the account.");
    }
}
