/// <summary>
/// Cómo está la carrera en esta computadora: el clima, la hora y la pista.
/// Lo actualiza RaceWeather con lo que decide el servidor, así todos ven lo
/// mismo y manejan con la misma pista:
/// - La física (KartWheel) usa Wetness.
/// - Lo visual (WeatherVisuals) usa todo lo demás.
/// Fuera de una carrera queda seco, despejado, al mediodía y sin niebla.
/// </summary>
public static class TrackConditions
{
    /// <summary>Qué tan mojada está la pista: 0 = seca, 1 = empapada.</summary>
    public static float Wetness { get; set; }

    /// <summary>Cuánto llueve: 0 = nada, 1 = lluvia fuerte.</summary>
    public static float Rain { get; set; }

    /// <summary>Cómo está el cielo (para las nubes y los relámpagos).</summary>
    public static WeatherScenario Scenario { get; set; } = WeatherScenario.Clear;

    public static TimeOfDay TimeOfDay { get; set; } = TimeOfDay.Noon;

    public static FogLevel Fog { get; set; } = FogLevel.None;

    /// <summary>True si hay un clima de carrera andando (si no, lo visual deja la escena como está).</summary>
    public static bool Active { get; set; }

    public static void Reset()
    {
        Wetness = 0f;
        Rain = 0f;
        Scenario = WeatherScenario.Clear;
        TimeOfDay = TimeOfDay.Noon;
        Fog = FogLevel.None;
        Active = false;
    }
}
