using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Calle de boxes de la pista: el recorrido, el limitador y un rectángulo por
/// kart (su box). La usa PitStops (la parada) y no maneja nada por su cuenta.
///
/// - Recorrido (path): la línea del medio de la calle, en el sentido normal
///   de la carrera. Empieza sobre la pista un poco antes de la entrada y
///   termina sobre la pista un poco después de la salida (por ahí entran y
///   salen los bots).
/// - Limitador: entre limiterStart y limiterEnd (metros sobre el recorrido),
///   que es donde la calle ya está separada de la pista.
/// - Boxes: boxCount rectángulos de boxSize, uno atrás del otro cada
///   boxSpacing metros desde firstBoxDistance, corridos boxOffset metros al
///   costado (a la derecha yendo en el sentido normal; negativo = a la
///   izquierda). El box 1 es el primero que se encuentra yendo en el sentido
///   de la carrera.
/// - Pista al revés (TrackDirection): todo se da vuelta solo; las consultas
///   (Project, GetPoint, GetBox...) siempre hablan en el sentido de esta carrera.
///
/// Todavía no hay modelos de boxes: mientras tanto (drawPlaceholders) se
/// pintan en el piso los rectángulos con su número, el tuyo en amarillo, y
/// las líneas del limitador. Cuando estén los modelos, se apaga
/// drawPlaceholders y se ponen los modelos en los mismos lugares (en el
/// editor se ven con Gizmos al seleccionar el objeto).
/// </summary>
public class PitLane : MonoBehaviour
{
    [Tooltip("Línea del medio de la calle de boxes, en el sentido normal de la carrera (de la entrada a la salida, empezando y terminando sobre la pista).")]
    [SerializeField] private Vector3[] path = new Vector3[0];

    [Tooltip("Medio ancho de la calle (m).")]
    [SerializeField, Min(1f)] private float halfWidth = 3f;

    [Tooltip("Velocidad máxima en la calle de boxes (km/h).")]
    [SerializeField, Min(5f)] private float speedLimitKmh = 40f;

    [Header("Limitador (metros sobre el recorrido, sentido normal)")]
    [SerializeField, Min(0f)] private float limiterStart = 20f;
    [SerializeField, Min(0f)] private float limiterEnd = 150f;

    [Header("Boxes")]
    [SerializeField, Min(1)] private int boxCount = 12;

    [Tooltip("Dónde está el centro del primer box, en metros sobre el recorrido (sentido normal).")]
    [SerializeField, Min(0f)] private float firstBoxDistance = 40f;

    [SerializeField, Min(3f)] private float boxSpacing = 6.5f;

    [Tooltip("Ancho y largo de cada rectángulo (m).")]
    [SerializeField] private Vector2 boxSize = new Vector2(2.2f, 4.2f);

    [Tooltip("Cuánto al costado de la línea del medio van los boxes (m). Positivo = a la derecha yendo en el sentido normal.")]
    [SerializeField] private float boxOffset = 1.3f;

    [Header("Marcas provisorias (hasta que estén los modelos)")]
    [SerializeField] private bool drawPlaceholders = true;
    [SerializeField] private Color boxColor = new Color(1f, 1f, 1f, 0.9f);
    [SerializeField] private Color ownBoxColor = new Color(1f, 0.82f, 0.1f, 1f);
    [SerializeField] private Color limiterColor = new Color(1f, 0.3f, 0.2f, 0.95f);

    private const float LineWidth = 0.14f;

    // Recorrido en el sentido de esta carrera.
    private Vector3[] points;
    private float[] cumulative;
    private bool builtReversed;
    private bool built;

    // Marcas.
    private GameObject placeholders;
    private bool placeholdersReversed;
    private readonly List<MeshRenderer> boxOutlines = new List<MeshRenderer>();
    private readonly List<MeshRenderer> boxFills = new List<MeshRenderer>();
    private readonly List<TextMeshPro> boxNumbers = new List<TextMeshPro>();
    private int highlightedBox = -1;
    private Material lineMaterial;

    private static PitLane cached;

    /// <summary>La calle de boxes de la escena, o null si la pista no tiene.</summary>
    public static PitLane Instance
    {
        get
        {
            if (cached == null)
                cached = FindFirstObjectByType<PitLane>();

            return cached;
        }
    }

    public bool IsValid => path != null && path.Length >= 2;

    /// <summary>Largo del recorrido (m).</summary>
    public float Length
    {
        get
        {
            Ensure();
            return cumulative != null && cumulative.Length > 0 ? cumulative[cumulative.Length - 1] : 0f;
        }
    }

    public float HalfWidth => halfWidth;

    /// <summary>Velocidad máxima en la calle (m/s).</summary>
    public float SpeedLimit => speedLimitKmh / 3.6f;

