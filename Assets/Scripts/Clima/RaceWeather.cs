using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Clima de la carrera. Lo decide el servidor y lo reciben todos, así cada
/// computadora ve el mismo cielo y maneja con la misma pista mojada.
///
/// - Al aparecer, el servidor arma el guion con lo que eligió el anfitrión en
///   el menú (WeatherChoiceStore), sorteando lo que esté "al azar" según los
///   pesos: el cielo (despejado, nublado, lluvia, tormenta, lluvia que para,
///   se larga a llover), la hora (mañana, mediodía, atardecer, noche) y la
///   niebla (sin, leve, densa). La hora y la niebla no cambian en la carrera.
/// - "Lluvia que para" y "Se larga a llover" cambian una sola vez: después de
///   que el primero completa una vuelta sorteada (entre la 1 y la anteúltima)
///   y unos segundos más. En una carrera de una vuelta, a los segundos de largar.
/// - La lluvia sube y baja de a poco (rainRampTime). La pista se moja con la
///   lluvia (wettingTime) y se seca más despacio cuando para (dryingTime).
/// - La tormenta es lluvia fuerte (1) toda la carrera; los relámpagos los
///   dibuja cada computadora (WeatherVisuals).
/// - Publica el cielo, la hora, la niebla, la lluvia y la humedad de la pista.
///   En cada computadora los pasa a TrackConditions: KartWheel saca el agarre
///   de ahí y WeatherVisuals dibuja el cielo, la luz, la niebla y la lluvia.
/// </summary>
public class RaceWeather : NetworkBehaviour
{
    public static RaceWeather Instance { get; private set; }

    [Header("Referencias")]
    [SerializeField] private RaceManager raceManager;
    [SerializeField] private RaceLapManager lapManager;

    [Header("Al azar: probabilidad de cada cielo (pesos)")]
    [SerializeField, Min(0f)] private float clearWeight = 30f;
    [SerializeField, Min(0f)] private float cloudyWeight = 20f;
    [SerializeField, Min(0f)] private float rainWeight = 12f;
    [SerializeField, Min(0f)] private float stormWeight = 6f;
    [SerializeField, Min(0f)] private float rainStopsWeight = 16f;
    [SerializeField, Min(0f)] private float rainStartsWeight = 16f;

    [Header("Al azar: probabilidad de cada hora (pesos)")]
    [SerializeField, Min(0f)] private float morningWeight = 25f;
    [SerializeField, Min(0f)] private float noonWeight = 35f;
    [SerializeField, Min(0f)] private float sunsetWeight = 25f;
    [SerializeField, Min(0f)] private float nightWeight = 15f;

    [Header("Al azar: probabilidad de niebla (pesos)")]
    [SerializeField, Min(0f)] private float noFogWeight = 70f;
    [SerializeField, Min(0f)] private float lightFogWeight = 20f;
    [SerializeField, Min(0f)] private float denseFogWeight = 10f;

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
        new NetworkVariable<WeatherScenario>(WeatherScenario.Clear);

    private readonly NetworkVariable<TimeOfDay> timeOfDay = new NetworkVariable<TimeOfDay>(TimeOfDay.Noon);

    private readonly NetworkVariable<FogLevel> fog = new NetworkVariable<FogLevel>(FogLevel.None);

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

    public TimeOfDay TimeOfDay => timeOfDay.Value;

    public FogLevel Fog => fog.Value;

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
            ? (WeatherScenario)Pick(clearWeight, cloudyWeight, rainWeight, stormWeight, rainStopsWeight, rainStartsWeight)
            : (WeatherScenario)((int)choice - 1);

        TimeOfDayChoice timeChoice = WeatherChoiceStore.Time;

        TimeOfDay chosenTime = timeChoice == TimeOfDayChoice.Random
            ? (TimeOfDay)Pick(morningWeight, noonWeight, sunsetWeight, nightWeight)
            : (TimeOfDay)((int)timeChoice - 1);

        FogChoice fogChoice = WeatherChoiceStore.Fog;

        FogLevel chosenFog = fogChoice == FogChoice.Random
            ? (FogLevel)Pick(noFogWeight, lightFogWeight, denseFogWeight)
            : (FogLevel)((int)fogChoice - 1);

        // La tormenta siempre llueve fuerte.
        intensity = chosen == WeatherScenario.Storm ? 1f : Random.Range(rainIntensityRange.x, rainIntensityRange.y);

        bool startsRaining = chosen == WeatherScenario.Rain || chosen == WeatherScenario.Storm ||
                             chosen == WeatherScenario.RainStops;

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
        timeOfDay.Value = chosenTime;
        fog.Value = chosenFog;
        Publish();

        string when = chosen == WeatherScenario.RainStops || chosen == WeatherScenario.RainStarts
            ? changeAfterLaps > 0
                ? $" (cambia {changeDelay:0} s después de que el primero complete la vuelta {changeAfterLaps})"
                : $" (cambia a los {changeDelay:0} s de la largada)"
            : "";

        Debug.Log($"CLIMA: {WeatherChoiceStore.Label(chosen)}, {WeatherChoiceStore.Label(chosenTime)}, " +
                  $"{WeatherChoiceStore.Label(chosenFog)} (elegido: {WeatherChoiceStore.Label(choice)} / " +
                  $"{WeatherChoiceStore.Label(timeChoice)} / {WeatherChoiceStore.Label(fogChoice)}), " +
                  $"fuerza de la lluvia {intensity:0.00}{when}.", this);
    }

    /// <summary>Sortea una opción según sus pesos (devuelve el índice).</summary>
    private static int Pick(params float[] weights)
    {
        float total = 0f;

        foreach (float weight in weights)
            total += weight;

        if (total <= 0f)
            return 0;

        float pick = Random.value * total;

        for (int i = 0; i < weights.Length; i++)
        {
            pick -= weights[i];

            if (pick < 0f)
                return i;
        }

        return weights.Length - 1;
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
        TrackConditions.Scenario = scenario.Value;
        TrackConditions.TimeOfDay = timeOfDay.Value;
        TrackConditions.Fog = fog.Value;
        TrackConditions.Active = true;
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
