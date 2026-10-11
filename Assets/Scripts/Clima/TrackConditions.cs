/// <summary>
/// Estado de la pista que usa la física (KartWheel) en esta computadora.
/// Lo actualiza RaceWeather con lo que decide el servidor, así todos los
/// karts manejan con la misma pista. Fuera de una carrera queda seca.
/// </summary>
public static class TrackConditions
{
    /// <summary>Qué tan mojada está la pista: 0 = seca, 1 = empapada.</summary>
    public static float Wetness { get; set; }

    /// <summary>Cuánto llueve: 0 = nada, 1 = lluvia fuerte.</summary>
    public static float Rain { get; set; }

    public static void Reset()
    {
        Wetness = 0f;
        Rain = 0f;
    }
}
