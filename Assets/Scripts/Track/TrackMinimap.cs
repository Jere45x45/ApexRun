using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Minimapa de la carrera, dibujado en la interfaz (no usa otra cámara).
/// - La pista se dibuja una vez, desde la línea central: un borde oscuro, el
///   asfalto encima y la línea de llegada.
/// - Cada kart es un punto que se mueve en cada frame. El del jugador es más
///   grande, de otro color y queda siempre arriba de los demás.
/// - El mapa queda fijo, visto desde arriba (+X a la derecha, +Z hacia arriba),
///   y se ajusta al rectángulo sin deformarse.
/// - Los karts que aparecen durante la carrera (multiplayer) se agregan con
///   AddKart y se sacan con RemoveKart.
/// </summary>
public class TrackMinimap : MaskableGraphic
{
    [Header("Pista")]
    [SerializeField] private TrackCenterline centerline;

    [Tooltip("Ancho del asfalto en el mapa (m). Con 0 usa el ancho medido de la pista.")]
    [SerializeField, Min(0f)] private float roadWidth;

    [Tooltip("Ancho mínimo del asfalto en el mapa (px), para que se vea en pantallas chicas.")]
    [SerializeField, Min(1f)] private float minRoadWidthPixels = 3f;

    [SerializeField, Min(0f)] private float borderPixels = 1.5f;

    [Tooltip("Margen libre alrededor de la pista (px).")]
    [SerializeField, Min(0f)] private float padding = 8f;

    [SerializeField] private Color roadColor = new Color(0.92f, 0.92f, 0.92f, 1f);

    [SerializeField] private Color borderColor = new Color(0.05f, 0.05f, 0.05f, 0.85f);

    [SerializeField] private Color finishLineColor = new Color(0.9f, 0.15f, 0.1f, 1f);

    [Header("Karts")]
    [SerializeField] private Transform playerKart;

    [SerializeField] private List<Transform> otherKarts = new List<Transform>();

    [Tooltip("Imagen de los puntos (un círculo).")]
    [SerializeField] private Sprite iconSprite;

    [SerializeField, Min(1f)] private float playerIconSize = 11f;

    [SerializeField, Min(1f)] private float otherIconSize = 8f;

    [SerializeField, Min(0f)] private float iconOutlinePixels = 1.5f;

    [SerializeField] private Color playerColor = new Color(1f, 0.75f, 0.1f, 1f);

    [SerializeField] private Color otherColor = new Color(0.25f, 0.6f, 1f, 1f);

    [SerializeField] private Color iconOutlineColor = new Color(0f, 0f, 0f, 0.9f);

    private readonly Dictionary<Transform, RectTransform> icons = new Dictionary<Transform, RectTransform>();

    // Conversión de mundo a mapa: centro de la pista (x, z) y píxeles por metro.
    private Vector2 worldCenter;
    private float pixelsPerMeter;

    protected override void Start()
    {
        base.Start();

        raycastTarget = false;

        if (!Application.isPlaying)
            return;

        foreach (Transform kart in otherKarts)
        {
            CreateIcon(kart, false);
        }

        CreateIcon(playerKart, true);
    }

    /// <summary>Agrega un kart al mapa (por ejemplo, uno que aparece en multiplayer).</summary>
    public void AddKart(Transform kart, bool isPlayer)
    {
        if (kart == null)
            return;

        if (isPlayer)
            playerKart = kart;
        else if (!otherKarts.Contains(kart))
            otherKarts.Add(kart);

        CreateIcon(kart, isPlayer);
    }

    public void RemoveKart(Transform kart)
    {
        if (kart == null)
            return;

        otherKarts.Remove(kart);

        if (playerKart == kart)
            playerKart = null;

        if (icons.TryGetValue(kart, out RectTransform icon))
        {
            icons.Remove(kart);

            if (icon != null)
                Destroy(icon.gameObject);
        }
    }

