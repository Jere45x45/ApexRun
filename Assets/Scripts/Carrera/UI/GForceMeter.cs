using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Medidor de fuerza G del HUD, al estilo del de BeamNG.drive:
/// - Un círculo con anillos cada 1 G y una cruz.
/// - Un punto que muestra la fuerza que siente el piloto, mirando desde el kart:
///   a la derecha al doblar a la izquierda, abajo al acelerar y arriba al frenar.
/// - Una estela con el recorrido del punto en el último segundo y medio.
/// - El contorno de los máximos alcanzados en cada dirección.
/// - Debajo, el valor total en G.
///
/// La fuerza sale del cambio de velocidad del Rigidbody en cada paso de física
/// (sin la vertical), dividido por la gravedad, y se suaviza para que no tiemble
/// con los pianos y los golpes. Las reapariciones (saltos enormes) se ignoran.
///
/// Se dibuja con geometría propia (no usa sprites) y se arma solo al cargar
/// una escena con RaceHUD: se pone arriba del velocímetro. Si ya hay uno en
/// la escena, no se crea otro.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class GForceMeter : MaskableGraphic
{
    private const float Gravity = 9.81f;
    private const int CircleSegments = 64;
    private const int MaxBins = 48;

    [Header("Kart")]
    [Tooltip("De acá se toma el kart del jugador (el mismo del velocímetro).")]
    [SerializeField] private RaceHUD hud;

    [Header("Escala")]
    [Tooltip("G en el borde del círculo.")]
    [SerializeField, Min(0.5f)] private float maxG = 3f;
    [Tooltip("Cada cuántos G hay un anillo.")]
    [SerializeField, Min(0.25f)] private float ringStep = 1f;

    [Header("Lectura")]
    [Tooltip("Suavizado (s). Sin esto el punto tiembla con los pianos y los golpes.")]
    [SerializeField, Min(0f)] private float smoothing = 0.12f;
    [Tooltip("Más que esto en un paso se toma como una reaparición y se ignora.")]
    [SerializeField, Min(1f)] private float ignoreAboveG = 15f;
    [Tooltip("Cuánto dura la estela (s).")]
    [SerializeField, Min(0f)] private float trailSeconds = 1.5f;

    [Header("Texto")]
    [SerializeField] private TMP_Text valueText;

    [Header("Colores")]
    [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.45f);
    [SerializeField] private Color gridColor = new Color(1f, 1f, 1f, 0.22f);
    [SerializeField] private Color maxColor = new Color(1f, 0.8f, 0.2f, 0.6f);
    [SerializeField] private Color trailColor = new Color(0.9f, 0.08f, 0.08f, 0.9f);
    [SerializeField] private Color dotColor = Color.white;
    [SerializeField] private Color dotOutlineColor = new Color(0.9f, 0.08f, 0.08f, 1f);

    [Header("Grosores (px)")]
    [SerializeField, Min(0.5f)] private float lineWidth = 1.5f;
    [SerializeField, Min(0.5f)] private float trailWidth = 3f;
    [SerializeField, Min(1f)] private float dotRadius = 5f;

    private static readonly FieldInfo HudKartField =
        typeof(RaceHUD).GetField("playerKart", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly float[] maxByDirection = new float[MaxBins];
    private readonly List<Vector3> trail = new List<Vector3>(); // x, y: G; z: tiempo

    private Rigidbody kart;
    private Vector3 previousVelocity;
    private bool hasPrevious;
    private Vector2 current;
    private string shownText;

    /// <summary>Fuerza G que siente el piloto: x a la derecha, y hacia adelante.</summary>
    public Vector2 CurrentG => current;

    /// <summary>Fuerza G total.</summary>
    public float TotalG => current.magnitude;

    /// <summary>Borra los máximos y la estela.</summary>
    public void ResetMax()
    {
        System.Array.Clear(maxByDirection, 0, maxByDirection.Length);
        trail.Clear();
        SetVerticesDirty();
    }

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    private Rigidbody FindKart()
    {
        if (hud == null)
            hud = GetComponentInParent<RaceHUD>();

        return hud != null && HudKartField != null ? HudKartField.GetValue(hud) as Rigidbody : null;
    }

    private void FixedUpdate()
    {
        Rigidbody body = FindKart();

        if (body != kart)
        {
            kart = body;
            hasPrevious = false;
            current = Vector2.zero;
            ResetMax();
        }

        if (kart == null)
            return;

        Vector3 velocity = kart.linearVelocity;

        if (!hasPrevious)
        {
            previousVelocity = velocity;
            hasPrevious = true;
            return;
        }

        Vector3 acceleration = (velocity - previousVelocity) / Time.fixedDeltaTime;
        previousVelocity = velocity;

        // En los ejes del kart. Lo que siente el piloto es lo contrario de la aceleración.
        Vector3 local = Quaternion.Inverse(kart.rotation) * acceleration;
        Vector2 felt = new Vector2(-local.x, -local.z) / Gravity;

        if (felt.magnitude > ignoreAboveG)
            return;

        float blend = smoothing > 0f ? 1f - Mathf.Exp(-Time.fixedDeltaTime / smoothing) : 1f;
        current = Vector2.Lerp(current, felt, blend);

        float magnitude = current.magnitude;

        if (magnitude > 0.02f)
        {
            float angle = Mathf.Atan2(current.y, current.x);
            int bin = Mathf.RoundToInt((angle / (2f * Mathf.PI) + 1f) * MaxBins) % MaxBins;
            maxByDirection[bin] = Mathf.Max(maxByDirection[bin], magnitude);
        }
    }

    private void Update()
    {
        float now = Time.time;

        trail.Add(new Vector3(current.x, current.y, now));

        int old = 0;
        while (old < trail.Count && now - trail[old].z > trailSeconds)
            old++;

        if (old > 0)
            trail.RemoveRange(0, old);

        SetVerticesDirty();

        if (valueText != null)
        {
            string text = TotalG.ToString("0.00", CultureInfo.InvariantCulture) + " G";

            if (text != shownText)
            {
                shownText = text;
                valueText.text = text;
            }
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect rect = GetPixelAdjustedRect();
        Vector2 center = rect.center;
        float radius = Mathf.Min(rect.width, rect.height) * 0.5f;

        if (radius <= 1f)
            return;

        float scale = radius / maxG;

        AddDisc(vh, center, radius, backgroundColor);

        for (float g = ringStep; g <= maxG + 0.001f; g += ringStep)
            AddRing(vh, center, g * scale, lineWidth, gridColor);

        AddLine(vh, center + Vector2.left * radius, center + Vector2.right * radius, lineWidth, gridColor, gridColor);
        AddLine(vh, center + Vector2.down * radius, center + Vector2.up * radius, lineWidth, gridColor, gridColor);

        // Contorno de los máximos.
        for (int i = 0; i < MaxBins; i++)
        {
            int next = (i + 1) % MaxBins;

            if (maxByDirection[i] <= 0f || maxByDirection[next] <= 0f)
                continue;

            AddLine(vh, ToPixel(center, scale, BinPoint(i)), ToPixel(center, scale, BinPoint(next)),
                lineWidth, maxColor, maxColor);
        }

        // Estela: se va apagando con el tiempo.
        float now = Time.time;

        for (int i = 1; i < trail.Count; i++)
        {
            Color a = trailColor;
            Color b = trailColor;
            a.a *= Fade(now - trail[i - 1].z);
            b.a *= Fade(now - trail[i].z);

            AddLine(vh, ToPixel(center, scale, trail[i - 1]), ToPixel(center, scale, trail[i]), trailWidth, a, b);
        }

        // Punto actual.
        Vector2 dot = ToPixel(center, scale, current);
        AddDisc(vh, dot, dotRadius + 1.5f, dotOutlineColor);
        AddDisc(vh, dot, dotRadius, dotColor);
    }

    private float Fade(float age)
    {
        return trailSeconds > 0f ? Mathf.Clamp01(1f - age / trailSeconds) : 0f;
    }

    private Vector2 BinPoint(int bin)
    {
        float angle = bin * 2f * Mathf.PI / MaxBins;
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * maxByDirection[bin];
    }

    private Vector2 ToPixel(Vector2 center, float scale, Vector2 g)
    {
        return center + Vector2.ClampMagnitude(g, maxG) * scale;
    }

    private static void AddDisc(VertexHelper vh, Vector2 center, float radius, Color color)
    {
        int start = vh.currentVertCount;
        vh.AddVert(center, color, Vector2.zero);

        for (int i = 0; i <= CircleSegments; i++)
        {
            float angle = i * 2f * Mathf.PI / CircleSegments;
            vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, color, Vector2.zero);
        }

        for (int i = 1; i <= CircleSegments; i++)
            vh.AddTriangle(start, start + i + 1, start + i);
    }

    private static void AddRing(VertexHelper vh, Vector2 center, float radius, float width, Color color)
    {
        float inner = radius - width * 0.5f;
        float outer = radius + width * 0.5f;
        int start = vh.currentVertCount;

        for (int i = 0; i <= CircleSegments; i++)
        {
            float angle = i * 2f * Mathf.PI / CircleSegments;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            vh.AddVert(center + direction * inner, color, Vector2.zero);
            vh.AddVert(center + direction * outer, color, Vector2.zero);
        }

        for (int i = 0; i < CircleSegments; i++)
        {
            int a = start + i * 2;
            vh.AddTriangle(a, a + 1, a + 3);
            vh.AddTriangle(a, a + 3, a + 2);
        }
    }

    private static void AddLine(VertexHelper vh, Vector2 from, Vector2 to, float width, Color colorFrom, Color colorTo)
    {
        Vector2 direction = to - from;

        if (direction.sqrMagnitude < 0.0001f)
            return;

        Vector2 normal = new Vector2(-direction.y, direction.x).normalized * (width * 0.5f);
        int start = vh.currentVertCount;

        vh.AddVert(from - normal, colorFrom, Vector2.zero);
        vh.AddVert(from + normal, colorFrom, Vector2.zero);
        vh.AddVert(to + normal, colorTo, Vector2.zero);
        vh.AddVert(to - normal, colorTo, Vector2.zero);

        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
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
                if (raceHud.GetComponentInChildren<GForceMeter>(true) == null)
                    Create(raceHud);
            }
        }
    }

    private static void Create(RaceHUD raceHud)
    {
        FieldInfo speedField = typeof(RaceHUD).GetField("speedText", BindingFlags.Instance | BindingFlags.NonPublic);
        TMP_Text speed = speedField != null ? speedField.GetValue(raceHud) as TMP_Text : null;

        // Va al lado del velocímetro: en su mismo panel (el HUD) y arriba de su recuadro.
        RectTransform speedBox = speed != null ? speed.transform.parent as RectTransform : null;
        Transform parent = speedBox != null ? speedBox.parent : raceHud.transform;

        var go = new GameObject("MedidorG", typeof(RectTransform), typeof(CanvasRenderer), typeof(GForceMeter));
        go.layer = parent.gameObject.layer;

        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(100f, 100f);

        float boxTop = speedBox != null ? speedBox.anchoredPosition.y + speedBox.sizeDelta.y : 60f;
        float boxCenterX = speedBox != null ? speedBox.anchoredPosition.x - speedBox.sizeDelta.x * 0.5f : -70f;
        const float labelHeight = 20f;
        rect.anchoredPosition = new Vector2(boxCenterX, boxTop + labelHeight + 4f);

        var meter = go.GetComponent<GForceMeter>();
        meter.hud = raceHud;

        var label = new GameObject("Valor", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.layer = go.layer;
        var labelRect = (RectTransform)label.transform;
        labelRect.SetParent(rect, false);
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 0f);
        labelRect.pivot = new Vector2(0.5f, 1f);
        labelRect.anchoredPosition = new Vector2(0f, -2f);
        labelRect.sizeDelta = new Vector2(0f, labelHeight);

        var text = label.GetComponent<TextMeshProUGUI>();
        if (speed != null)
        {
            text.font = speed.font;
            text.fontSharedMaterial = speed.fontSharedMaterial;
        }
        text.fontSize = 16f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.text = "0.00 G";

        meter.valueText = text;
    }
}
