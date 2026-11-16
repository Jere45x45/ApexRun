using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Boxes en el HUD del jugador (PitStops):
/// - En la calle de boxes, arriba al centro: el limitador y cuál es tu box.
/// - Quieto en tu box: las ruedas para elegir. Con 1, 2, 3 o 4 (las del
///   catálogo, en ese orden), o moviéndote con ←/→ (o A/D) y Enter, o con un
///   clic (el mouse aparece al moverlo). Las que tenés puestas dicen
///   "puestas": elegirlas es cambiar por otras nuevas iguales (más rápido).
/// - Mientras te las cambian: una barra con lo que falta.
/// - Con parada obligatoria, abajo del cartel: si ya paraste o no.
/// Se arma solo al cargar una escena con RaceHUD y calle de boxes.
/// </summary>
public class PitStopHUD : MonoBehaviour
{
    [SerializeField] private RaceHUD hud;

    [SerializeField] private Color panelColor = new Color(0.06f, 0.06f, 0.08f, 0.82f);
    [SerializeField] private Color optionColor = new Color(0.18f, 0.18f, 0.21f, 0.95f);
    [SerializeField] private Color selectedColor = new Color(0.62f, 0f, 0f, 1f);
    [SerializeField] private Color accentColor = new Color(1f, 0.82f, 0.1f, 1f);

    private const int MaxOptions = 9;

    private static readonly FieldInfo HudKartField =
        typeof(RaceHUD).GetField("playerKart", BindingFlags.Instance | BindingFlags.NonPublic);

    private GameObject banner;
    private TMP_Text bannerText;
    private TMP_Text mandatoryText;

    private GameObject choicePanel;
    private TMP_Text choiceTitle;
    private TMP_Text choiceHint;
    private readonly List<Image> optionBackgrounds = new List<Image>();
    private readonly List<TMP_Text> optionTexts = new List<TMP_Text>();
    private readonly List<Image> optionIcons = new List<Image>();
    private readonly List<GameObject> optionObjects = new List<GameObject>();

    private GameObject workPanel;
    private TMP_Text workText;
    private RectTransform workFill;

    private TMP_FontAsset font;
    private TMP_FontAsset boldFont;

    private Rigidbody kartBody;
    private KartBehaviour kart;
    private List<WheelData> options = new List<WheelData>();
    private int selected;
    private PitStops.StopState shownState = (PitStops.StopState)(-1);
    private string shownBanner;
    private string shownMandatory;

    private void Update()
    {
        PitStops pits = PitStops.Instance;
        KartBehaviour current = FindKart();

        if (pits == null || pits.Lane == null || current == null)
        {
            SetActive(banner, false);
            SetActive(choicePanel, false);
            SetActive(workPanel, false);
            SetActive(mandatoryText != null ? mandatoryText.gameObject : null, false);
            return;
        }

        PitStops.StopState state = pits.GetState(current);
        bool inLane = pits.IsInLane(current);

        UpdateBanner(pits, current, state, inLane);
        UpdateMandatory(pits);

        if (state != shownState)
        {
            shownState = state;

            if (state == PitStops.StopState.Choosing)
                OpenChoice(pits, current);
        }

        SetActive(choicePanel, state == PitStops.StopState.Choosing);
        SetActive(workPanel, state == PitStops.StopState.Working);

        if (state == PitStops.StopState.Choosing)
        {
            // Los números eligen las ruedas: la cámara no tiene que cambiar.
            KartCameraController.BlockNumberKeys();
            ReadChoiceKeys(pits, current);
        }
        else if (state == PitStops.StopState.Working)
            UpdateWork(pits, current);
    }

    private KartBehaviour FindKart()
    {
        if (hud == null)
            hud = FindFirstObjectByType<RaceHUD>();

        Rigidbody body = hud != null && HudKartField != null ? HudKartField.GetValue(hud) as Rigidbody : null;

        if (body != kartBody)
        {
            kartBody = body;
            kart = body != null ? body.GetComponent<KartBehaviour>() : null;
        }

        return kart;
    }