    private void LateUpdate()
    {
        if (icons.Count == 0 || !UpdateMapping())
            return;

        Vector2 rectCenter = rectTransform.rect.center;

        foreach (KeyValuePair<Transform, RectTransform> pair in icons)
        {
            if (pair.Key == null || pair.Value == null)
                continue;

            bool visible = pair.Key.gameObject.activeInHierarchy;

            if (pair.Value.gameObject.activeSelf != visible)
                pair.Value.gameObject.SetActive(visible);

            // Los íconos están anclados al centro del mapa.
            pair.Value.anchoredPosition = WorldToMap(pair.Key.position) - rectCenter;
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        if (!UpdateMapping())
            return;

        IReadOnlyList<Vector3> points = centerline.Points;
        Vector2[] line = new Vector2[points.Count];

        for (int i = 0; i < line.Length; i++)
        {
            line[i] = WorldToMap(points[i]);
        }

        float width = roadWidth > 0f ? roadWidth : centerline.AverageWidth;
        float road = Mathf.Max(width * pixelsPerMeter, minRoadWidthPixels);

        AddLoopStrip(vh, line, road + 2f * borderPixels, borderColor);
        AddLoopStrip(vh, line, road, roadColor);

        // Línea de llegada: una barra que cruza la pista en el punto 0.
        Vector2 along = (line[1] - line[0]).normalized;
        Vector2 across = new Vector2(-along.y, along.x) * (road * 0.5f + borderPixels);
        Vector2 thickness = along * Mathf.Max(1.5f, road * 0.3f) * 0.5f;

        AddQuad(vh,
            line[0] - across - thickness,
            line[0] + across - thickness,
            line[0] + across + thickness,
            line[0] - across + thickness,
            finishLineColor);
    }

    /// <summary>
    /// Calcula el centro y la escala para que la pista entre en el rectángulo
    /// sin deformarse. Devuelve false si todavía no hay línea central.
    /// </summary>
    private bool UpdateMapping()
    {
        if (centerline == null || !centerline.IsValid)
            return false;

        IReadOnlyList<Vector3> points = centerline.Points;

        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);

        for (int i = 0; i < points.Count; i++)
        {
            Vector2 flat = new Vector2(points[i].x, points[i].z);
            min = Vector2.Min(min, flat);
            max = Vector2.Max(max, flat);
        }

        // Se suma el ancho de la pista para que el borde no quede cortado.
        Vector2 size = max - min + Vector2.one * Mathf.Max(centerline.AverageWidth, roadWidth);
        Rect rect = rectTransform.rect;

        float availableWidth = Mathf.Max(1f, rect.width - 2f * padding);
        float availableHeight = Mathf.Max(1f, rect.height - 2f * padding);

        worldCenter = (min + max) * 0.5f;
        pixelsPerMeter = Mathf.Min(availableWidth / size.x, availableHeight / size.y);

        return true;
    }

    private Vector2 WorldToMap(Vector3 world)
    {
        Vector2 flat = new Vector2(world.x, world.z);

        return rectTransform.rect.center + (flat - worldCenter) * pixelsPerMeter;
    }

    private void CreateIcon(Transform kart, bool isPlayer)
    {
        if (kart == null || !Application.isPlaying)
            return;

        if (icons.TryGetValue(kart, out RectTransform existing) && existing != null)
            Destroy(existing.gameObject);

        float size = isPlayer ? playerIconSize : otherIconSize;

        // Contorno oscuro con el color adentro, para que se vea sobre el asfalto.
        RectTransform outline = CreateImage(kart.name + " (minimapa)", transform, iconOutlineColor);
        outline.anchorMin = new Vector2(0.5f, 0.5f);
        outline.anchorMax = new Vector2(0.5f, 0.5f);
        outline.sizeDelta = new Vector2(size, size);

        RectTransform fill = CreateImage("Color", outline, isPlayer ? playerColor : otherColor);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.offsetMin = Vector2.one * iconOutlinePixels;
        fill.offsetMax = -Vector2.one * iconOutlinePixels;

        icons[kart] = outline;

        // El jugador siempre arriba de los demás.
        if (playerKart != null && icons.TryGetValue(playerKart, out RectTransform playerIcon) && playerIcon != null)
            playerIcon.SetAsLastSibling();
    }

    private RectTransform CreateImage(string objectName, Transform parent, Color imageColor)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(parent, false);

        Image image = imageObject.GetComponent<Image>();
        image.sprite = iconSprite;
        image.color = imageColor;
        image.raycastTarget = false;

        return (RectTransform)imageObject.transform;
    }

    /// <summary>Franja de ancho fijo a lo largo de una línea cerrada.</summary>
    private static void AddLoopStrip(VertexHelper vh, Vector2[] line, float width, Color32 stripColor)
    {
        int count = line.Length;
        int first = vh.currentVertCount;
        float half = width * 0.5f;

        for (int i = 0; i < count; i++)
        {
            Vector2 tangent = line[(i + 1) % count] - line[(i - 1 + count) % count];
            tangent = tangent.sqrMagnitude > 1e-8f ? tangent.normalized : Vector2.right;

            Vector2 normal = new Vector2(-tangent.y, tangent.x) * half;

            vh.AddVert(line[i] + normal, stripColor, Vector2.zero);
            vh.AddVert(line[i] - normal, stripColor, Vector2.zero);
        }

        for (int i = 0; i < count; i++)
        {
            int a = first + 2 * i;
            int b = first + 2 * ((i + 1) % count);

            vh.AddTriangle(a, b, a + 1);
            vh.AddTriangle(a + 1, b, b + 1);
        }
    }

    private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 quadColor)
    {
        int first = vh.currentVertCount;

        vh.AddVert(a, quadColor, Vector2.zero);
        vh.AddVert(b, quadColor, Vector2.zero);
        vh.AddVert(c, quadColor, Vector2.zero);
        vh.AddVert(d, quadColor, Vector2.zero);

        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first, first + 2, first + 3);
    }
}
