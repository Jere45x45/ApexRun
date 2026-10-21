using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Datos de la pista que usan los bots, sacados de la línea central
/// (TrackCenterline) y del asfalto (su TrackSurface):
/// - Cuánto lugar hay a cada lado de la línea, cada 2 m. Es el lugar
///   utilizable: lo que queda hasta el borde menos medio kart y un margen.
/// - Qué tan cerrada es la curva en cada punto (curvatura, 1/radio).
/// Con eso se arma la trazada de cada bot: una "fracción" de −1 (todo a la
/// izquierda) a 1 (todo a la derecha) que se convierte en metros según el
/// ancho de la pista en ese lugar.
/// Se mide una sola vez por pista, la primera vez que un bot la pide.
/// </summary>
public class BotTrackInfo
{
    public const float SampleSpacing = 2f;

    // Medio kart (de costado) y el margen que se deja hasta el borde (m).
    private const float KartHalfWidth = 0.75f;
    private const float EdgeMargin = 0.35f;

    // Hasta dónde se busca el borde de la pista a cada lado (m).
    private const float MaxSearch = 16f;

    private static readonly Dictionary<TrackCenterline, BotTrackInfo> Cache =
        new Dictionary<TrackCenterline, BotTrackInfo>();

    private readonly Collider surface;
    private readonly float length;
    private readonly int count;
    private readonly float[] left;
    private readonly float[] right;
    private readonly float[] curvature;

    public TrackCenterline Centerline { get; }

    public float Length => length;

    /// <summary>Los datos de esa pista (se miden la primera vez). Null si la línea no sirve.</summary>
    public static BotTrackInfo Get(TrackCenterline centerline)
    {
        if (centerline == null || !centerline.IsValid)
            return null;

        if (Cache.TryGetValue(centerline, out BotTrackInfo info) && Mathf.Abs(info.length - centerline.Length) < 0.01f)
            return info;

        // Las pistas de escenas que ya se cerraron.
        List<TrackCenterline> gone = null;

        foreach (TrackCenterline key in Cache.Keys)
        {
            if (key == null)
                (gone ??= new List<TrackCenterline>()).Add(key);
        }

        if (gone != null)
        {
            foreach (TrackCenterline key in gone)
                Cache.Remove(key);
        }

        info = new BotTrackInfo(centerline);
        Cache[centerline] = info;
        return info;
    }

    private BotTrackInfo(TrackCenterline centerline)
    {
        Centerline = centerline;
        surface = centerline.TrackSurface;
        length = centerline.Length;
        count = Mathf.Max(3, Mathf.CeilToInt(length / SampleSpacing));

        left = new float[count];
        right = new float[count];
        curvature = new float[count];

        float[] rawLeft = new float[count];
        float[] rawRight = new float[count];
        float[] rawCurvature = new float[count];

        for (int i = 0; i < count; i++)
        {
            float distance = i * length / count;
            Vector3 point = centerline.GetPointAtDistance(distance);
            Vector3 side = GetRightVector(distance);

            rawRight[i] = Usable(MeasureEdge(point, side));
            rawLeft[i] = Usable(MeasureEdge(point, -side));

            Vector3 before = Flat(centerline.GetDirectionAtDistance(distance - 5f));
            Vector3 after = Flat(centerline.GetDirectionAtDistance(distance + 5f));
            rawCurvature[i] = Vector3.SignedAngle(before, after, Vector3.up) * Mathf.Deg2Rad / 10f;
        }

        // Ancho: el menor de los vecinos (por las costuras de la malla).
        // Curva: promedio de los vecinos.
        for (int i = 0; i < count; i++)
        {
            float minLeft = float.MaxValue;
            float minRight = float.MaxValue;
            float sum = 0f;

            for (int k = -2; k <= 2; k++)
            {
                int j = (i + k + count) % count;

                if (k >= -1 && k <= 1)
                {
                    minLeft = Mathf.Min(minLeft, rawLeft[j]);
                    minRight = Mathf.Min(minRight, rawRight[j]);
                }

                sum += rawCurvature[j];
            }

            left[i] = minLeft;
            right[i] = minRight;
            curvature[i] = sum / 5f;
        }
    }

    /// <summary>Lugar utilizable a la izquierda de la línea central (m).</summary>
    public float GetUsableLeft(float distance) => Sample(left, distance);