    // ------------------------------------------------------------------
    // Cartel
    // ------------------------------------------------------------------

    private void UpdateBanner(PitStops pits, KartBehaviour current, PitStops.StopState state, bool inLane)
    {
        bool show = inLane || state != PitStops.StopState.None;
        SetActive(banner, show);

        if (!show)
            return;

        int box = pits.GetBox(current) + 1;
        string limiter = GameLanguage.Text("LIMITADOR ", "PIT LIMITER ") + Mathf.RoundToInt(pits.Lane.SpeedLimitKmh) + " km/h";
        string line;

        switch (state)
        {
            case PitStops.StopState.Choosing:
                line = GameLanguage.Text("Elegí las ruedas", "Choose your tyres");
                break;

            case PitStops.StopState.Working:
                line = GameLanguage.Text("Cambiando ruedas...", "Changing tyres...");
                break;

            case PitStops.StopState.Done:
                // En la qualy todos arrancan en su box ya "parados".
                line = RaceSession.IsQualifying && current.GetComponent<Rigidbody>().linearVelocity.sqrMagnitude < 1f
                    ? GameLanguage.Text("Salí cuando quieras", "Leave whenever you want")
                    : GameLanguage.Text("¡Listo! Salí", "Done! Go");
                break;

            default:
                line = GameLanguage.Text("Frená a 0 en tu box: el ", "Stop at 0 in your box: number ") + "<color=#FFD11A>" + box + "</color>";
                break;
        }

        string text = "<b>BOXES</b>  ·  " + limiter + "\n<size=80%>" + line + "</size>";

        if (text != shownBanner)
        {
            shownBanner = text;
            bannerText.text = text;
        }
    }

    private void UpdateMandatory(PitStops pits)
    {
        bool show = pits.StopIsMandatory && kartBody != null;
        SetActive(mandatoryText != null ? mandatoryText.gameObject : null, show);

        if (!show)
            return;

        bool done = pits.GetStops(kartBody) > 0;
        string text = GameLanguage.Text("PARADA OBLIGATORIA: ", "MANDATORY PIT STOP: ") +
                      (done ? "<color=#4CD964>" + GameLanguage.Text("HECHA", "DONE") + "</color>"
                            : "<color=#FFB01F>" + GameLanguage.Text("PENDIENTE", "PENDING") + "</color>");

        if (text != shownMandatory)
        {
            shownMandatory = text;
            mandatoryText.text = text;
        }
    }

    // ------------------------------------------------------------------
    // Elegir ruedas
    // ------------------------------------------------------------------

    private void OpenChoice(PitStops pits, KartBehaviour current)
    {
        options = pits.GetWheelOptions(current);
        WheelData installed = PitStops.GetCurrentWheels(current);

        selected = Mathf.Max(0, options.IndexOf(installed));

        for (int i = 0; i < optionObjects.Count; i++)
        {
            bool used = i < options.Count;
            optionObjects[i].SetActive(used);

            if (!used)
                continue;

            WheelData wheels = options[i];
            string name = wheels.DisplayName;

            if (string.IsNullOrWhiteSpace(name))
                name = wheels.name;

            optionTexts[i].text = "<size=130%><b>" + (i + 1) + "</b></size>\n" + name +
                                  (wheels == installed ? "\n<size=75%><color=#BBBBBB>" + GameLanguage.Text("(puestas)", "(fitted)") + "</color></size>" : "");

            optionIcons[i].sprite = wheels.icon;
            optionIcons[i].gameObject.SetActive(wheels.icon != null);
        }

        choiceTitle.text = GameLanguage.Text("ELEGÍ LAS RUEDAS", "CHOOSE YOUR TYRES");
        choiceHint.text = GameLanguage.Text("1 - " + options.Count + "   ·   ← → y Enter   ·   clic", "1 - " + options.Count + "   ·   ← → and Enter   ·   click");

        PaintSelection();
    }

