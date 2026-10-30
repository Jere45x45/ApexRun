using UnityEngine;

/// <summary>
/// Lo que se ve del clima en esta computadora, según TrackConditions (lo
/// decide el servidor, ver RaceWeather):
/// - Hora del día: posición y color del sol, brillo y color del cielo.
///   Mediodía es como está armada la escena. De noche el "sol" es la luna:
///   luz azulada y suave, cielo oscuro.
/// - Nubes: nublado o lloviendo, baja el sol, las sombras se suavizan y el
///   cielo se pone gris.
/// - Niebla: leve o densa (la elige el anfitrión); con lluvia, además, bruma.
/// - Lluvia: gotas en un sistema de partículas que acompaña a la cámara.
/// - Tormenta: además de lluvia fuerte, relámpagos (destellos de luz) cada
///   tanto. Cada computadora los dibuja a su ritmo: no cambian el juego.
/// Todo cambia de a poco. Al salir de la carrera la escena vuelve a como estaba.
/// No cambia nada del manejo: eso lo hace la humedad de la pista en KartWheel.
/// </summary>
public class WeatherVisuals : MonoBehaviour
{
    [System.Serializable]
    public class TimeOfDayLook
    {
        [Tooltip("Altura del sol sobre el horizonte (°).")]
        public float sunElevation = 50f;

        [Tooltip("Giro del sol respecto de como está en la escena (°): de mañana sale de un lado, al atardecer se pone del otro.")]
        public float sunYawOffset;

        public Color sunColor = new Color(1f, 0.957f, 0.839f);

        public float sunIntensity = 1f;

        [Tooltip("Brillo del cielo (exposición del skybox).")]
        public float skyExposure = 1.3f;

        public Color skyTint = new Color(0.5f, 0.5f, 0.5f);

        [Tooltip("Multiplica la luz ambiente que da el cielo (de noche hace falta subirla para que se vea algo).")]
        public float ambient = 1f;

        public Color fogColor = new Color(0.62f, 0.66f, 0.72f);

        [Tooltip("Qué tan iluminadas se ven las gotas (de noche, apenas).")]
        public float dropBrightness = 1f;

        public TimeOfDayLook() { }

        public TimeOfDayLook(float elevation, float yaw, Color sun, float intensity, float exposure, Color tint, float ambient, Color fog, float drops)
        {
            sunElevation = elevation;
            sunYawOffset = yaw;
            sunColor = sun;
            sunIntensity = intensity;
            skyExposure = exposure;
            skyTint = tint;
            this.ambient = ambient;
            fogColor = fog;
            dropBrightness = drops;
        }
    }

    [Header("Hora del día")]
    [SerializeField] private TimeOfDayLook morning = new TimeOfDayLook(
        22f, -70f, new Color(1f, 0.9f, 0.78f), 0.95f, 1.15f, new Color(0.5f, 0.52f, 0.56f), 1f, new Color(0.75f, 0.76f, 0.78f), 1f);

    [SerializeField] private TimeOfDayLook noon = new TimeOfDayLook(
        50f, 0f, new Color(1f, 0.957f, 0.839f), 1f, 1.3f, new Color(0.5f, 0.5f, 0.5f), 1f, new Color(0.62f, 0.66f, 0.72f), 1f);

    [SerializeField] private TimeOfDayLook sunset = new TimeOfDayLook(
        7f, 70f, new Color(1f, 0.55f, 0.3f), 0.85f, 1.05f, new Color(0.55f, 0.45f, 0.4f), 1f, new Color(0.62f, 0.5f, 0.45f), 0.8f);

    [SerializeField] private TimeOfDayLook night = new TimeOfDayLook(
        40f, 150f, new Color(0.55f, 0.65f, 0.95f), 0.28f, 0.07f, new Color(0.2f, 0.25f, 0.4f), 3f, new Color(0.08f, 0.1f, 0.15f), 0.35f);

