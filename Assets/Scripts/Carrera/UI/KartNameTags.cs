using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Nombres arriba de los karts de los demás (jugadores y bots), en el HUD.
/// - Se ve el nombre de cada kart sobre su cabeza, más chico cuanto más lejos.
///   Más allá de maxDistance, o detrás de la cámara, no se muestra.
/// - La tecla N los prende y apaga; la elección queda guardada (PlayerPrefs).
/// - El kart propio no lleva nombre.
/// Se arma solo al cargar una escena con RaceHUD (no hace falta tocar la escena),
/// detrás de los paneles del HUD. Se oculta junto con el HUD en los resultados.
/// </summary>
public class KartNameTags : MonoBehaviour
{
    private const string PrefsKey = "ApexRun.NombresKarts";

    [Tooltip("Tecla para mostrar u ocultar los nombres.")]
    [SerializeField] private Key toggleKey = Key.N;

    [Tooltip("Altura del nombre sobre el kart (m).")]
    [SerializeField] private float height = 1.05f;

    [Tooltip("Hasta qué distancia se ven los nombres (m).")]
    [SerializeField, Min(1f)] private float maxDistance = 70f;

    [SerializeField, Min(1f)] private float nearSize = 18f;
    [SerializeField, Min(1f)] private float farSize = 10f;

    [Tooltip("Distancia a la que el nombre tiene nearSize (m).")]
    [SerializeField, Min(0.5f)] private float nearDistance = 6f;

    [SerializeField] private Color textColor = Color.white;
    [SerializeField] private Color outlineColor = new Color(0f, 0f, 0f, 0.85f);

    private static readonly FieldInfo HudKartField =
        typeof(RaceHUD).GetField("playerKart", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly Dictionary<KartBehaviour, TextMeshProUGUI> labels = new Dictionary<KartBehaviour, TextMeshProUGUI>();
    private readonly List<KartBehaviour> removed = new List<KartBehaviour>();

    private RaceHUD hud;
    private RaceManager raceManager;
    private Canvas canvas;
    private RectTransform area;
    private TMP_FontAsset font;
    private bool visible = true;

    public bool Visible
    {
        get => visible;
        set
        {
            visible = value;
            PlayerPrefs.SetInt(PrefsKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    private void Awake()
    {
        visible = PlayerPrefs.GetInt(PrefsKey, 1) == 1;
        area = (RectTransform)transform;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
            Visible = !visible;

        if (raceManager == null)
            raceManager = FindFirstObjectByType<RaceManager>();

        if (canvas == null)
            canvas = GetComponentInParent<Canvas>();

        Camera camera = Camera.main;

        if (raceManager == null || camera == null || canvas == null)
            return;

        Rigidbody own = hud != null && HudKartField != null ? HudKartField.GetValue(hud) as Rigidbody : null;
        Camera eventCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

        foreach (KartBehaviour kart in raceManager.RaceKarts)
        {
            if (kart == null)
                continue;

            TextMeshProUGUI label = GetLabel(kart);
            Rigidbody body = kart.GetComponent<Rigidbody>();
            bool show = visible && body != own && kart.gameObject.activeInHierarchy;

            Vector3 screen = Vector3.zero;

            if (show)
            {
                screen = camera.WorldToScreenPoint(kart.transform.position + Vector3.up * height);
                show = screen.z > 0.1f && screen.z < maxDistance;
            }

            if (show && RectTransformUtility.ScreenPointToLocalPointInRectangle(area, screen, eventCamera, out Vector2 local))
            {
                float t = Mathf.InverseLerp(nearDistance, maxDistance, screen.z);
                label.rectTransform.anchoredPosition = local;
                label.fontSize = Mathf.Lerp(nearSize, farSize, t);

                Color color = textColor;
                color.a *= 1f - Mathf.InverseLerp(maxDistance * 0.8f, maxDistance, screen.z);
                label.color = color;

                string name = RaceStandingsTower.DisplayName(body);

                if (label.text != name)
                    label.text = name;

                if (!label.gameObject.activeSelf)
                    label.gameObject.SetActive(true);
            }
            else if (label.gameObject.activeSelf)
            {
                label.gameObject.SetActive(false);
            }
        }

        // Karts que ya no están (se fue un jugador).
        removed.Clear();

        foreach (KeyValuePair<KartBehaviour, TextMeshProUGUI> pair in labels)
        {
            if (pair.Key == null || !raceManager.RaceKarts.Contains(pair.Key))
                removed.Add(pair.Key);
        }

        foreach (KartBehaviour kart in removed)
        {
            if (labels[kart] != null)
                Destroy(labels[kart].gameObject);

            labels.Remove(kart);
        }
    }

    private TextMeshProUGUI GetLabel(KartBehaviour kart)
    {
        if (labels.TryGetValue(kart, out TextMeshProUGUI label) && label != null)
            return label;

        var go = new GameObject("Nombre", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.layer = gameObject.layer;

        var rect = (RectTransform)go.transform;
        rect.SetParent(area, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(200f, 30f);

        label = go.GetComponent<TextMeshProUGUI>();

        if (font != null)
            label.font = font;

        label.alignment = TextAlignmentOptions.Bottom;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        label.fontSize = nearSize;
        label.outlineWidth = 0.25f;
        label.outlineColor = outlineColor;

        labels[kart] = label;
        return label;
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
                if (raceHud.GetComponentInChildren<KartNameTags>(true) == null)
                    Create(raceHud);
            }
        }
    }

    private static void Create(RaceHUD raceHud)
    {
        var go = new GameObject("NombresKarts", typeof(RectTransform), typeof(KartNameTags));
        go.layer = raceHud.gameObject.layer;

        var rect = (RectTransform)go.transform;
        rect.SetParent(raceHud.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;

        // Detrás de los paneles del HUD.
        rect.SetAsFirstSibling();

        var tags = go.GetComponent<KartNameTags>();
        tags.hud = raceHud;

        tags.font = RaceStandingsTower.FindHudFont(raceHud, bold: true);
    }
}
