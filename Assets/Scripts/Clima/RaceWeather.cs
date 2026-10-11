using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Clima de la carrera. Lo decide el servidor y lo reciben todos, así cada
/// computadora ve la misma lluvia y maneja con la misma pista mojada.
///
/// - Al aparecer, el servidor arma el guion: el que eligió el anfitrión en el
///   menú (WeatherChoiceStore) o uno al azar según los pesos.
/// - "Lluvia que para" y "Se larga a llover" cambian una sola vez: después de
///   que el primero completa una vuelta sorteada (entre la 1 y la anteúltima)
///   y unos segundos más. En una carrera de una vuelta, a los segundos de largar.
/// - La lluvia sube y baja de a poco (rainRampTime). La pista se moja con la
///   lluvia (wettingTime) y se seca más despacio cuando para (dryingTime).
/// - Publica la lluvia y la humedad de la pista (0 a 1). En cada computadora
///   las pasa a TrackConditions: KartWheel saca el agarre de ahí y
///   RainEffect dibuja la lluvia.
/// </summary>
public class RaceWeather : NetworkBehaviour
{
    public static RaceWeather Instance { get; private set; }

    [Header("Referencias")]
    [SerializeField] private RaceManager raceManager;
    [SerializeField] private RaceLapManager lapManager;

    [Header("Al azar: probabilidad de cada clima (pesos)")]
    [SerializeField, Min(0f)] private float dryWeight = 45f;
    [SerializeField, Min(0f)] private float rainWeight = 15f;
    [SerializeField, Min(0f)] private float rainStopsWeight = 20f;
    [SerializeField, Min(0f)] private float rainStartsWeight = 20f;

    [Header("Lluvia")]
    [Tooltip("Qué tan fuerte llueve: se sortea entre estos dos valores (1 = lluvia fuerte).")]
    [SerializeField] private Vector2 rainIntensityRange = new Vector2(0.6f, 1f);

    [Tooltip("Cuánto tarda la lluvia en llegar a su fuerza, o en parar del todo (s).")]
    [SerializeField, Min(0.1f)] private float rainRampTime = 20f;

    [Tooltip("Cuánto tarda la pista en mojarse del todo con lluvia fuerte (s).")]
    [SerializeField, Min(0.1f)] private float wettingTime = 40f;

    [Tooltip("Cuánto tarda la pista en secarse del todo cuando para (s).")]
    [SerializeField, Min(0.1f)] private float dryingTime = 90f;

    [Header("Cuándo cambia")]
    [Tooltip("Carreras de varias vueltas: segundos que pasan después de que el primero completa la vuelta sorteada (entre estos dos).")]
    [SerializeField] private Vector2 extraDelayRange = new Vector2(0f, 30f);

    [Tooltip("Carreras de una vuelta: segundos desde la largada (entre estos dos).")]
    [SerializeField] private Vector2 singleLapChangeTime = new Vector2(30f, 70f);

    private const float PublishInterval = 0.25f;

    private readonly NetworkVariable<WeatherScenario> scenario =
        new NetworkVariable<WeatherScenario>(WeatherScenario.Dry);

    private readonly NetworkVariable<float> rain = new NetworkVariable<float>(0f);

    private readonly NetworkVariable<float> wetness = new NetworkVariable<float>(0f);

    // ---------- Solo en el servidor ----------
    private float rainNow;
    private float rainTarget;
    private float wetNow;
    private float intensity;

    // Vueltas que tiene que completar el primero antes del cambio (0 = carrera de una vuelta: por tiempo).
    private int changeAfterLaps;
    private float changeDelay;
    private float changeAt = -1f;
    private bool changed;

    private float raceStartTime = -1f;
    private int leaderLaps;
    private float nextPublish;

    public WeatherScenario Scenario => scenario.Value;

    /// <summary>Cuánto llueve ahora (0 a 1).</summary>
    public float Rain => TrackConditions.Rain;

    /// <summary>Qué tan mojada está la pista ahora (0 a 1).</summary>
    public float Wetness => TrackConditions.Wetness;

    private void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            SetupServer();