    [Header("Nubes (cielo cubierto del todo)")]
    [Tooltip("Desde esta fuerza de lluvia el cielo ya está cubierto del todo.")]
    [SerializeField, Range(0.05f, 1f)] private float fullOvercastAt = 0.5f;

    [Tooltip("Cuánto cubren las nubes cuando está nublado sin llover (0 a 1).")]
    [SerializeField, Range(0f, 1f)] private float cloudyCover = 0.8f;

    [Tooltip("Nubes que quedan en los climas con lluvia mientras no llueve (antes de largarse o después de parar).")]
    [SerializeField, Range(0f, 1f)] private float rainyDayCover = 0.35f;

    [Tooltip("Cielo gris que pone la niebla leve y la densa (para que no quede un cielo azul sobre la niebla).")]
    [SerializeField] private Vector2 fogCover = new Vector2(0.3f, 0.7f);

    [Tooltip("Luz del sol con el cielo cubierto (fracción).")]
    [SerializeField, Range(0f, 1f)] private float overcastSun = 0.35f;

    [Tooltip("Sombras con el cielo cubierto (fracción).")]
    [SerializeField, Range(0f, 1f)] private float overcastShadows = 0.25f;

    [Tooltip("Luz ambiente con el cielo cubierto (fracción).")]
    [SerializeField, Range(0f, 1f)] private float overcastAmbient = 0.65f;

    [Tooltip("Brillo del cielo con el cielo cubierto (fracción).")]
    [SerializeField, Range(0f, 1f)] private float overcastSky = 0.35f;

    [SerializeField] private Color overcastSkyTint = new Color(0.36f, 0.38f, 0.41f);

    [Header("Niebla")]
    [Tooltip("Densidad con niebla leve (exp²). Con 0,008 a unos 125 m se ve poco más de un tercio.")]
    [SerializeField, Min(0f)] private float lightFogDensity = 0.008f;

    [Tooltip("Densidad con niebla densa (exp²). Con 0,02 a unos 50 m se ve poco más de un tercio.")]
    [SerializeField, Min(0f)] private float denseFogDensity = 0.02f;

    [Tooltip("Bruma con lluvia fuerte (exp²), aunque no haya niebla.")]
    [SerializeField, Min(0f)] private float rainHaze = 0.006f;

    [Header("Tormenta")]
    [Tooltip("Cuánto más oscuro está con tormenta (fracción de la luz con lluvia).")]
    [SerializeField, Range(0f, 1f)] private float stormDarkness = 0.6f;

    [Tooltip("Segundos entre relámpagos (al azar entre estos dos).")]
    [SerializeField] private Vector2 lightningInterval = new Vector2(5f, 14f);

    [Tooltip("Cuánta luz agrega un relámpago (al sol).")]
    [SerializeField] private float lightningSun = 2.5f;

    [Tooltip("Cuánta luz agrega un relámpago (al ambiente y al cielo).")]
    [SerializeField] private float lightningAmbient = 1.2f;

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

    [Header("Transición")]
    [Tooltip("Suaviza lo que llega por red y los cambios de nubes (s).")]
    [SerializeField, Min(0.01f)] private float smoothing = 0.5f;

    // Cada cuánto se recalcula la luz ambiente del cielo (es lo más caro).
    private const float EnvironmentInterval = 0.5f;

    private ParticleSystem drops;
    private ParticleSystem.EmissionModule emission;

    // La escena como estaba.
    private Light sun;
    private Quaternion baseSunRotation;
    private float baseSunYaw;
    private float baseSunIntensity;
    private Color baseSunColor;
    private float baseShadowStrength;
    private float baseAmbientIntensity;
    private float baseReflectionIntensity;
    private Material originalSkybox;
    private bool baseFog;
    private FogMode baseFogMode;
    private float baseFogDensity;
    private Color baseFogColor;

    private Material skybox;

    private float rain;
    private float overcast;
    private bool applied;
    private float nextEnvironmentUpdate;
    private float lastEnvironmentSky = -1f;

