using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Estado de las cubiertas en el HUD, al estilo de la telemetría de F1:
/// - Cuatro cubiertas vistas desde arriba (adelante arriba).
/// - El color es la temperatura: azul fría, verde en su punto, naranja y
///   rojo recalentada.
/// - Lo pintado de cada cubierta es la goma que le queda: baja con el desgaste.
/// - Al costado, la temperatura en °C.
/// - Abajo, "REBUFO" mientras el kart va en la estela de otro.
///
/// Se arma solo al cargar una escena con RaceHUD, a la izquierda del medidor
/// de fuerza G (así no hace falta tocar la escena). Con la tecla H se muestra
/// u oculta, y queda guardado.
/// </summary>
public class TireStatusHUD : MonoBehaviour
{
    private const string VisibleKey = "ApexRun.EstadoCubiertas";
    private const float TireWidth = 16f;
    private const float TireHeight = 26f;

    [SerializeField] private RaceHUD hud;

    [Tooltip("Tecla para mostrar u ocultar el estado de las cubiertas.")]
    [SerializeField] private Key toggleKey = Key.H;

    [Header("Colores")]
    [SerializeField] private Color coldColor = new Color(0.25f, 0.55f, 1f);
    [SerializeField] private Color optimalColor = new Color(0.2f, 0.85f, 0.35f);
    [SerializeField] private Color warmColor = new Color(1f, 0.6f, 0.1f);
    [SerializeField] private Color hotColor = new Color(0.95f, 0.12f, 0.1f);
    [SerializeField] private Color emptyColor = new Color(0.08f, 0.08f, 0.09f, 0.9f);

