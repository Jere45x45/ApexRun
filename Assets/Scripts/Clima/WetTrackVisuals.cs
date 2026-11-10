using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// La pista se ve mojada cuando llueve (va en el objeto Clima de Race, al lado de WeatherVisuals).
/// - Piso (lo que tiene TrackSurface): copias de sus materiales con el shader ApexRun/WetGround,
///   que se oscurece, brilla y hace charcos según TrackConditions.Wetness. Los charcos tienen
///   ondas mientras llueve.
/// - Lo demás de la pista (barreras, conos, estructuras): copias del Standard un poco más oscuras
///   y brillantes.
/// - Reflejo plano: una cámara espejada bajo el piso dibuja karts, barreras y cielo para que se
///   vean en el asfalto mojado y en los charcos.
/// Los materiales originales no se tocan: al salir de la carrera vuelve todo como estaba.
/// Con la pista seca no cambia nada (ni el shader, ni el costo del reflejo).
/// </summary>
public class WetTrackVisuals : MonoBehaviour
{
    [System.Serializable]
    public class WetLook
    {
        [Tooltip("Cuánto se oscurece mojado (0 = nada, 1 = negro).")]
        [Range(0f, 1f)] public float darken = 0.45f;

        [Tooltip("Brillo del piso mojado.")]
        [Range(0f, 1f)] public float smoothness = 0.75f;

        [Tooltip("Cantidad de charcos con la pista empapada (0 = ninguno).")]
        [Range(0f, 1f)] public float puddles = 1f;

        [Tooltip("Tamaño de los charcos: más chico = charcos más grandes (1/m).")]
        public float puddleScale = 0.14f;

        public WetLook() { }

        public WetLook(float darken, float smoothness, float puddles, float puddleScale)
        {
            this.darken = darken;
            this.smoothness = smoothness;
            this.puddles = puddles;
            this.puddleScale = puddleScale;
        }
    }

    [Tooltip("Shader del piso mojado (ApexRun/WetGround). Tiene que estar asignado para que entre en el build.")]
    [SerializeField] private Shader wetGroundShader;

    [Header("Cómo se moja cada superficie")]
    [SerializeField] private WetLook road = new WetLook(0.35f, 0.66f, 1f, 0.14f);
    [SerializeField] private WetLook gravel = new WetLook(0.3f, 0.5f, 0.6f, 0.2f);
    [SerializeField] private WetLook grass = new WetLook(0.25f, 0.4f, 0.3f, 0.1f);

    [Header("Lo que no es piso (barreras, conos, estructuras)")]
    [SerializeField, Range(0f, 1f)] private float propDarken = 0.2f;
    [SerializeField, Range(0f, 1f)] private float propSmoothness = 0.6f;

    [Header("Reflejo")]
    [Tooltip("Refleja karts, barreras y cielo en la pista mojada (cuesta un dibujo más de la escena por cuadro).")]
    [SerializeField] private bool planarReflection = true;

    [Tooltip("Resolución del reflejo respecto de la pantalla.")]
    [SerializeField, Range(0.25f, 1f)] private float reflectionScale = 0.5f;

    [Tooltip("Por debajo de esta calidad (índice de Quality) no hay reflejo plano.")]
    [SerializeField] private int minQualityForReflection = 2;

    [Tooltip("Desde qué humedad se arma el reflejo.")]
    [SerializeField, Range(0f, 1f)] private float reflectionFrom = 0.03f;

    private static readonly int WetnessId = Shader.PropertyToID("_ApexWetness");
    private static readonly int RainId = Shader.PropertyToID("_ApexRain");
    private static readonly int PlanarOnId = Shader.PropertyToID("_ApexPlanarOn");
    private static readonly int PlanarTexId = Shader.PropertyToID("_ApexPlanarReflection");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int GlossId = Shader.PropertyToID("_Glossiness");

    private class PropMaterial
    {
        public Material copy;
        public Color color;
        public float gloss;
    }

