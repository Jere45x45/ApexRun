using UnityEngine;

public class WeatherManager : MonoBehaviour
{
    public static WeatherManager Instance { get; private set; }

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
        RenderSettings.fog = true;
    }

    private void Update()
    {
        if (targetProfile == null) return;

        CurrentGripMultiplier = Mathf.Lerp(
            CurrentGripMultiplier, 
            targetProfile.gripMultiplier, 
            Time.deltaTime * transitionSpeed
        );

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

    public void SetNormal() => targetProfile = normalWeather;
    public void SetRain() => targetProfile = rainWeather;
    public void SetSnow() => targetProfile = snowWeather;
}