using UnityEngine;

/// <summary>
/// Lo que se ve de la lluvia en esta computadora, según TrackConditions.Rain
/// (lo decide el servidor, ver RaceWeather):
/// - Gotas: un sistema de partículas que acompaña a la cámara (va donde va la
///   cámara y mira para donde mira, así siempre llueve alrededor del jugador).
/// - Cielo y luz más oscuros: baja el sol, la luz ambiente y el brillo del cielo.
/// - Un poco de bruma (niebla suave) mientras llueve.
/// Todo cambia de a poco y vuelve a como estaba la escena cuando para.
/// No cambia nada del manejo: eso lo hace la humedad de la pista en KartWheel.
/// </summary>
public class RainEffect : MonoBehaviour
{
    [Header("Gotas")]
    [Tooltip("Material de las gotas (Particles/Standard Unlit con la textura de partícula por defecto).")]
    [SerializeField] private Material dropMaterial;

    [Tooltip("Gotas por segundo con lluvia fuerte.")]
    [SerializeField] private float maxDropsPerSecond = 4500f;

    [Tooltip("Velocidad de caída de las gotas (m/s).")]
    [SerializeField] private float dropSpeed = 20f;

    [Tooltip("Tamaño de la zona donde llueve alrededor de la cámara (ancho, alto, largo en m).")]
    [SerializeField] private Vector3 areaSize = new Vector3(36f, 1f, 36f);

    [Tooltip("Dónde está la zona respecto de la cámara: arriba y un poco adelante (m).")]
    [SerializeField] private Vector3 areaOffset = new Vector3(0f, 14f, 8f);

    [SerializeField] private Color dropColor = new Color(0.82f, 0.86f, 0.92f, 0.6f);

    [Tooltip("Grosor de las gotas (m).")]
    [SerializeField] private float dropWidth = 0.05f;

    [Tooltip("Largo de la estela de cada gota: fracción de su velocidad (0,06 a 20 m/s = 1,2 m).")]
    [SerializeField] private float streakLength = 0.06f;

    [Header("Cielo y luz con lluvia fuerte")]
    [Tooltip("Desde esta fuerza de lluvia el cielo ya está cubierto del todo (con menos, a medias).")]
    [SerializeField, Range(0.05f, 1f)] private float fullOvercastAt = 0.5f;

    [Tooltip("Luz del sol con lluvia fuerte (fracción de la normal).")]
    [SerializeField, Range(0f, 1f)] private float sunIntensity = 0.35f;

    [SerializeField] private Color sunTint = new Color(0.75f, 0.8f, 0.9f);

    [Tooltip("Qué tan marcadas son las sombras con lluvia fuerte (cielo cubierto: casi no hay).")]
    [SerializeField, Range(0f, 1f)] private float shadowStrength = 0.25f;

    [Tooltip("Luz ambiente con lluvia fuerte (fracción de la normal).")]
    [SerializeField, Range(0f, 1f)] private float ambientIntensity = 0.6f;

    [Tooltip("Brillo del cielo con lluvia fuerte (fracción del normal).")]
    [SerializeField, Range(0f, 1f)] private float skyExposure = 0.3f;

    [SerializeField] private Color skyTint = new Color(0.36f, 0.38f, 0.41f);

    [Header("Bruma")]
    [Tooltip("Densidad de la niebla con lluvia fuerte (0 = sin bruma). Con 0,006 a 200 m se ve menos de la mitad.")]
    [SerializeField, Min(0f)] private float fogDensity = 0.006f;

    [SerializeField] private Color fogColor = new Color(0.52f, 0.55f, 0.6f);

    [Header("Transición")]
    [Tooltip("Suaviza lo que llega por red (s).")]
    [SerializeField, Min(0.01f)] private float smoothing = 0.5f;

    private ParticleSystem drops;
    private ParticleSystem.EmissionModule emission;

    private Light sun;
    private float baseSunIntensity;
    private Color baseSunColor;
    private float baseShadowStrength;

    private float baseAmbientIntensity;
    private float baseReflectionIntensity;

    private Material skybox;
    private Material originalSkybox;
    private float baseSkyExposure = 1.3f;
    private Color baseSkyTint = new Color(0.5f, 0.5f, 0.5f);

    private bool baseFog;
    private FogMode baseFogMode;
    private float baseFogDensity;
    private Color baseFogColor;

    private float rain;
    private float applied = -1f;

    private void Awake()
    {
        CreateDrops();
        SaveScene();
    }

    private void OnDestroy()
    {
        RestoreScene();

        if (skybox != null)
            Destroy(skybox);
    }

    private void LateUpdate()
    {
        float target = TrackConditions.Rain;
        rain = Mathf.MoveTowards(rain, target, Time.deltaTime / smoothing);

        FollowCamera();

        // La cantidad de gotas sigue la lluvia al instante; el cielo y la luz, solo si cambió.
        emission.rateOverTime = rain * maxDropsPerSecond;

        if (rain > 0f && !drops.isPlaying)
            drops.Play();

        float overcast = Mathf.Clamp01(rain / fullOvercastAt);

        if (Mathf.Abs(overcast - applied) > 0.002f || (overcast == 0f && applied != 0f))
            ApplyLook(overcast);
    }

