using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Línea central de la pista: una línea cerrada que sigue el centro del
/// asfalto en el sentido de carrera. Empieza en la línea de llegada.
/// - Sirve para saber cuánto avanzó cada kart (posiciones) y para dibujar el
///   minimapa. Más adelante, también para el reinicio en pista, la contramano
///   y la IA.
/// - Los puntos están en coordenadas del mundo y se guardan en la escena.
/// - Se generan desde la malla de la pista: clic derecho en el componente >
///   "Generar desde la pista". Si cambia el modelo de la pista, hay que volver
///   a generarlos.
/// </summary>
public class TrackCenterline : MonoBehaviour
{
    [Header("Generación")]
    [Tooltip("Collider de la superficie de manejo (el objeto Pista).")]
    [SerializeField] private MeshCollider trackSurface;

    [Tooltip("Línea de llegada. Su eje Z (azul) apunta en el sentido de carrera.")]
    [SerializeField] private Transform startFinishLine;

    [Tooltip("Distancia entre puntos de la línea (m).")]
    [SerializeField, Min(0.5f)] private float pointSpacing = 2f;

    [Header("Datos generados")]
    [SerializeField] private Vector3[] points = new Vector3[0];

    [Tooltip("Ancho medio del asfalto, medido al generar (m).")]
    [SerializeField] private float averageWidth;

    // Distancia acumulada desde el punto 0 hasta cada punto. La última entrada
    // es el largo total (vuelve al punto 0).
    private float[] cumulative;

    public IReadOnlyList<Vector3> Points => points;

    public float AverageWidth => averageWidth;

    public bool IsValid => points != null && points.Length >= 3;

    public float Length
    {
        get
        {
            EnsureCumulative();
            return cumulative[cumulative.Length - 1];
        }
    }

    private void OnValidate()
    {
        cumulative = null;
    }

