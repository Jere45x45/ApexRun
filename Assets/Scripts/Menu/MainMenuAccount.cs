using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Botón de la cuenta en el menú principal (arriba a la derecha): dice si
/// jugás como invitado o con qué usuario, y abre el panel de la cuenta
/// (AccountPanelUI) encima del menú. Al abrir el menú se conecta a los
/// servicios en segundo plano, para mostrar la cuenta (sin internet, dice
/// "sin conexión"). Se arma solo en la escena que tiene MenuManager: no hay que
/// tocar la escena.
/// </summary>
public class MainMenuAccount : MonoBehaviour
{
    private Button button;
    private Image accent;
    private TMP_Text titleText;
    private TMP_Text nameText;
    private AccountPanelUI panel;
    private bool connecting;
    private bool offline;

    private void Build()
    {
        RectTransform parent = (RectTransform)transform;
        TMP_FontAsset font = FindMenuFont();

        // El botón, arriba a la derecha.
        RectTransform chip = CodeUI.NewRect("BotonCuenta", parent);
        chip.anchorMin = chip.anchorMax = chip.pivot = new Vector2(1f, 1f);
        chip.anchoredPosition = new Vector2(-16f, -16f);
        chip.sizeDelta = new Vector2(176f, 40f);

        Image background = CodeUI.AddImage(chip, CodeUI.BoxColor);
        button = chip.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.3f, 1.3f, 1.35f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.85f, 1f);
        button.colors = colors;
        button.onClick.AddListener(() => panel.Show());

        accent = CodeUI.AddImage(CodeUI.NewRect("Marca", chip), CodeUI.MutedText);
        CodeUI.SetBox((RectTransform)accent.transform, 0f, 0f, 4f, 40f);

        titleText = CodeUI.NewText("Titulo", chip, 9f, TextAlignmentOptions.Left, font);
        CodeUI.SetBox(titleText.rectTransform, 14f, 5f, 156f, 12f);
        titleText.color = CodeUI.MutedText;
        titleText.text = GameLanguage.Text("CUENTA", "ACCOUNT");

        nameText = CodeUI.NewText("Nombre", chip, 13f, TextAlignmentOptions.Left, font);
        CodeUI.SetBox(nameText.rectTransform, 14f, 17f, 156f, 18f);
        nameText.overflowMode = TextOverflowModes.Ellipsis;

        // El panel, encima de todo el menú.
        panel = AccountPanelUI.Create(parent, font, true);
        panel.Changed += Refresh;
    }

    private void Awake()
    {
        Build();
    }

    private void OnEnable()
    {
        PlayerAccount.Changed += Refresh;
        Refresh();
        ConnectInBackground();
    }

    private void OnDisable()
    {
        PlayerAccount.Changed -= Refresh;
    }

    /// <summary>Se conecta para saber si hay una cuenta guardada (sin bloquear el menú).</summary>
    private async void ConnectInBackground()
    {
        if (connecting || PlayerAccount.IsReady || GameSession.Instance == null)
            return;

        connecting = true;
        bool ok = await GameSession.Instance.EnsureSignedInAsync(SessionMenuUI.SavedName);

        if (ok)
            await PlayerAccount.RefreshAsync();

        connecting = false;
        offline = !ok;

        if (this != null)
            Refresh();
    }

    private void Refresh()
    {
        if (nameText == null)
            return;

        string username = PlayerAccount.Username;

        if (username != null)
        {
            nameText.text = username;
            accent.color = CodeUI.LightBlue;
        }
        else
        {
            nameText.text = PlayerAccount.IsReady
                ? GameLanguage.Text("Invitado", "Guest")
                : offline ? GameLanguage.Text("Sin conexión", "Offline") : GameLanguage.Text("Conectando...", "Connecting...");
            accent.color = CodeUI.MutedText;
        }
    }

    /// <summary>La fuente de los textos del menú (la de los botones).</summary>
    private TMP_FontAsset FindMenuFont()
    {
        foreach (TMP_Text text in GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.font != null)
                return text.font;
        }

        TMP_Text any = FindFirstObjectByType<TMP_Text>(FindObjectsInactive.Include);
        return any != null ? any.font : null;
    }

    // ------------------------------------------------------------------
    // Armado automático (en la escena del menú)
    // ------------------------------------------------------------------

    private static bool hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        if (!hooked)
        {
            SceneManager.sceneLoaded += (scene, mode) => InstallIn(scene);
            hooked = true;
        }

        InstallIn(SceneManager.GetActiveScene());
    }

    private static void InstallIn(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            MenuManager menu = root.GetComponentInChildren<MenuManager>(true);

            if (menu == null)
                continue;

            GameObject main = menu.GetPanel(MenuPanel.Main);

            if (main != null && main.GetComponent<MainMenuAccount>() == null)
                main.AddComponent<MainMenuAccount>();

            return;
        }
    }
}