    private void CreateDrops()
    {
        GameObject holder = new GameObject("Gotas");
        holder.transform.SetParent(transform, false);

        drops = holder.AddComponent<ParticleSystem>();
        drops.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = drops.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = (areaOffset.y + 4f) / dropSpeed;
        main.startSpeed = 0f;
        main.startSize = dropWidth;
        main.startColor = dropColor;
        main.maxParticles = Mathf.CeilToInt(maxDropsPerSecond * ((areaOffset.y + 4f) / dropSpeed)) + 200;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Shape;

        emission = drops.emission;
        emission.rateOverTime = 0f;

        ParticleSystem.ShapeModule shape = drops.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = areaSize;
        shape.position = areaOffset;

        ParticleSystem.VelocityOverLifetimeModule velocity = drops.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
        velocity.y = new ParticleSystem.MinMaxCurve(-dropSpeed * 1.1f, -dropSpeed * 0.9f);
        velocity.z = new ParticleSystem.MinMaxCurve(-1.5f, -0.5f);

        ParticleSystemRenderer renderer = holder.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = streakLength;
        renderer.lengthScale = 1f;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        if (dropMaterial != null)
            renderer.sharedMaterial = dropMaterial;
        else
            Debug.LogWarning("RainEffect: falta el material de las gotas.", this);
    }

    private void FollowCamera()
    {
        Camera camera = Camera.main;

        if (camera == null)
            return;

        // Va con la cámara y gira solo para los costados (las gotas siempre caen derecho).
        Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);

        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        drops.transform.SetPositionAndRotation(
            camera.transform.position,
            Quaternion.LookRotation(forward.normalized, Vector3.up)
        );
    }

    private void SaveScene()
    {
        sun = RenderSettings.sun;

        if (sun == null)
        {
            foreach (Light light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional)
                {
                    sun = light;
                    break;
                }
            }
        }

        if (sun != null)
        {
            baseSunIntensity = sun.intensity;
            baseSunColor = sun.color;
            baseShadowStrength = sun.shadowStrength;
        }

        baseAmbientIntensity = RenderSettings.ambientIntensity;
        baseReflectionIntensity = RenderSettings.reflectionIntensity;

        // El cielo es un material compartido (asset): se cambia una copia para no tocar el original.
        originalSkybox = RenderSettings.skybox;

        if (originalSkybox != null)
        {
            skybox = new Material(originalSkybox);
            RenderSettings.skybox = skybox;

            if (skybox.HasProperty("_Exposure"))
                baseSkyExposure = skybox.GetFloat("_Exposure");

            if (skybox.HasProperty("_SkyTint"))
                baseSkyTint = skybox.GetColor("_SkyTint");
        }

        baseFog = RenderSettings.fog;
        baseFogMode = RenderSettings.fogMode;
        baseFogDensity = RenderSettings.fogDensity;
        baseFogColor = RenderSettings.fogColor;
    }

    private void ApplyLook(float amount)
    {
        applied = amount;

        if (sun != null)
        {
            sun.intensity = baseSunIntensity * Mathf.Lerp(1f, sunIntensity, amount);
            sun.color = Color.Lerp(baseSunColor, baseSunColor * sunTint, amount);
            sun.shadowStrength = Mathf.Lerp(baseShadowStrength, baseShadowStrength * shadowStrength, amount);
        }

        RenderSettings.ambientIntensity = baseAmbientIntensity * Mathf.Lerp(1f, ambientIntensity, amount);
        RenderSettings.reflectionIntensity = baseReflectionIntensity * Mathf.Lerp(1f, ambientIntensity, amount);

        if (skybox != null)
        {
            if (skybox.HasProperty("_Exposure"))
                skybox.SetFloat("_Exposure", baseSkyExposure * Mathf.Lerp(1f, skyExposure, amount));

            if (skybox.HasProperty("_SkyTint"))
                skybox.SetColor("_SkyTint", Color.Lerp(baseSkyTint, skyTint, amount));
        }

        if (fogDensity > 0f && amount > 0f)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = Mathf.Lerp(baseFog ? baseFogDensity : 0f, fogDensity, amount);
            RenderSettings.fogColor = Color.Lerp(baseFogColor, fogColor, baseFog ? amount : 1f);
        }
        else
        {
            RenderSettings.fog = baseFog;
            RenderSettings.fogMode = baseFogMode;
            RenderSettings.fogDensity = baseFogDensity;
            RenderSettings.fogColor = baseFogColor;
        }
    }

    private void RestoreScene()
    {
        if (applied < 0f)
            return;

        ApplyLook(0f);

        if (originalSkybox != null)
            RenderSettings.skybox = originalSkybox;
    }
}