    /// <summary>
    /// Cuántos metros sobre la línea hay desde la línea de llegada hasta el
    /// punto de la línea más cercano a <paramref name="position"/> (0 a Length).
    /// </summary>
    public float GetDistance(Vector3 position)
    {
        if (!IsValid)
            return 0f;

        EnsureCumulative();

        float bestSqr = float.MaxValue;
        float bestDistance = 0f;

        for (int i = 0; i < points.Length; i++)
        {
            Vector3 a = points[i];
            Vector3 ab = points[(i + 1) % points.Length] - a;

            float t = Mathf.Clamp01(Vector3.Dot(position - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            float sqr = (a + ab * t - position).sqrMagnitude;

            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                bestDistance = cumulative[i] + t * (cumulative[i + 1] - cumulative[i]);
            }
        }

        return bestDistance;
    }

    /// <summary>Punto de la línea a esa distancia de la llegada (da la vuelta).</summary>
    public Vector3 GetPointAtDistance(float distance)
    {
        if (!IsValid)
            return transform.position;

        int segment = FindSegment(distance, out float t);

        return Vector3.Lerp(points[segment], points[(segment + 1) % points.Length], t);
    }

    /// <summary>Hacia dónde va la carrera en ese punto de la línea.</summary>
    public Vector3 GetDirectionAtDistance(float distance)
    {
        if (!IsValid)
            return transform.forward;

        int segment = FindSegment(distance, out _);

        return (points[(segment + 1) % points.Length] - points[segment]).normalized;
    }

    private int FindSegment(float distance, out float t)
    {
        EnsureCumulative();

        float length = cumulative[cumulative.Length - 1];
        distance = Mathf.Repeat(distance, length);

        // Búsqueda binaria del tramo que contiene la distancia.
        int low = 0;
        int high = points.Length - 1;

        while (low < high)
        {
            int middle = (low + high + 1) / 2;

            if (cumulative[middle] <= distance)
                low = middle;
            else
                high = middle - 1;
        }

        float segmentLength = cumulative[low + 1] - cumulative[low];
        t = segmentLength > 1e-6f ? (distance - cumulative[low]) / segmentLength : 0f;

        return low;
    }

    private void EnsureCumulative()
    {
        int count = points != null ? points.Length : 0;

        if (cumulative != null && cumulative.Length == count + 1)
            return;

        cumulative = new float[count + 1];

        for (int i = 1; i <= count; i++)
        {
            cumulative[i] = cumulative[i - 1] + Vector3.Distance(points[i - 1], points[i % count]);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!IsValid)
            return;

        Gizmos.color = Color.yellow;

        for (int i = 0; i < points.Length; i++)
        {
            Gizmos.DrawLine(points[i], points[(i + 1) % points.Length]);
        }

        // Una marca cada 50 m y la llegada en verde, con el sentido de carrera.
        Gizmos.color = Color.cyan;

        for (float d = 50f; d < Length; d += 50f)
        {
            Gizmos.DrawSphere(GetPointAtDistance(d), 0.6f);
        }

        Gizmos.color = Color.green;
        Gizmos.DrawSphere(points[0], 1f);
        Gizmos.DrawLine(points[0], points[0] + GetDirectionAtDistance(0f) * 6f);
    }

#if UNITY_EDITOR
    /// <summary>
    /// Genera la línea desde la malla de la pista:
    /// 1. Toma las caras que miran hacia arriba (la superficie de manejo).
    /// 2. Encuentra sus dos bordes, el exterior y el interior.
    /// 3. Recorre el borde exterior y, en cada punto, toma el punto medio con el
    ///    punto más cercano del borde interior.
    /// 4. Suaviza, reparte los puntos a distancias iguales y los apoya sobre el
    ///    asfalto.
    /// 5. Empieza en la línea de llegada y sigue el sentido de carrera.
    /// </summary>
    [ContextMenu("Generar desde la pista")]
    private void GenerateFromTrack()
    {
        if (trackSurface == null || trackSurface.sharedMesh == null)
        {
            Debug.LogError("TrackCenterline necesita el MeshCollider de la pista.", this);
            return;
        }

        if (startFinishLine == null)
        {
            Debug.LogError("TrackCenterline necesita la línea de llegada.", this);
            return;
        }

        List<List<Vector3>> borders = FindBorders(trackSurface);

        if (borders.Count < 2)
        {
            Debug.LogError($"Se esperaban dos bordes (exterior e interior) y se encontraron {borders.Count}.", this);
            return;
        }

        // El más largo es el exterior y el siguiente, el interior.
        borders.Sort((a, b) => LoopLength(b).CompareTo(LoopLength(a)));

        List<Vector3> outer = ResampleLoop(borders[0], 1f);
        List<Vector3> inner = borders[1];

        List<Vector3> center = new List<Vector3>(outer.Count);
        float widthSum = 0f;

        foreach (Vector3 outerPoint in outer)
        {
            Vector3 innerPoint = ClosestPointOnLoop(inner, outerPoint);

            center.Add((outerPoint + innerPoint) * 0.5f);
            widthSum += Vector3.Distance(outerPoint, innerPoint);
        }

        center = SmoothLoop(center, 10);
        center = ResampleLoop(center, pointSpacing);

        int missed = PlaceOnSurface(center);

        StartAtFinishLine(center);

        Undo.RecordObject(this, "Generar línea central");

        points = center.ToArray();
        averageWidth = widthSum / outer.Count;
        cumulative = null;

        EditorUtility.SetDirty(this);

        Debug.Log(
            $"Línea central: {points.Length} puntos, {Length:F0} m, ancho medio {averageWidth:F1} m. " +
            $"Puntos que no cayeron sobre el asfalto: {missed}.",
            this);
    }

    private static List<List<Vector3>> FindBorders(MeshCollider surface)
    {
        Mesh mesh = surface.sharedMesh;
        Transform meshTransform = surface.transform;

        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;

        // Une los vértices que están en el mismo lugar (el FBX los repite por
        // normales o UV distintas).
        List<Vector3> welded = new List<Vector3>();
        int[] weldedIndex = new int[vertices.Length];
        Dictionary<Vector3Int, int> lookup = new Dictionary<Vector3Int, int>();

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 world = meshTransform.TransformPoint(vertices[i]);
            Vector3Int key = Vector3Int.RoundToInt(world * 500f);

            if (!lookup.TryGetValue(key, out int index))
            {
                index = welded.Count;
                welded.Add(world);
                lookup.Add(key, index);
            }

            weldedIndex[i] = index;
        }

        // Cuenta cuántas caras de la superficie de manejo usan cada arista.
        Dictionary<long, int> edgeUse = new Dictionary<long, int>();

        for (int i = 0; i < triangles.Length; i += 3)
        {
            int a = weldedIndex[triangles[i]];
            int b = weldedIndex[triangles[i + 1]];
            int c = weldedIndex[triangles[i + 2]];

            Vector3 normal = Vector3.Cross(welded[b] - welded[a], welded[c] - welded[a]).normalized;

            if (normal.y <= 0.5f)
                continue;

            CountEdge(edgeUse, a, b);
            CountEdge(edgeUse, b, c);
            CountEdge(edgeUse, c, a);
        }

        // Las aristas que usa una sola cara forman el borde.
        Dictionary<int, List<int>> neighbours = new Dictionary<int, List<int>>();

        foreach (KeyValuePair<long, int> edge in edgeUse)
        {
            if (edge.Value != 1)
                continue;

            int a = (int)(edge.Key >> 32);
            int b = (int)(edge.Key & 0xffffffffL);

            AddNeighbour(neighbours, a, b);
            AddNeighbour(neighbours, b, a);
        }

        // Encadena las aristas del borde en lazos cerrados.
        List<List<Vector3>> loops = new List<List<Vector3>>();
        HashSet<int> visited = new HashSet<int>();

        foreach (int start in neighbours.Keys)
        {
            if (visited.Contains(start))
                continue;

            List<Vector3> loop = new List<Vector3>();
            int current = start;

            while (current >= 0 && visited.Add(current))
            {
                loop.Add(welded[current]);

                int next = -1;

                foreach (int candidate in neighbours[current])
                {
                    if (!visited.Contains(candidate))
                    {
                        next = candidate;
                        break;
                    }
                }

                current = next;
            }

            if (loop.Count >= 3)
                loops.Add(loop);
        }

        return loops;
    }