    // Renderers tocados y sus materiales originales.
    private readonly Dictionary<Renderer, Material[]> originals = new Dictionary<Renderer, Material[]>();
    private readonly List<Material> copies = new List<Material>();
    private readonly List<PropMaterial> props = new List<PropMaterial>();
    private readonly List<Renderer> groundRenderers = new List<Renderer>();
    private readonly List<Collider> groundColliders = new List<Collider>();

    private bool swapped;
    private float lastWetness = -1f;

    // Reflejo plano.
    private Camera reflectionCamera;
    private readonly Dictionary<Camera, RenderTexture> textures = new Dictionary<Camera, RenderTexture>();
    private bool renderingReflection;
    private float planeHeight;
    private bool hasPlaneHeight;

    private void OnEnable()
    {
        Camera.onPreCull += OnCameraPreCull;
    }

    private void OnDisable()
    {
        Camera.onPreCull -= OnCameraPreCull;
        Shader.SetGlobalFloat(PlanarOnId, 0f);
    }

    private void OnDestroy()
    {
        Restore();

        foreach (var rt in textures.Values)
            if (rt != null)
            {
                rt.Release();
                Destroy(rt);
            }
        textures.Clear();

        if (reflectionCamera != null)
            Destroy(reflectionCamera.gameObject);

        Shader.SetGlobalFloat(WetnessId, 0f);
        Shader.SetGlobalFloat(RainId, 0f);
        Shader.SetGlobalFloat(PlanarOnId, 0f);
    }

    private void LateUpdate()
    {
        float wet = TrackConditions.Active ? Mathf.Clamp01(TrackConditions.Wetness) : 0f;
        float rain = TrackConditions.Active ? Mathf.Clamp01(TrackConditions.Rain) : 0f;

        // Hasta que no se moja, la pista sigue con sus materiales de siempre.
        if (!swapped && wet > 0.001f)
            Swap();

        if (!swapped)
            return;

        Shader.SetGlobalFloat(WetnessId, wet);
        Shader.SetGlobalFloat(RainId, rain);

        if (Mathf.Abs(wet - lastWetness) > 0.002f)
        {
            lastWetness = wet;

            foreach (var p in props)
            {
                p.copy.SetColor(ColorId, p.color * Mathf.Lerp(1f, 1f - propDarken, wet));
                p.copy.SetFloat(GlossId, Mathf.Lerp(p.gloss, Mathf.Max(p.gloss, propSmoothness), wet));
            }
        }
    }

    // ---------- Materiales ----------

    private void Swap()
    {
        swapped = true;

        if (wetGroundShader == null)
            wetGroundShader = Shader.Find("ApexRun/WetGround");

        var groundCopies = new Dictionary<Material, Material>();
        var propCopies = new Dictionary<Material, Material>();
        var roots = new HashSet<Transform>();

        foreach (var surface in FindObjectsByType<TrackSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var renderer = surface.GetComponent<Renderer>();
            if (renderer == null)
                continue;

            roots.Add(surface.transform.root);
            groundRenderers.Add(renderer);

            foreach (var c in surface.GetComponents<Collider>())
                groundColliders.Add(c);

            WetLook look = LookFor(surface.SurfaceData);
            Material[] mats = renderer.sharedMaterials;
            Material[] swappedMats = new Material[mats.Length];

            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                swappedMats[i] = m;

                if (!CanUseWetShader(m) || wetGroundShader == null)
                    continue;

                if (!groundCopies.TryGetValue(m, out Material copy))
                {
                    copy = new Material(m) { name = m.name + " (mojado)" };
                    copy.shader = wetGroundShader;
                    copy.SetFloat("_WetDarken", look.darken);
                    copy.SetFloat("_WetSmoothness", look.smoothness);
                    copy.SetFloat("_PuddleAmount", look.puddles);
                    copy.SetFloat("_PuddleScale", look.puddleScale);
                    groundCopies[m] = copy;
                    copies.Add(copy);
                }

                swappedMats[i] = copy;
            }

            originals[renderer] = mats;
            renderer.sharedMaterials = swappedMats;
        }

        // Lo demás de los modelos de la pista.
        foreach (Transform root in roots)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (originals.ContainsKey(renderer) || renderer is ParticleSystemRenderer)
                    continue;