    public float SpeedLimitKmh => speedLimitKmh;

    public int BoxCount => boxCount;

    public Vector2 BoxSize => boxSize;

    /// <summary>Dónde empieza el limitador en esta carrera (m sobre el recorrido).</summary>
    public float LimiterStart => Reversed ? Length - limiterEnd : limiterStart;

    /// <summary>Dónde termina el limitador en esta carrera (m sobre el recorrido).</summary>
    public float LimiterEnd => Reversed ? Length - limiterStart : limiterEnd;

    private static bool Reversed => TrackDirection.IsReversed;

    private void OnDestroy()
    {
        if (cached == this)
            cached = null;

        if (lineMaterial != null)
            Destroy(lineMaterial);
    }

    private void Update()
    {
        // La pista se da vuelta después de que la escena arranca (en los
        // clientes, cuando llega por red): las marcas se rehacen.
        if (drawPlaceholders && IsValid && (placeholders == null || placeholdersReversed != Reversed))
            BuildPlaceholders();
    }

    // ------------------------------------------------------------------
    // Recorrido
    // ------------------------------------------------------------------

    private void Ensure()
    {
        if (!IsValid)
        {
            points = null;
            cumulative = null;
            return;
        }

        bool reversed = Reversed;

        if (built && builtReversed == reversed && points != null && points.Length == path.Length)
            return;

        points = new Vector3[path.Length];

        for (int i = 0; i < path.Length; i++)
            points[i] = reversed ? path[path.Length - 1 - i] : path[i];

        cumulative = new float[points.Length];

        for (int i = 1; i < points.Length; i++)
            cumulative[i] = cumulative[i - 1] + Vector3.Distance(points[i - 1], points[i]);

        builtReversed = reversed;
        built = true;
    }

    /// <summary>
    /// El punto del recorrido más cercano: s = metros desde la entrada (en el
    /// sentido de esta carrera) y lateral = metros al costado (+ = a la
    /// derecha yendo en el sentido de la carrera). Devuelve la distancia
    /// horizontal a la línea.
    /// </summary>
    public float Project(Vector3 position, out float s, out float lateral)
    {
        Ensure();
        s = 0f;
        lateral = 0f;

        if (points == null)
            return float.MaxValue;

        float bestSqr = float.MaxValue;

        for (int i = 0; i < points.Length - 1; i++)
        {
            Vector3 a = Flat(points[i]);
            Vector3 ab = Flat(points[i + 1]) - a;
            Vector3 p = Flat(position);

            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            Vector3 closest = a + ab * t;
            float sqr = (p - closest).sqrMagnitude;

            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                s = cumulative[i] + t * (cumulative[i + 1] - cumulative[i]);

                Vector3 right = Vector3.Cross(Vector3.up, ab).normalized;
                lateral = Vector3.Dot(p - closest, right);
            }
        }