    /// <summary>Lugar utilizable a la derecha de la línea central (m).</summary>
    public float GetUsableRight(float distance) => Sample(right, distance);

    /// <summary>Medio ancho utilizable (promedio de los dos lados, m).</summary>
    public float GetHalfWidth(float distance) => 0.5f * (GetUsableLeft(distance) + GetUsableRight(distance));

    /// <summary>Curvatura en ese punto (1/m): positiva si dobla a la derecha.</summary>
    public float GetCurvature(float distance) => Sample(curvature, distance);

    /// <summary>Hacia la derecha de la pista en ese punto (horizontal).</summary>
    public Vector3 GetRightVector(float distance)
    {
        return Vector3.Cross(Vector3.up, Flat(Centerline.GetDirectionAtDistance(distance))).normalized;
    }

    /// <summary>Corrimiento desde la línea central (m, + = derecha) para una fracción del lugar utilizable.</summary>
    public float GetLateralOffset(float distance, float fraction)
    {
        return fraction >= 0f ? fraction * GetUsableRight(distance) : fraction * GetUsableLeft(distance);
    }

    /// <summary>Punto de la trazada: la línea central corrida esa fracción del lugar utilizable.</summary>
    public Vector3 GetPoint(float distance, float fraction)
    {
        return Centerline.GetPointAtDistance(distance) + GetRightVector(distance) * GetLateralOffset(distance, fraction);
    }

    /// <summary>En qué fracción del lugar utilizable está una posición (−1 a la izquierda, 1 a la derecha; puede pasarse).</summary>
    public float GetFraction(Vector3 position, float distance)
    {
        float offset = Vector3.Dot(position - Centerline.GetPointAtDistance(distance), GetRightVector(distance));

        return offset >= 0f
            ? offset / Mathf.Max(0.3f, GetUsableRight(distance))
            : offset / Mathf.Max(0.3f, GetUsableLeft(distance));
    }

    /// <summary>True si debajo de esa posición hay asfalto.</summary>
    public bool IsOnAsphalt(Vector3 position)
    {
        if (surface == null)
            return true;

        return surface.Raycast(new Ray(position + Vector3.up, Vector3.down), out _, 4f);
    }

    /// <summary>Apoya un punto sobre el asfalto (si lo encuentra) y devuelve la normal del piso.</summary>
    public Vector3 PlaceOnAsphalt(Vector3 position, out Vector3 normal)
    {
        normal = Vector3.up;

        if (surface != null && surface.Raycast(new Ray(position + Vector3.up * 3f, Vector3.down), out RaycastHit hit, 8f))
        {
            normal = hit.normal;
            return hit.point;
        }

        return position;
    }

    private float Sample(float[] values, float distance)
    {
        float position = Mathf.Repeat(distance, length) / length * count;
        int index = Mathf.FloorToInt(position) % count;
        float t = position - Mathf.Floor(position);

        return Mathf.Lerp(values[index], values[(index + 1) % count], t);
    }

    /// <summary>Hasta dónde hay asfalto desde el punto, hacia ese lado (m).</summary>
    private float MeasureEdge(Vector3 point, Vector3 side)
    {
        if (surface == null)
            return 4f;

        for (float offset = 0.5f; offset <= MaxSearch; offset += 0.5f)
        {
            if (HasAsphalt(point + side * offset))
                continue;

            // Afina entre el último punto con asfalto y el primero sin.
            float low = offset - 0.5f;
            float high = offset;

            for (int i = 0; i < 4; i++)
            {
                float middle = 0.5f * (low + high);

                if (HasAsphalt(point + side * middle))
                    low = middle;
                else
                    high = middle;
            }

            return low;
        }

        return MaxSearch;
    }

    private bool HasAsphalt(Vector3 point)
    {
        return surface.Raycast(new Ray(point + Vector3.up * 3f, Vector3.down), out _, 8f);
    }

    private static float Usable(float edge)
    {
        return Mathf.Max(0.3f, edge - KartHalfWidth - EdgeMargin);
    }

    private static Vector3 Flat(Vector3 vector)
    {
        vector.y = 0f;
        return vector.sqrMagnitude > 1e-6f ? vector.normalized : Vector3.forward;
    }
}
