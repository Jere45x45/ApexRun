using UnityEngine;

public class WeatherProfile : MonoBehaviour
{
    // Singleton simple para acceso global al entorno (aceptable para sistemas ambientales únicos)
    public static WeatherProfile Instance { get; private set; }

    [Header("Perfiles de Clima")]
    [SerializeField] private WeatherProfile normalWeather;
    [SerializeField] private WeatherProfile rainWeather;
    [SerializeField] private WeatherProfile snowWeather;

    [Header("Sistemas de Partículas")]
    [SerializeField] private ParticleSystem rainParticles;
    [SerializeField] private ParticleSystem snowParticles;

    [Header("Configuración de Transición")]
    [SerializeField] private float transitionSpeed = 0.5f;

    private WeatherProfile targetProfile;

    // El Kart leerá este valor
    public float CurrentGripMultiplier { get; private set; } = 1f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        targetProfile = normalWeather;
        RenderSettings.fog = true; // Aseguramos que la niebla esté activa
    }

    private void Update()
    {
        if (targetProfile == null) return;

        // 1. Transición suave de la Física
        CurrentGripMultiplier = Mathf.Lerp(
            CurrentGripMultiplier, 
            targetProfile.gripMultiplier, 
            Time.deltaTime * transitionSpeed
        );

        // 2. Transición suave de la Visibilidad
        RenderSettings.fogDensity = Mathf.Lerp(
            RenderSettings.fogDensity, 
            targetProfile.fogDensity, 
            Time.deltaTime * transitionSpeed
        );
        RenderSettings.fogColor = Color.Lerp(
            RenderSettings.fogColor, 
            targetProfile.fogColor, 
            Time.deltaTime * transitionSpeed
        );

        // 3. Transición suave de las Partículas
        UpdateParticleEmission(rainParticles, targetProfile.rainEmissionRate);
        UpdateParticleEmission(snowParticles, targetProfile.snowEmissionRate);
    }

    private void UpdateParticleEmission(ParticleSystem ps, float targetRate)
    {
        if (ps == null) return;
        
        var emission = ps.emission;
        float currentRate = emission.rateOverTime.constant;
        float newRate = Mathf.Lerp(currentRate, targetRate, Time.deltaTime * transitionSpeed);
        
        emission.rateOverTime = newRate;
    }

    // Métodos para cambiar el clima desde UI, triggers u otros scripts
    public void SetNormal() => targetProfile = normalWeather;
    public void SetRain() => targetProfile = rainWeather;
    public void SetSnow() => targetProfile = snowWeather;
}