        return Mathf.Sqrt(bestSqr);
    }

    /// <summary>True si la posición está en la calle de boxes, en la parte del limitador.</summary>
    public bool IsInLane(Vector3 position, out float s, out float lateral)
    {
        float distance = Project(position, out s, out lateral);
        return distance <= halfWidth + 0.75f && s >= LimiterStart && s <= LimiterEnd;
    }

    /// <summary>Punto del recorrido a s metros de la entrada, corrido lateral metros a la derecha.</summary>
    public Vector3 GetPoint(float s, float lateral = 0f)
    {
        Ensure();

        if (points == null)
            return transform.position;

        int segment = FindSegment(s, out float t);
        Vector3 a = points[segment];
        Vector3 b = points[segment + 1];
        Vector3 point = Vector3.Lerp(a, b, t);
        Vector3 right = Vector3.Cross(Vector3.up, Flat(b - a)).normalized;

        return point + right * lateral;
    }

    /// <summary>Hacia dónde va la calle en s (horizontal, en el sentido de la carrera).</summary>
    public Vector3 GetDirection(float s)
    {
        Ensure();

        if (points == null)
            return Vector3.forward;

        int segment = FindSegment(s, out _);
        Vector3 direction = Flat(points[segment + 1] - points[segment]);
        return direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
    }

    private int FindSegment(float s, out float t)
    {
        s = Mathf.Clamp(s, 0f, cumulative[cumulative.Length - 1]);

        for (int i = 0; i < points.Length - 1; i++)
        {
            if (s <= cumulative[i + 1] || i == points.Length - 2)
            {
                float length = cumulative[i + 1] - cumulative[i];
                t = length > 1e-5f ? Mathf.Clamp01((s - cumulative[i]) / length) : 0f;
                return i;
            }
        }

        t = 0f;
        return 0;
    }

    // ------------------------------------------------------------------
    // Boxes
    // ------------------------------------------------------------------

    /// <summary>Dónde está el box (0 = el primero que se encuentra en esta carrera).</summary>
    public void GetBox(int index, out Vector3 center, out Quaternion rotation, out float s, out float lateral)
    {
        index = Mathf.Clamp(index, 0, boxCount - 1);

        float last = firstBoxDistance + (boxCount - 1) * boxSpacing;

        // Los boxes van todos en fila, sobre la recta que une el primero con el
        // último (así quedan todos derechos aunque la línea de la calle tenga
        // algún zigzag), uno cada boxSpacing metros.
        float firstS;
        float lastS;

        if (Reversed)
        {
            // El primero en esta carrera es el último en el sentido normal.
            firstS = Length - last;
            lastS = Length - firstBoxDistance;
            lateral = -boxOffset;
        }
        else
        {
            firstS = firstBoxDistance;
            lastS = last;
            lateral = boxOffset;
        }

        s = firstS + index * boxSpacing;

        Vector3 start = GetPoint(firstS);
        Vector3 row = Flat(GetPoint(lastS) - start);
        Vector3 direction = boxCount > 1 && row.sqrMagnitude > 1f ? row.normalized : GetDirection(s);
        Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;

        center = start + direction * (index * boxSpacing) + right * lateral;
        center.y = GetPoint(s).y;
        rotation = Quaternion.LookRotation(direction, Vector3.up);
    }

    /// <summary>True si el centro del kart está dentro del rectángulo del box.</summary>
    public bool IsInsideBox(int index, Vector3 position)
    {
        GetBox(index, out Vector3 center, out Quaternion rotation, out _, out _);
        Vector3 local = Quaternion.Inverse(rotation) * (position - center);

        return Mathf.Abs(local.x) <= boxSize.x * 0.5f && Mathf.Abs(local.z) <= boxSize.y * 0.5f && Mathf.Abs(local.y) < 2.5f;
    }

    /// <summary>Box que se pinta de amarillo (el del jugador de esta computadora). -1 = ninguno.</summary>
    public void SetHighlightedBox(int index)
    {
        if (highlightedBox == index)
            return;

        highlightedBox = index;
        ApplyHighlight();
    }

    // ------------------------------------------------------------------
    // Marcas provisorias
    // ------------------------------------------------------------------

    private void BuildPlaceholders()
    {
        if (placeholders != null)
            Destroy(placeholders);

        boxOutlines.Clear();
        boxFills.Clear();
        boxNumbers.Clear();

        placeholdersReversed = Reversed;
        placeholders = new GameObject("Marcas provisorias");
        placeholders.transform.SetParent(transform, false);

        if (lineMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            lineMaterial = new Material(shader);
        }

        for (int i = 0; i < boxCount; i++)
        {
            GetBox(i, out Vector3 center, out Quaternion rotation, out _, out _);
            center = OnGround(center);

            var box = new GameObject("Box " + (i + 1));
            box.transform.SetParent(placeholders.transform, false);
            box.transform.SetPositionAndRotation(center, rotation);

            boxOutlines.Add(CreateMesh("Rectángulo", box.transform, OutlineMesh(boxSize.x, boxSize.y, LineWidth), boxColor));

            MeshRenderer fill = CreateMesh("Relleno", box.transform, QuadMesh(boxSize.x - LineWidth * 2f, boxSize.y - LineWidth * 2f, Vector3.zero), new Color(ownBoxColor.r, ownBoxColor.g, ownBoxColor.b, 0.22f));
            fill.enabled = false;
            boxFills.Add(fill);

            boxNumbers.Add(CreateLabel((i + 1).ToString(), box.transform, new Vector3(0f, 0.02f, boxSize.y * 0.18f), 9f, boxColor));
        }

        CreateLimiterLine("Limitador (entrada)", LimiterStart, true);
        CreateLimiterLine("Limitador (salida)", LimiterEnd, false);

        ApplyHighlight();
    }

    private void CreateLimiterLine(string name, float s, bool entry)
    {
        Vector3 center = OnGround(GetPoint(s));
        Quaternion rotation = Quaternion.LookRotation(GetDirection(s), Vector3.up);

        var line = new GameObject(name);
        line.transform.SetParent(placeholders.transform, false);
        line.transform.SetPositionAndRotation(center, rotation);

        CreateMesh("Línea", line.transform, QuadMesh(halfWidth * 2f, 0.45f, Vector3.zero), limiterColor);

        string text = entry ? "PIT " + Mathf.RoundToInt(speedLimitKmh) : "FIN PIT";
        CreateLabel(text, line.transform, new Vector3(0f, 0.02f, entry ? 2.2f : -2.2f), 7f, limiterColor);
    }

    private void ApplyHighlight()
    {
        for (int i = 0; i < boxOutlines.Count; i++)
        {
            bool own = i == highlightedBox;
            Color color = own ? ownBoxColor : boxColor;

            if (boxOutlines[i] != null)
                boxOutlines[i].material.color = color;

            if (boxFills[i] != null)
                boxFills[i].enabled = own;

            if (boxNumbers[i] != null)
                boxNumbers[i].color = color;
        }
    }

    private MeshRenderer CreateMesh(string name, Transform parent, Mesh mesh, Color color)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;

        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = lineMaterial;
        renderer.material.color = color;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    private static TextMeshPro CreateLabel(string text, Transform parent, Vector3 localPosition, float size, Color color)
    {
        var go = new GameObject("Texto " + text, typeof(TextMeshPro));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;

        // Acostado en el piso, para leerlo llegando.
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        var label = go.GetComponent<TextMeshPro>();
        label.text = text;
        label.fontSize = size;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.color = color;
        label.rectTransform.sizeDelta = new Vector2(4f, 2f);
        return label;
    }

    /// <summary>Altura del piso (sin karts) un poquito arriba, para que la marca no parpadee.</summary>
    private static Vector3 OnGround(Vector3 position)
    {
        RaycastHit[] hits = Physics.RaycastAll(position + Vector3.up * 3f, Vector3.down, 10f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.attachedRigidbody != null || hit.distance >= best)
                continue;

            best = hit.distance;
            position.y = hit.point.y;
        }

        position.y += 0.025f;
        return position;
    }

    private static Mesh OutlineMesh(float width, float length, float line)
    {
        float hw = width * 0.5f;
        float hl = length * 0.5f;

        var mesh = new Mesh { name = "Rectángulo de box" };
        var vertices = new List<Vector3>();
        var triangles = new List<int>();

        AddQuad(vertices, triangles, new Vector3(-hw, 0f, -hl), new Vector3(hw, 0f, -hl + line));
        AddQuad(vertices, triangles, new Vector3(-hw, 0f, hl - line), new Vector3(hw, 0f, hl));
        AddQuad(vertices, triangles, new Vector3(-hw, 0f, -hl), new Vector3(-hw + line, 0f, hl));
        AddQuad(vertices, triangles, new Vector3(hw - line, 0f, -hl), new Vector3(hw, 0f, hl));

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh QuadMesh(float width, float length, Vector3 center)
    {
        var mesh = new Mesh { name = "Marca" };
        var vertices = new List<Vector3>();
        var triangles = new List<int>();

        AddQuad(vertices, triangles, center + new Vector3(-width * 0.5f, 0f, -length * 0.5f), center + new Vector3(width * 0.5f, 0f, length * 0.5f));

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddQuad(List<Vector3> vertices, List<int> triangles, Vector3 min, Vector3 max)
    {
        int start = vertices.Count;

        vertices.Add(new Vector3(min.x, 0f, min.z));
        vertices.Add(new Vector3(min.x, 0f, max.z));
        vertices.Add(new Vector3(max.x, 0f, max.z));
        vertices.Add(new Vector3(max.x, 0f, min.z));

        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
    }

    private static Vector3 Flat(Vector3 vector)
    {
        vector.y = 0f;
        return vector;
    }

    // ------------------------------------------------------------------
    // Editor
    // ------------------------------------------------------------------

#if UNITY_EDITOR
    /// <summary>Lo usa el armado de la calle desde el editor.</summary>
    public void SetLayout(Vector3[] newPath, float newHalfWidth, float newLimiterStart, float newLimiterEnd,
                          int newBoxCount, float newFirstBox, float newSpacing, float newOffset)
    {
        path = newPath;
        halfWidth = newHalfWidth;
        limiterStart = newLimiterStart;
        limiterEnd = newLimiterEnd;
        boxCount = newBoxCount;
        firstBoxDistance = newFirstBox;
        boxSpacing = newSpacing;
        boxOffset = newOffset;
        built = false;
    }

    private void OnDrawGizmosSelected()
    {
        if (!IsValid)
            return;

        // En el editor, siempre en el sentido normal.
        Gizmos.color = Color.cyan;

        for (int i = 0; i < path.Length - 1; i++)
            Gizmos.DrawLine(path[i] + Vector3.up * 0.2f, path[i + 1] + Vector3.up * 0.2f);

        if (Application.isPlaying)
            return;

        built = false;

        Gizmos.color = Color.yellow;

        for (int i = 0; i < boxCount; i++)
        {
            GetBox(i, out Vector3 center, out Quaternion rotation, out _, out _);
            Gizmos.matrix = Matrix4x4.TRS(center + Vector3.up * 0.1f, rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(boxSize.x, 0.05f, boxSize.y));
        }

        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(GetPoint(limiterStart), 0.6f);
        Gizmos.DrawWireSphere(GetPoint(limiterEnd), 0.6f);
    }
#endif
}