                Material[] mats = renderer.sharedMaterials;
                Material[] swappedMats = new Material[mats.Length];
                bool any = false;

                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    swappedMats[i] = m;

                    if (!IsOpaqueStandard(m) || m.name.StartsWith("Default-"))
                        continue;

                    if (!propCopies.TryGetValue(m, out Material copy))
                    {
                        copy = new Material(m) { name = m.name + " (mojado)" };
                        propCopies[m] = copy;
                        copies.Add(copy);
                        props.Add(new PropMaterial { copy = copy, color = m.GetColor(ColorId), gloss = m.GetFloat(GlossId) });
                    }

                    swappedMats[i] = copy;
                    any = true;
                }

                if (!any)
                    continue;

                originals[renderer] = mats;
                renderer.sharedMaterials = swappedMats;
            }
        }
    }

    private void Restore()
    {
        foreach (var pair in originals)
            if (pair.Key != null)
                pair.Key.sharedMaterials = pair.Value;
        originals.Clear();

        foreach (var m in copies)
            if (m != null)
                Destroy(m);
        copies.Clear();
        props.Clear();
        groundRenderers.Clear();
        groundColliders.Clear();
        swapped = false;
    }

    private WetLook LookFor(TrackSurfaceData data)
    {
        switch (TrackSurfaceData.EffectOf(data))
        {
            case TrackSurfaceData.SurfaceEffect.Gravel: return gravel;
            case TrackSurfaceData.SurfaceEffect.Dirt: return grass;
            default: return road;
        }
    }

    private static bool IsOpaqueStandard(Material m)
    {
        return m != null && m.shader != null && m.shader.name == "Standard"
            && (!m.HasProperty("_Mode") || m.GetFloat("_Mode") == 0f);
    }

    // El shader mojado imita al Standard simple: color, textura, normal, brillo y metal.
    private static bool CanUseWetShader(Material m)
    {
        if (!IsOpaqueStandard(m))
            return false;

        return !m.IsKeywordEnabled("_METALLICGLOSSMAP")
            && !m.IsKeywordEnabled("_EMISSION")
            && !m.IsKeywordEnabled("_DETAIL_MULX2")
            && !m.IsKeywordEnabled("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
    }

    // ---------- Reflejo plano ----------

    private bool ReflectionWanted()
    {
        return planarReflection
            && swapped
            && TrackConditions.Active
            && TrackConditions.Wetness >= reflectionFrom
            && QualitySettings.GetQualityLevel() >= minQualityForReflection;
    }

    private void OnCameraPreCull(Camera cam)
    {
        if (renderingReflection || cam == reflectionCamera)
            return;

        if (!ReflectionWanted() || cam.cameraType != CameraType.Game || cam.orthographic)
        {
            Shader.SetGlobalFloat(PlanarOnId, 0f);
            return;
        }

        RenderReflection(cam);
    }

    private void RenderReflection(Camera cam)
    {
        UpdatePlaneHeight(cam);

        RenderTexture rt = TextureFor(cam);
        Camera rc = ReflectionCamera();

        rc.CopyFrom(cam);
        rc.enabled = false;
        rc.targetTexture = rt;
        rc.useOcclusionCulling = false;
        rc.cullingMask = cam.cullingMask & ~(1 << 5); // sin UI

        Vector3 normal = Vector3.up;
        Vector3 pos = new Vector3(0f, planeHeight, 0f);
        float d = -Vector3.Dot(normal, pos);
        Vector4 plane = new Vector4(normal.x, normal.y, normal.z, d);

        Matrix4x4 reflection = ReflectionMatrix(plane);
        rc.worldToCameraMatrix = cam.worldToCameraMatrix * reflection;
        rc.transform.position = reflection.MultiplyPoint(cam.transform.position);

        Vector4 clip = CameraSpacePlane(rc.worldToCameraMatrix, pos, normal, 0.05f);
        rc.projectionMatrix = cam.CalculateObliqueMatrix(clip);
        rc.cullingMatrix = rc.projectionMatrix * rc.worldToCameraMatrix;

        // El piso no se dibuja en su propio reflejo; sin sombras para que cueste menos.
        foreach (var r in groundRenderers)
            if (r != null)
                r.forceRenderingOff = true;

        float shadowDistance = QualitySettings.shadowDistance;
        QualitySettings.shadowDistance = 0f;

        renderingReflection = true;
        bool invert = GL.invertCulling;
        GL.invertCulling = !invert;

        try
        {
            rc.Render();
        }
        finally
        {
            GL.invertCulling = invert;
            renderingReflection = false;
            QualitySettings.shadowDistance = shadowDistance;

            foreach (var r in groundRenderers)
                if (r != null)
                    r.forceRenderingOff = false;
        }

        Shader.SetGlobalTexture(PlanarTexId, rt);
        Shader.SetGlobalFloat(PlanarOnId, 1f);
    }

    private void UpdatePlaneHeight(Camera cam)
    {
        Ray ray = new Ray(cam.transform.position + Vector3.up * 20f, Vector3.down);
        float best = float.NegativeInfinity;

        foreach (var c in groundColliders)
        {
            if (c != null && c.enabled && c.Raycast(ray, out RaycastHit hit, 200f))
                best = Mathf.Max(best, hit.point.y);
        }

        if (float.IsNegativeInfinity(best))
            return;

        planeHeight = hasPlaneHeight ? Mathf.Lerp(planeHeight, best, 0.2f) : best;
        hasPlaneHeight = true;
    }

    private RenderTexture TextureFor(Camera cam)
    {
        int w = Mathf.Max(64, Mathf.RoundToInt(cam.pixelWidth * reflectionScale));
        int h = Mathf.Max(64, Mathf.RoundToInt(cam.pixelHeight * reflectionScale));

        if (textures.TryGetValue(cam, out RenderTexture rt) && rt != null && rt.width == w && rt.height == h)
            return rt;

        if (rt != null)
        {
            rt.Release();
            Destroy(rt);
        }

        var format = cam.allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;
        rt = new RenderTexture(w, h, 24, format)
        {
            name = "ReflejoPista",
            useMipMap = true,
            autoGenerateMips = true,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Trilinear,
            hideFlags = HideFlags.DontSave
        };
        rt.Create();
        textures[cam] = rt;
        return rt;
    }

    private Camera ReflectionCamera()
    {
        if (reflectionCamera != null)
            return reflectionCamera;

        var go = new GameObject("ReflejoPista") { hideFlags = HideFlags.HideAndDontSave };
        reflectionCamera = go.AddComponent<Camera>();
        reflectionCamera.enabled = false;
        return reflectionCamera;
    }

    private static Vector4 CameraSpacePlane(Matrix4x4 worldToCamera, Vector3 pos, Vector3 normal, float offset)
    {
        Vector3 p = worldToCamera.MultiplyPoint(pos + normal * offset);
        Vector3 n = worldToCamera.MultiplyVector(normal).normalized;
        return new Vector4(n.x, n.y, n.z, -Vector3.Dot(p, n));
    }

    private static Matrix4x4 ReflectionMatrix(Vector4 plane)
    {
        Matrix4x4 m = Matrix4x4.identity;
        m.m00 = 1f - 2f * plane.x * plane.x;
        m.m01 = -2f * plane.x * plane.y;
        m.m02 = -2f * plane.x * plane.z;
        m.m03 = -2f * plane.w * plane.x;

        m.m10 = -2f * plane.y * plane.x;
        m.m11 = 1f - 2f * plane.y * plane.y;
        m.m12 = -2f * plane.y * plane.z;
        m.m13 = -2f * plane.w * plane.y;

        m.m20 = -2f * plane.z * plane.x;
        m.m21 = -2f * plane.z * plane.y;
        m.m22 = 1f - 2f * plane.z * plane.z;
        m.m23 = -2f * plane.w * plane.z;

        m.m30 = 0f;
        m.m31 = 0f;
        m.m32 = 0f;
        m.m33 = 1f;
        return m;
    }
}