        ApplyToTrack();
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && lapManager != null)
            lapManager.LapCompleted -= HandleLapCompleted;

        TrackConditions.Reset();
    }

    public override void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        // Otras escenas (Catálogo, pruebas) siempre arrancan con la pista seca.
        TrackConditions.Reset();

        base.OnDestroy();
    }

    private void SetupServer()
    {
        WeatherChoice choice = WeatherChoiceStore.Current;

        WeatherScenario chosen = choice == WeatherChoice.Random
            ? PickRandom()
            : (WeatherScenario)((int)choice - 1);

        intensity = Random.Range(rainIntensityRange.x, rainIntensityRange.y);

        bool startsRaining = chosen == WeatherScenario.Rain || chosen == WeatherScenario.RainStops;

        rainNow = rainTarget = startsRaining ? intensity : 0f;
        wetNow = startsRaining ? 1f : 0f;

        int laps = lapManager != null ? lapManager.TotalLaps : 1;

        if (laps > 1)
        {
            changeAfterLaps = Random.Range(1, laps);
            changeDelay = Random.Range(extraDelayRange.x, extraDelayRange.y);
        }
        else
        {
            changeAfterLaps = 0;
            changeDelay = Random.Range(singleLapChangeTime.x, singleLapChangeTime.y);
        }

        if (lapManager != null)
            lapManager.LapCompleted += HandleLapCompleted;

        scenario.Value = chosen;
        Publish();

        string when = chosen == WeatherScenario.RainStops || chosen == WeatherScenario.RainStarts
            ? changeAfterLaps > 0
                ? $" (cambia {changeDelay:0} s después de que el primero complete la vuelta {changeAfterLaps})"
                : $" (cambia a los {changeDelay:0} s de la largada)"
            : "";

        Debug.Log($"CLIMA: {WeatherChoiceStore.Label(chosen)}, elegido: {WeatherChoiceStore.Label(choice)}, " +
                  $"fuerza de la lluvia {intensity:0.00}{when}.", this);
    }

    private WeatherScenario PickRandom()
    {
        float total = dryWeight + rainWeight + rainStopsWeight + rainStartsWeight;

        if (total <= 0f)
            return WeatherScenario.Dry;

        float pick = Random.value * total;

        if ((pick -= dryWeight) < 0f) return WeatherScenario.Dry;
        if ((pick -= rainWeight) < 0f) return WeatherScenario.Rain;
        if ((pick -= rainStopsWeight) < 0f) return WeatherScenario.RainStops;
        return WeatherScenario.RainStarts;
    }

    private void HandleLapCompleted(Rigidbody kart)
    {
        // Se dispara antes de pasar a la vuelta siguiente: la vuelta actual es la que completó.
        leaderLaps = Mathf.Max(leaderLaps, lapManager.GetCurrentLap(kart));
    }

    private void Update()
    {
        if (IsServer)
            UpdateServer(Time.deltaTime);

        ApplyToTrack();
    }

    private void UpdateServer(float deltaTime)
    {
        if (raceStartTime < 0f && raceManager != null && raceManager.IsRacing())
            raceStartTime = Time.time;

        bool changes = scenario.Value == WeatherScenario.RainStops || scenario.Value == WeatherScenario.RainStarts;

        if (changes && !changed && raceStartTime >= 0f)
        {
            if (changeAt < 0f)
            {
                if (changeAfterLaps == 0)
                    changeAt = raceStartTime + changeDelay;
                else if (leaderLaps >= changeAfterLaps)
                    changeAt = Time.time + changeDelay;
            }

            if (changeAt >= 0f && Time.time >= changeAt)
                ChangeRain(scenario.Value == WeatherScenario.RainStarts);
        }

        rainNow = Mathf.MoveTowards(rainNow, rainTarget, deltaTime / rainRampTime);

        // Con lluvia la pista se moja (más rápido cuanto más llueve); sin lluvia se seca.
        bool raining = rainNow > 0.05f;
        float rate = raining ? rainNow / wettingTime : 1f / dryingTime;
        wetNow = Mathf.MoveTowards(wetNow, raining ? 1f : 0f, rate * deltaTime);

        if (Time.unscaledTime >= nextPublish)
        {
            nextPublish = Time.unscaledTime + PublishInterval;
            Publish();
        }
    }

    private void ChangeRain(bool startRaining)
    {
        changed = true;
        rainTarget = startRaining ? Mathf.Max(intensity, 0.1f) : 0f;

        Debug.Log(startRaining ? "CLIMA: se largó a llover." : "CLIMA: paró de llover, la pista se empieza a secar.", this);
    }

    private void Publish()
    {
        if (!Mathf.Approximately(rain.Value, rainNow))
            rain.Value = rainNow;

        if (!Mathf.Approximately(wetness.Value, wetNow))
            wetness.Value = wetNow;
    }

    private void ApplyToTrack()
    {
        if (!IsSpawned)
            return;

        // El servidor usa sus valores al instante; los demás, los que publica.
        TrackConditions.Rain = IsServer ? rainNow : rain.Value;
        TrackConditions.Wetness = IsServer ? wetNow : wetness.Value;
    }

    // ---------- Para probar (clic derecho en el componente, en Play, en el host) ----------

    [ContextMenu("Clima: largar lluvia ahora")]
    private void ForceRain()
    {
        if (!IsServer)
            return;

        if (intensity <= 0f)
            intensity = 1f;

        ChangeRain(true);
    }

    [ContextMenu("Clima: parar lluvia ahora")]
    private void ForceStop()
    {
        if (IsServer)
            ChangeRain(false);
    }
}
