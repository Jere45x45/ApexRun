using UnityEngine;

/// <summary>Cómo está el cielo durante una carrera.</summary>
public enum WeatherScenario : byte
{
    /// <summary>Despejado toda la carrera.</summary>
    Clear,

    /// <summary>Nublado sin llover: menos sol, sombras suaves.</summary>
    Cloudy,

    /// <summary>Llueve toda la carrera.</summary>
    Rain,

    /// <summary>Lluvia fuerte con relámpagos toda la carrera.</summary>
    Storm,

    /// <summary>Larga lloviendo y en algún momento para (la pista se seca de a poco).</summary>
    RainStops,

    /// <summary>Larga sin lluvia y en algún momento se larga a llover.</summary>
    RainStarts
}

/// <summary>Hora del día de la carrera (fija durante toda la carrera).</summary>
public enum TimeOfDay : byte
{
    Morning,
    Noon,
    Sunset,
    Night
}

/// <summary>Niebla de la carrera.</summary>
public enum FogLevel : byte
{
    None,
    Light,
    Dense
}

/// <summary>Lo que elige el anfitrión para el cielo: uno fijo o al azar.</summary>
public enum WeatherChoice
{
    Random,
    Clear,
    Cloudy,
    Rain,
    Storm,

    /// <summary>
    /// Clima dinámico: el servidor sortea si larga lloviendo y para, o si larga
    /// seco y se larga a llover (RainStops o RainStarts). Cambia una vez en la carrera.
    /// </summary>
    Dynamic
}

/// <summary>Lo que elige el anfitrión para la hora: una fija o al azar.</summary>
public enum TimeOfDayChoice
{
    Random,
    Morning,
    Noon,
    Sunset,
    Night
}

/// <summary>Lo que elige el anfitrión para la niebla: una fija o al azar.</summary>
public enum FogChoice
{
    Random,
    None,
    Light,
    Dense
}

/// <summary>
/// Clima, hora y niebla que eligió el anfitrión en el menú (configuración de
/// la carrera). Pasan a la carrera (los lee RaceWeather en el servidor) y
/// quedan guardados para la próxima vez.
/// </summary>
public static class WeatherChoiceStore
{
    private const string WeatherKey = "ApexRun.Clima";
    private const string TimeKey = "ApexRun.Hora";
    private const string FogKey = "ApexRun.Niebla";

    private static WeatherChoice? weather;
    private static TimeOfDayChoice? time;
    private static FogChoice? fog;

    /// <summary>Cielo elegido (se llama Current por compatibilidad).</summary>
    public static WeatherChoice Current
    {
        get => weather ??= LoadWeather();
        set { weather = value; Save(WeatherKey, (int)value); }
    }

    public static TimeOfDayChoice Time
    {
        get => time ??= (TimeOfDayChoice)Load<TimeOfDayChoice>(TimeKey);
        set { time = value; Save(TimeKey, (int)value); }
    }

    public static FogChoice Fog
    {
        get => fog ??= (FogChoice)Load<FogChoice>(FogKey);
        set { fog = value; Save(FogKey, (int)value); }
    }

    /// <summary>La opción siguiente o la anterior de cualquiera de las listas (para los botones &lt; y &gt;).</summary>
    public static T Step<T>(T value, int direction) where T : System.Enum
    {
        int count = System.Enum.GetValues(typeof(T)).Length;
        int index = (System.Convert.ToInt32(value) + direction % count + count) % count;
        return (T)System.Enum.ToObject(typeof(T), index);
    }

    public static WeatherChoice Next(WeatherChoice choice) => Step(choice, +1);

    public static WeatherChoice Previous(WeatherChoice choice) => Step(choice, -1);

    // ---------- Textos ----------

    public static string Label(WeatherChoice choice)
    {
        switch (choice)
        {
            case WeatherChoice.Random: return GameLanguage.Text("AL AZAR", "RANDOM");
            case WeatherChoice.Dynamic: return GameLanguage.Text("DINÁMICO", "DYNAMIC");
            default: return Label((WeatherScenario)((int)choice - 1)).ToUpperInvariant();
        }
    }

    public static string Label(TimeOfDayChoice choice)
    {
        return choice == TimeOfDayChoice.Random ? GameLanguage.Text("AL AZAR", "RANDOM") : Label((TimeOfDay)((int)choice - 1)).ToUpperInvariant();
    }

    public static string Label(FogChoice choice)
    {
        return choice == FogChoice.Random ? GameLanguage.Text("AL AZAR", "RANDOM") : Label((FogLevel)((int)choice - 1)).ToUpperInvariant();
    }

    public static string Label(WeatherScenario scenario)
    {
        switch (scenario)
        {
            case WeatherScenario.Cloudy: return GameLanguage.Text("nublado", "cloudy");
            case WeatherScenario.Rain: return GameLanguage.Text("lluvia", "rain");
            case WeatherScenario.Storm: return GameLanguage.Text("tormenta", "storm");
            case WeatherScenario.RainStops: return GameLanguage.Text("lluvia que para", "rain stops");
            case WeatherScenario.RainStarts: return GameLanguage.Text("se larga a llover", "rain starts");
            default: return GameLanguage.Text("despejado", "clear");
        }
    }

    public static string Label(TimeOfDay timeOfDay)
    {
        switch (timeOfDay)
        {
            case TimeOfDay.Morning: return GameLanguage.Text("mañana", "morning");
            case TimeOfDay.Sunset: return GameLanguage.Text("atardecer", "sunset");
            case TimeOfDay.Night: return GameLanguage.Text("noche", "night");
            default: return GameLanguage.Text("mediodía", "noon");
        }
    }

    public static string Label(FogLevel level)
    {
        switch (level)
        {
            case FogLevel.Light: return GameLanguage.Text("niebla leve", "light fog");
            case FogLevel.Dense: return GameLanguage.Text("niebla densa", "dense fog");
            default: return GameLanguage.Text("sin niebla", "no fog");
        }
    }

    /// <summary>Resumen para la lista de servidores, por ejemplo "LLUVIA · NOCHE · NIEBLA LEVE".</summary>
    public static string Summary()
    {
        string text = (Current == WeatherChoice.Dynamic ? GameLanguage.Text("CLIMA DINÁMICO", "DYNAMIC WEATHER") : Label(Current)) + "  ·  " + Label(Time);

        if (Fog != FogChoice.None)
            text += "  ·  " + (Fog == FogChoice.Random ? GameLanguage.Text("NIEBLA AL AZAR", "RANDOM FOG") : Label(Fog));

        return text;
    }

    // ---------- PlayerPrefs ----------

    /// <summary>
    /// Cielo guardado. Antes había "lluvia que para" (5) y "se larga a llover" (6):
    /// los dos pasan a ser el clima dinámico.
    /// </summary>
    private static WeatherChoice LoadWeather()
    {
        int saved = PlayerPrefs.GetInt(WeatherKey, 0);

        if (saved == 6)
            return WeatherChoice.Dynamic;

        return System.Enum.IsDefined(typeof(WeatherChoice), saved) ? (WeatherChoice)saved : WeatherChoice.Random;
    }

    private static int Load<T>(string key) where T : System.Enum
    {
        int saved = PlayerPrefs.GetInt(key, 0);
        return System.Enum.IsDefined(typeof(T), saved) ? saved : 0;
    }

    private static void Save(string key, int value)
    {
        PlayerPrefs.SetInt(key, value);
        PlayerPrefs.Save();
    }
}