    private void ReadChoiceKeys(PitStops pits, KartBehaviour current)
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null || options.Count == 0)
            return;

        for (int i = 0; i < Mathf.Min(options.Count, MaxOptions); i++)
        {
            if (keyboard[Key.Digit1 + i].wasPressedThisFrame || keyboard[Key.Numpad1 + i].wasPressedThisFrame)
            {
                Choose(i);
                return;
            }
        }

        if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame)
        {
            selected = (selected - 1 + options.Count) % options.Count;
            PaintSelection();
        }

        if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame)
        {
            selected = (selected + 1) % options.Count;
            PaintSelection();
        }

        if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            Choose(selected);
    }

    private void Choose(int index)
    {
        if (index < 0 || index >= options.Count || PitStops.Instance == null || kart == null)
            return;

        selected = index;
        PaintSelection();
        PitStops.Instance.ChooseWheels(kart, options[index]);
    }

    private void PaintSelection()
    {
        for (int i = 0; i < optionBackgrounds.Count; i++)
            optionBackgrounds[i].color = i == selected ? selectedColor : optionColor;
    }

    private void UpdateWork(PitStops pits, KartBehaviour current)
    {
        float progress = pits.GetWorkProgress(current);
        workFill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);

        WheelData wheels = pits.GetChosenWheels(current);
        string name = wheels != null ? (string.IsNullOrWhiteSpace(wheels.DisplayName) ? wheels.name : wheels.DisplayName) : "";

        workText.text = GameLanguage.Text("CAMBIANDO RUEDAS", "CHANGING TYRES") + "  ·  " + name + "  ·  " +
                        pits.GetWorkRemaining(current).ToString("0.0", GameLanguage.IsEnglish ? CultureInfo.InvariantCulture : new CultureInfo("es-AR")) + " s";
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }

    // ------------------------------------------------------------------
    // Armado automático (así no hace falta tocar la escena)
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

        RaceHUD raceHud = null;
        bool hasLane = false;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (raceHud == null)
                raceHud = root.GetComponentInChildren<RaceHUD>(true);

            if (root.GetComponentInChildren<PitLane>() != null)
                hasLane = true;
        }

        if (raceHud == null || !hasLane)
            return;

        Canvas canvas = raceHud.GetComponentInParent<Canvas>(true);

        if (canvas == null || canvas.rootCanvas.GetComponentInChildren<PitStopHUD>(true) != null)
            return;

        Create(raceHud, canvas.rootCanvas.transform);
    }

    private static void Create(RaceHUD raceHud, Transform parent)
    {
        var go = new GameObject("Boxes (HUD)", typeof(RectTransform), typeof(PitStopHUD));
        go.layer = parent.gameObject.layer;

        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        Stretch(rect);

        // Que quede arriba de lo demás del HUD (los botones tienen que recibir el clic).
        rect.SetAsLastSibling();

        var hudComponent = go.GetComponent<PitStopHUD>();
        hudComponent.hud = raceHud;
        hudComponent.font = RaceStandingsTower.FindHudFont(raceHud, false);
        hudComponent.boldFont = RaceStandingsTower.FindHudFont(raceHud, true);
        hudComponent.Build(rect);
    }

    private void Build(RectTransform root)
    {
        // Cartel de arriba.
        var bannerImage = NewImage("Cartel", root, panelColor, false);
        RectTransform bannerRect = bannerImage.rectTransform;
        bannerRect.anchorMin = bannerRect.anchorMax = new Vector2(0.5f, 1f);
        bannerRect.pivot = new Vector2(0.5f, 1f);
        bannerRect.anchoredPosition = new Vector2(0f, -14f);
        // Angosto: a la izquierda está el botón de pausa.
        bannerRect.sizeDelta = new Vector2(330f, 52f);
        banner = bannerImage.gameObject;

        bannerText = NewText("Texto", bannerRect, font, 17f, TextAlignmentOptions.Center);
        Stretch(bannerText.rectTransform);
        banner.SetActive(false);

        mandatoryText = NewText("Parada obligatoria", root, font, 15f, TextAlignmentOptions.Center);
        RectTransform mandatoryRect = mandatoryText.rectTransform;
        mandatoryRect.anchorMin = mandatoryRect.anchorMax = new Vector2(0.5f, 1f);
        mandatoryRect.pivot = new Vector2(0.5f, 1f);
        mandatoryRect.anchoredPosition = new Vector2(0f, -70f);
        mandatoryRect.sizeDelta = new Vector2(330f, 22f);
        mandatoryText.gameObject.SetActive(false);

        // Elegir ruedas.
        var choiceImage = NewImage("Elegir ruedas", root, panelColor, false);
        RectTransform choiceRect = choiceImage.rectTransform;
        choiceRect.anchorMin = choiceRect.anchorMax = new Vector2(0.5f, 0.5f);
        choiceRect.pivot = new Vector2(0.5f, 0.5f);
        choiceRect.anchoredPosition = new Vector2(0f, -40f);
        choiceRect.sizeDelta = new Vector2(4 * 150f + 5 * 12f, 200f);
        choicePanel = choiceImage.gameObject;

        choiceTitle = NewText("Título", choiceRect, boldFont != null ? boldFont : font, 24f, TextAlignmentOptions.Center);
        Place(choiceTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(600f, 30f));
        choiceTitle.color = accentColor;

        for (int i = 0; i < MaxOptions; i++)
        {
            int index = i;

            var optionImage = NewImage("Rueda " + (i + 1), choiceRect, optionColor, true);
            var button = optionImage.gameObject.AddComponent<Button>();
            button.targetGraphic = optionImage;
            button.onClick.AddListener(() => Choose(index));

            // Hasta 4 en una fila (los demás, si algún día hay más, al lado).
            float x = (i - 1.5f) * 162f;
            Place(optionImage.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(x, -6f), new Vector2(150f, 110f));

            var icon = NewImage("Ícono", optionImage.rectTransform, Color.white, false);
            icon.preserveAspect = true;
            Place(icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -26f), new Vector2(44f, 44f));

            var label = NewText("Nombre", optionImage.rectTransform, font, 16f, TextAlignmentOptions.Bottom);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(6f, 6f);
            label.rectTransform.offsetMax = new Vector2(-6f, -6f);
            label.textWrappingMode = TextWrappingModes.Normal;

            optionBackgrounds.Add(optionImage);
            optionTexts.Add(label);
            optionIcons.Add(icon);
            optionObjects.Add(optionImage.gameObject);
        }

        choiceHint = NewText("Ayuda", choiceRect, font, 15f, TextAlignmentOptions.Center);
        Place(choiceHint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(600f, 22f));
        choiceHint.color = new Color(0.8f, 0.8f, 0.8f);
        choicePanel.SetActive(false);

        // Cambiando ruedas.
        var workImage = NewImage("Cambiando ruedas", root, panelColor, false);
        RectTransform workRect = workImage.rectTransform;
        workRect.anchorMin = workRect.anchorMax = new Vector2(0.5f, 0.5f);
        workRect.anchoredPosition = new Vector2(0f, -40f);
        workRect.sizeDelta = new Vector2(480f, 74f);
        workPanel = workImage.gameObject;

        workText = NewText("Texto", workRect, font, 18f, TextAlignmentOptions.Center);
        Place(workText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(460f, 26f));

        var barBack = NewImage("Barra", workRect, new Color(1f, 1f, 1f, 0.12f), false);
        Place(barBack.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(440f, 14f));

        var fill = NewImage("Hecho", barBack.rectTransform, accentColor, false);
        fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = new Vector2(0f, 1f);
        fill.rectTransform.offsetMin = Vector2.zero;
        fill.rectTransform.offsetMax = Vector2.zero;
        workFill = fill.rectTransform;
        workPanel.SetActive(false);
    }

    private static Image NewImage(string name, Transform parent, Color color, bool raycast)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);

        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = raycast;
        return image;
    }

    private static TMP_Text NewText(string name, Transform parent, TMP_FontAsset font, float size, TextAlignmentOptions alignment)
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
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }
}
