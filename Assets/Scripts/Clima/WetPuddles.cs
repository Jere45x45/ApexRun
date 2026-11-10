using UnityEngine;

/// <summary>
/// Dónde hay charcos, para el código. Es la misma cuenta que hace el shader
/// ApexRun/WetGround (mismo ruido, mismo umbral), así un efecto sabe si la rueda
/// está pisando un charco que se ve.
/// - At(punto, superficie): 0 = sin charco, 1 = charco lleno.
/// - Disturb(punto, fuerza): una rueda cruzó un charco; el shader dibuja anillos ahí.
/// WetTrackVisuals pasa la cantidad y el tamaño de los charcos de cada superficie
/// (Configure) y manda las pisadas al shader (PushToShader).
/// </summary>
public static class WetPuddles
{
    /// <summary>Pisadas que recuerda el shader a la vez (tiene que coincidir con APEX_SPLASHES).</summary>
    public const int MaxSplashes = 32;

    /// <summary>Cuánto dura un anillo en el charco (s); igual que en el shader.</summary>
    public const float SplashLife = 1.6f;

    private static readonly int SplashesId = Shader.PropertyToID("_ApexSplashes");
    private static readonly int TimeId = Shader.PropertyToID("_ApexTime");

    private struct Look
    {
        public float Amount;
        public float Scale;
    }

    // Por defecto, los mismos valores que WetTrackVisuals.
    private static Look road = new Look { Amount = 1f, Scale = 0.22f };
    private static Look gravel = new Look { Amount = 0.6f, Scale = 0.2f };
    private static Look grass = new Look { Amount = 0.3f, Scale = 0.1f };

    private static readonly Vector4[] splashes = new Vector4[MaxSplashes];
    private static int next;
    private static bool dirty = true;

    public static void Configure(TrackSurfaceData.SurfaceEffect effect, float amount, float scale)
    {
        var look = new Look { Amount = Mathf.Clamp01(amount), Scale = Mathf.Max(0.001f, scale) };

        switch (effect)
        {
            case TrackSurfaceData.SurfaceEffect.Gravel: gravel = look; break;
            case TrackSurfaceData.SurfaceEffect.Dirt: grass = look; break;
            default: road = look; break;
        }
    }

    /// <summary>Qué tan dentro de un charco está el punto (0 a 1), con la humedad de ahora.</summary>
    public static float At(Vector3 point, TrackSurfaceData surface)
    {
        float wet = Mathf.Clamp01(TrackConditions.Wetness);

        if (wet <= 0.25f)
            return 0f;

        TrackSurfaceData.SurfaceEffect effect = TrackSurfaceData.EffectOf(surface);

        if (effect == TrackSurfaceData.SurfaceEffect.None)
            return 0f;

        Look look = effect == TrackSurfaceData.SurfaceEffect.Gravel ? gravel
            : effect == TrackSurfaceData.SurfaceEffect.Dirt ? grass
            : road;

        float grow = Mathf.Clamp01((wet - 0.25f) / 0.75f) * look.Amount;

        if (grow < 0.001f)
            return 0f;

        float threshold = Mathf.Lerp(1.05f, 0.61f, grow);
        float noise = PuddleNoise(point.x * look.Scale, point.z * look.Scale);
        return SmoothStep(threshold, threshold + 0.07f, noise);
    }

    /// <summary>
    /// Una rueda cruzó un charco: anillos en ese punto (fuerza 0 a 1). Si ya hay una
    /// pisada muy cerca y reciente, no agrega otra.
    /// </summary>
    public static void Disturb(Vector3 point, float strength)
    {
        if (strength <= 0.01f)
            return;

        float now = Time.time;

        for (int k = 1; k <= 8; k++)
        {
            Vector4 s = splashes[(next - k + MaxSplashes) % MaxSplashes];

            if (s.w > 0f && now - s.z < 0.12f)
            {
                float dx = s.x - point.x;
                float dz = s.y - point.z;

                if (dx * dx + dz * dz < 0.35f * 0.35f)
                    return;
            }
        }

        splashes[next] = new Vector4(point.x, point.z, now, Mathf.Clamp01(strength));
        next = (next + 1) % MaxSplashes;
        dirty = true;
    }

    /// <summary>Manda las pisadas y la hora al shader (una vez por cuadro).</summary>
    public static void PushToShader()
    {
        Shader.SetGlobalFloat(TimeId, Time.time);

        if (!dirty)
            return;

        Shader.SetGlobalVectorArray(SplashesId, splashes);
        dirty = false;
    }

    /// <summary>Borra las pisadas (al salir de la carrera).</summary>
    public static void Clear()
    {
        for (int i = 0; i < MaxSplashes; i++)
            splashes[i] = Vector4.zero;

        next = 0;
        dirty = true;
        PushToShader();
    }

    // ---------- La misma cuenta que el shader ----------

    private static float Frac(float v) => v - Mathf.Floor(v);

    private static float Hash21(float x, float y)
    {
        float px = Frac(x * 123.34f);
        float py = Frac(y * 456.21f);
        float d = px * (px + 45.32f) + py * (py + 45.32f);
        px += d;
        py += d;
        return Frac(px * py);
    }

    private static float ValueNoise(float x, float y)
    {
        float ix = Mathf.Floor(x);
        float iy = Mathf.Floor(y);
        float fx = x - ix;
        float fy = y - iy;
        float ux = fx * fx * (3f - 2f * fx);
        float uy = fy * fy * (3f - 2f * fy);

        float a = Hash21(ix, iy);
        float b = Hash21(ix + 1f, iy);
        float c = Hash21(ix, iy + 1f);
        float d = Hash21(ix + 1f, iy + 1f);

        return Mathf.LerpUnclamped(Mathf.LerpUnclamped(a, b, ux), Mathf.LerpUnclamped(c, d, ux), uy);
    }

    private static float PuddleNoise(float x, float y)
    {
        return ValueNoise(x, y) * 0.6f
            + ValueNoise(x * 2.7f + 5.1f, y * 2.7f + 5.1f) * 0.3f
            + ValueNoise(x * 6.1f + 1.7f, y * 6.1f + 1.7f) * 0.1f;
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }
}