    private static readonly FieldInfo HudKartField =
        typeof(RaceHUD).GetField("playerKart", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly Image[] fills = new Image[4];
    private readonly TMP_Text[] temperatures = new TMP_Text[4];
    private readonly int[] shownTemperature = { int.MinValue, int.MinValue, int.MinValue, int.MinValue };
    private GameObject content;
    private TMP_Text slipstreamText;
    private Rigidbody kartBody;
    private KartBehaviour kart;
    private bool visible = true;

    private void Start()
    {
        visible = PlayerPrefs.GetInt(VisibleKey, 1) != 0;
        ApplyVisible();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
        {
            visible = !visible;
            PlayerPrefs.SetInt(VisibleKey, visible ? 1 : 0);
            PlayerPrefs.Save();
            ApplyVisible();
        }

        KartBehaviour current = FindKart();
        KartTireThermals thermals = current != null ? current.TireThermals : null;

        if (content != null && content.activeSelf != (visible && thermals != null))
            content.SetActive(visible && thermals != null);

        if (thermals == null || !visible)
            return;

        TireThermalSettings settings = thermals.Settings;

        for (int i = 0; i < fills.Length; i++)
        {
            float temperature = thermals.GetTemperature(i);
            float remaining = 1f - thermals.GetWear(i);

            fills[i].color = TemperatureColor(temperature, settings);

            RectTransform fill = fills[i].rectTransform;
            fill.anchorMax = new Vector2(1f, Mathf.Clamp01(remaining));

            int rounded = Mathf.RoundToInt(temperature);

            if (rounded != shownTemperature[i])
            {
                shownTemperature[i] = rounded;
                temperatures[i].text = rounded + "°";
            }
        }

        float slip = current.Slipstream;
        Color slipColor = slipstreamText.color;
        slipColor.a = Mathf.Clamp01(slip * 2.5f);
        slipstreamText.color = slipColor;
    }

    private KartBehaviour FindKart()
    {
        if (hud == null)
            hud = GetComponentInParent<RaceHUD>();

        Rigidbody body = hud != null && HudKartField != null ? HudKartField.GetValue(hud) as Rigidbody : null;

        if (body != kartBody)
        {
            kartBody = body;
            kart = body != null ? body.GetComponent<KartBehaviour>() : null;
        }

        return kart;
    }

    private Color TemperatureColor(float temperature, TireThermalSettings settings)
    {
        if (temperature < settings.optimalMin)
        {
            float t = Mathf.InverseLerp(settings.coldTemperature + 10f, settings.optimalMin, temperature);
            return Color.Lerp(coldColor, optimalColor, t);
        }

        if (temperature <= settings.optimalMax)
            return optimalColor;

        float middle = (settings.optimalMax + settings.hotTemperature) * 0.5f;

        if (temperature < middle)
            return Color.Lerp(optimalColor, warmColor, Mathf.InverseLerp(settings.optimalMax, middle, temperature));

        return Color.Lerp(warmColor, hotColor, Mathf.InverseLerp(middle, settings.hotTemperature, temperature));
    }

    private void ApplyVisible()
    {
        if (content != null)
            content.SetActive(visible);
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
                if (raceHud.GetComponentInChildren<TireStatusHUD>(true) == null)
                    Create(raceHud);
            }
        }
    }

    private static void Create(RaceHUD raceHud)
    {
        FieldInfo speedField = typeof(RaceHUD).GetField("speedText", BindingFlags.Instance | BindingFlags.NonPublic);
        TMP_Text speed = speedField != null ? speedField.GetValue(raceHud) as TMP_Text : null;

        RectTransform speedBox = speed != null ? speed.transform.parent as RectTransform : null;
        Transform parent = speedBox != null ? speedBox.parent : raceHud.transform;

        var go = new GameObject("EstadoCubiertas", typeof(RectTransform), typeof(TireStatusHUD));
        go.layer = parent.gameObject.layer;

        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(96f, 100f);

        // A la izquierda del medidor G (que va arriba del velocímetro, centrado con él).
        float boxTop = speedBox != null ? speedBox.anchoredPosition.y + speedBox.sizeDelta.y : 60f;
        float boxCenterX = speedBox != null ? speedBox.anchoredPosition.x - speedBox.sizeDelta.x * 0.5f : -70f;
        rect.anchoredPosition = new Vector2(boxCenterX - 112f, boxTop + 24f);

        var status = go.GetComponent<TireStatusHUD>();
        status.hud = raceHud;

        var content = new GameObject("Contenido", typeof(RectTransform));
        content.layer = go.layer;
        var contentRect = (RectTransform)content.transform;
        contentRect.SetParent(rect, false);
        Stretch(contentRect);
        status.content = content;

        var background = NewImage("Fondo", contentRect, new Color(0f, 0f, 0f, 0.45f));
        Stretch(background.rectTransform);

        // Cuerpo del kart entre las cuatro cubiertas.
        var body = NewImage("Kart", contentRect, new Color(1f, 1f, 1f, 0.12f));
        Place(body.rectTransform, new Vector2(0f, 8f), new Vector2(18f, 56f));

        TMP_FontAsset font = speed != null ? speed.font : null;
        Material fontMaterial = speed != null ? speed.fontSharedMaterial : null;

        // FL, FR, RL, RR
        Vector2[] centers =
        {
            new Vector2(-17f, 26f),
            new Vector2(17f, 26f),
            new Vector2(-17f, -10f),
            new Vector2(17f, -10f)
        };

        for (int i = 0; i < 4; i++)
        {
            bool left = i % 2 == 0;

            var tire = NewImage("Cubierta" + i, contentRect, status.emptyColor);
            Place(tire.rectTransform, centers[i], new Vector2(TireWidth, TireHeight));

            var fill = NewImage("Goma", tire.rectTransform, status.optimalColor);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.offsetMin = new Vector2(1.5f, 1.5f);
            fill.rectTransform.offsetMax = new Vector2(-1.5f, -1.5f);
            status.fills[i] = fill;

            var label = NewText("Temperatura" + i, contentRect, font, fontMaterial, 12f,
                left ? TextAlignmentOptions.Right : TextAlignmentOptions.Left);
            Place(label.rectTransform, centers[i] + new Vector2(left ? -24f : 24f, 0f), new Vector2(32f, 16f));
            label.text = "--°";
            status.temperatures[i] = label;
        }

        var slipText = NewText("Rebufo", contentRect, font, fontMaterial, 13f, TextAlignmentOptions.Center);
        Place(slipText.rectTransform, new Vector2(0f, -39f), new Vector2(96f, 18f));
        slipText.text = GameLanguage.Text("REBUFO", "SLIPSTREAM");
        slipText.color = new Color(0.4f, 0.85f, 1f, 0f);
        status.slipstreamText = slipText;
    }

    private static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);

        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text NewText(string name, Transform parent, TMP_FontAsset font, Material material, float size, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);

        var text = go.GetComponent<TextMeshProUGUI>();

        if (font != null)
        {
            text.font = font;
            text.fontSharedMaterial = material;
        }

        text.fontSize = size;
        text.color = Color.white;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void Place(RectTransform rect, Vector2 center, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = center;
        rect.sizeDelta = size;
    }
}