    private static void CountEdge(Dictionary<long, int> edgeUse, int a, int b)
    {
        long key = ((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);

        edgeUse.TryGetValue(key, out int count);
        edgeUse[key] = count + 1;
    }

    private static void AddNeighbour(Dictionary<int, List<int>> neighbours, int from, int to)
    {
        if (!neighbours.TryGetValue(from, out List<int> list))
        {
            list = new List<int>(2);
            neighbours.Add(from, list);
        }

        list.Add(to);
    }

    private static float LoopLength(List<Vector3> loop)
    {
        float length = 0f;

        for (int i = 0; i < loop.Count; i++)
        {
            length += Vector3.Distance(loop[i], loop[(i + 1) % loop.Count]);
        }

        return length;
    }

    private static Vector3 ClosestPointOnLoop(List<Vector3> loop, Vector3 point)
    {
        Vector3 best = loop[0];
        float bestSqr = float.MaxValue;

        for (int i = 0; i < loop.Count; i++)
        {
            Vector3 a = loop[i];
            Vector3 ab = loop[(i + 1) % loop.Count] - a;

            float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            Vector3 candidate = a + ab * t;
            float sqr = (candidate - point).sqrMagnitude;

            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>Reparte los puntos de un lazo cerrado a distancias iguales.</summary>
    private static List<Vector3> ResampleLoop(List<Vector3> loop, float spacing)
    {
        float total = LoopLength(loop);
        int count = Mathf.Max(3, Mathf.RoundToInt(total / spacing));
        float step = total / count;

        List<Vector3> result = new List<Vector3>(count);

        int segment = 0;
        float segmentStart = 0f;

        for (int k = 0; k < count; k++)
        {
            float target = k * step;

            // Avanza hasta el tramo que contiene la distancia buscada.
            while (segment < loop.Count - 1 &&
                   segmentStart + Vector3.Distance(loop[segment], loop[segment + 1]) < target)
            {
                segmentStart += Vector3.Distance(loop[segment], loop[segment + 1]);
                segment++;
            }

            Vector3 a = loop[segment];
            Vector3 b = loop[(segment + 1) % loop.Count];
            float length = Vector3.Distance(a, b);
            float t = length > 1e-6f ? (target - segmentStart) / length : 0f;

            result.Add(Vector3.Lerp(a, b, Mathf.Clamp01(t)));
        }

        return result;
    }

    private static List<Vector3> SmoothLoop(List<Vector3> loop, int iterations)
    {
        int count = loop.Count;
        Vector3[] current = loop.ToArray();
        Vector3[] next = new Vector3[count];

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            for (int i = 0; i < count; i++)
            {
                next[i] = (current[(i - 1 + count) % count] + 2f * current[i] + current[(i + 1) % count]) * 0.25f;
            }

            Vector3[] swap = current;
            current = next;
            next = swap;
        }

        return new List<Vector3>(current);
    }

    /// <summary>Apoya cada punto sobre el asfalto. Devuelve cuántos no lo tocaron.</summary>
    private int PlaceOnSurface(List<Vector3> line)
    {
        int missed = 0;

        for (int i = 0; i < line.Count; i++)
        {
            Ray ray = new Ray(line[i] + Vector3.up * 5f, Vector3.down);

            if (trackSurface.Raycast(ray, out RaycastHit hit, 20f))
                line[i] = hit.point;
            else
                missed++;
        }

        return missed;
    }

    /// <summary>
    /// Ordena la línea para que el punto 0 sea el más cercano a la línea de
    /// llegada y para que avance en el sentido de carrera.
    /// </summary>
    private void StartAtFinishLine(List<Vector3> line)
    {
        int count = line.Count;
        int start = 0;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            float sqr = (line[i] - startFinishLine.position).sqrMagnitude;

            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                start = i;
            }
        }

        Vector3 direction = line[(start + 1) % count] - line[start];
        bool reversed = Vector3.Dot(direction, startFinishLine.forward) < 0f;

        List<Vector3> ordered = new List<Vector3>(count);

        for (int k = 0; k < count; k++)
        {
            int index = reversed ? start - k : start + k;
            ordered.Add(line[(index % count + count) % count]);
        }

        line.Clear();
        line.AddRange(ordered);
    }
#endif
}