    // Relámpagos.
    private float nextLightning;
    private float lightningTime = -1f;
    private float flash;

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
        if (!TrackConditions.Active)
            return;

        float step = Time.deltaTime / smoothing;

        rain = Mathf.MoveTowards(rain, TrackConditions.Rain, step);
        overcast = Mathf.MoveTowards(overcast, TargetOvercast(), step);

        UpdateLightning();
        FollowCamera();

        TimeOfDayLook look = CurrentLook();

        emission.rateOverTime = rain * maxDropsPerSecond;

        ParticleSystem.MainModule main = drops.main;
        main.startColor = dropColor * new Color(1f, 1f, 1f, 1f) * Mathf.Lerp(look.dropBrightness, look.dropBrightness * 0.8f, overcast);

        if (rain > 0f && !drops.isPlaying)
            drops.Play();

        ApplyLook(look);
        applied = true;
    }

    // ---------- Cálculos ----------

    private float TargetOvercast()
    {
        float cover;

        switch (TrackConditions.Scenario)
        {
            case WeatherScenario.Clear: cover = 0f; break;
            case WeatherScenario.Cloudy: cover = cloudyCover; break;
            default: cover = rainyDayCover; break;
        }

        if (TrackConditions.Fog == FogLevel.Light)
            cover = Mathf.Max(cover, fogCover.x);
        else if (TrackConditions.Fog == FogLevel.Dense)
            cover = Mathf.Max(cover, fogCover.y);

        return Mathf.Max(cover, Mathf.Clamp01(TrackConditions.Rain / fullOvercastAt));
    }

    private TimeOfDayLook CurrentLook()
    {
        switch (TrackConditions.TimeOfDay)
        {
            case TimeOfDay.Morning: return morning;
            case TimeOfDay.Sunset: return sunset;
            case TimeOfDay.Night: return night;
            default: return noon;
        }
    }

    private void UpdateLightning()
    {
        bool storm = TrackConditions.Scenario == WeatherScenario.Storm && rain > 0.5f;

        if (!storm)
        {
            flash = 0f;
            lightningTime = -1f;
            return;
        }

        if (nextLightning <= 0f)
            nextLightning = Time.time + Random.Range(lightningInterval.x, lightningInterval.y);

        if (lightningTime < 0f && Time.time >= nextLightning)
        {
            lightningTime = 0f;
            nextLightning = Time.time + Random.Range(lightningInterval.x, lightningInterval.y);
        }

        if (lightningTime < 0f)
        {
            flash = 0f;
            return;
        }

        // Dos destellos seguidos, como un relámpago de verdad.
        lightningTime += Time.deltaTime;

        if (lightningTime < 0.07f) flash = 1f;
        else if (lightningTime < 0.15f) flash = 0.1f;
        else if (lightningTime < 0.25f) flash = 0.7f;
        else if (lightningTime < 0.45f) flash = Mathf.Lerp(0.7f, 0f, (lightningTime - 0.25f) / 0.2f);
        else
        {
            flash = 0f;
            lightningTime = -1f;
        }
    }

    // ---------- Aplicar ----------

    private void ApplyLook(TimeOfDayLook look)
    {
        bool storm = TrackConditions.Scenario == WeatherScenario.Storm;
        float darkness = storm ? Mathf.Lerp(1f, stormDarkness, Mathf.Clamp01(rain)) : 1f;

        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(look.sunElevation, baseSunYaw + look.sunYawOffset, 0f);
            sun.color = Color.Lerp(look.sunColor, Grey(look.sunColor), overcast * 0.6f);
            sun.intensity = look.sunIntensity * Mathf.Lerp(1f, overcastSun, overcast) * darkness + flash * lightningSun;
            sun.shadowStrength = baseShadowStrength * Mathf.Lerp(1f, overcastShadows, overcast);
        }

        float skyExposure = look.skyExposure * Mathf.Lerp(1f, overcastSky, overcast) * darkness;

        if (skybox != null)
        {
            if (skybox.HasProperty("_Exposure"))
                skybox.SetFloat("_Exposure", skyExposure + flash * lightningAmbient * 0.5f);

            if (skybox.HasProperty("_SkyTint"))
                skybox.SetColor("_SkyTint", Color.Lerp(look.skyTint, overcastSkyTint * Brightness(look.skyTint) * 2f, overcast));
        }

        float ambient = baseAmbientIntensity * look.ambient * Mathf.Lerp(1f, overcastAmbient, overcast) * darkness;
        RenderSettings.ambientIntensity = ambient + flash * lightningAmbient;
        RenderSettings.reflectionIntensity = baseReflectionIntensity * Mathf.Clamp01(ambient / Mathf.Max(0.01f, baseAmbientIntensity));

        float fogDensity = TrackConditions.Fog == FogLevel.Dense ? denseFogDensity
                         : TrackConditions.Fog == FogLevel.Light ? lightFogDensity
                         : 0f;

        fogDensity = Mathf.Max(fogDensity, rainHaze * Mathf.Clamp01(rain / fullOvercastAt));

        if (fogDensity > 0f)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.fogColor = Color.Lerp(look.fogColor, Grey(look.fogColor) * 0.85f, overcast) * darkness;
        }
        else
        {
            RenderSettings.fog = false;
        }

        // La luz ambiente sale del cielo: hay que recalcularla cuando cambia (sin los relámpagos, que son instantáneos).
        if (Time.unscaledTime >= nextEnvironmentUpdate && Mathf.Abs(skyExposure - lastEnvironmentSky) > 0.01f)
        {
            nextEnvironmentUpdate = Time.unscaledTime + EnvironmentInterval;
            lastEnvironmentSky = skyExposure;

            if (skybox != null && skybox.HasProperty("_Exposure"))
                skybox.SetFloat("_Exposure", skyExposure);

            DynamicGI.UpdateEnvironment();
        }
    }

    private static Color Grey(Color color)
    {
        float value = Brightness(color);
        return new Color(value, value, value, color.a);
    }

    private static float Brightness(Color color) => (color.r + color.g + color.b) / 3f;

    // ---------- Gotas ----------

    private void CreateDrops()
    {
        GameObject holder = new GameObject("Gotas");
        holder.transform.SetParent(transform, false);

        drops = holder.AddComponent<ParticleSystem>();
        drops.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        float lifetime = (areaOffset.y + 4f) / dropSpeed;

        ParticleSystem.MainModule main = drops.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = lifetime;
        main.startSpeed = 0f;
        main.startSize = dropWidth;
        main.startColor = dropColor;
        main.maxParticles = Mathf.CeilToInt(maxDropsPerSecond * lifetime) + 200;
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
            Debug.LogWarning("WeatherVisuals: falta el material de las gotas.", this);
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

    // ---------- Escena ----------

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
            baseSunRotation = sun.transform.rotation;
            baseSunYaw = sun.transform.eulerAngles.y;
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
        }

        baseFog = RenderSettings.fog;
        baseFogMode = RenderSettings.fogMode;
        baseFogDensity = RenderSettings.fogDensity;
        baseFogColor = RenderSettings.fogColor;
    }

    private void RestoreScene()
    {
        if (sun != null)
        {
            sun.transform.rotation = baseSunRotation;
            sun.intensity = baseSunIntensity;
            sun.color = baseSunColor;
            sun.shadowStrength = baseShadowStrength;
        }

        RenderSettings.ambientIntensity = baseAmbientIntensity;
        RenderSettings.reflectionIntensity = baseReflectionIntensity;

        RenderSettings.fog = baseFog;
        RenderSettings.fogMode = baseFogMode;
        RenderSettings.fogDensity = baseFogDensity;
        RenderSettings.fogColor = baseFogColor;

        if (originalSkybox != null)
            RenderSettings.skybox = originalSkybox;

        if (applied)
            DynamicGI.UpdateEnvironment();
    }
}
