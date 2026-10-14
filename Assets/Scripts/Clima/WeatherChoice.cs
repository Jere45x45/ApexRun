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
    RainStops,
    RainStarts
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
        get => weather ??= (WeatherChoice)Load<WeatherChoice>(WeatherKey);
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
        return choice == WeatherChoice.Random ? "AL AZAR" : Label((WeatherScenario)((int)choice - 1)).ToUpperInvariant();
    }

    public static string Label(TimeOfDayChoice choice)
    {
        return choice == TimeOfDayChoice.Random ? "AL AZAR" : Label((TimeOfDay)((int)choice - 1)).ToUpperInvariant();
    }

    public static string Label(FogChoice choice)
    {
        return choice == FogChoice.Random ? "AL AZAR" : Label((FogLevel)((int)choice - 1)).ToUpperInvariant();
    }

    public static string Label(WeatherScenario scenario)
    {
        switch (scenario)
        {
            case WeatherScenario.Cloudy: return "nublado";
            case WeatherScenario.Rain: return "lluvia";
            case WeatherScenario.Storm: return "tormenta";
            case WeatherScenario.RainStops: return "lluvia que para";
            case WeatherScenario.RainStarts: return "se larga a llover";
            default: return "despejado";
        }
    }

    public static string Label(TimeOfDay timeOfDay)
    {
        switch (timeOfDay)
        {
            case TimeOfDay.Morning: return "mañana";
            case TimeOfDay.Sunset: return "atardecer";
            case TimeOfDay.Night: return "noche";
            default: return "mediodía";
        }
    }

    public static string Label(FogLevel level)
    {
        switch (level)
        {
            case FogLevel.Light: return "niebla leve";
            case FogLevel.Dense: return "niebla densa";
            default: return "sin niebla";
        }
    }

    /// <summary>Resumen para la lista de servidores, por ejemplo "LLUVIA · NOCHE · NIEBLA LEVE".</summary>
    public static string Summary()
    {
        string text = Label(Current) + "  ·  " + Label(Time);

        if (Fog != FogChoice.None)
            text += "  ·  " + (Fog == FogChoice.Random ? "NIEBLA AL AZAR" : Label(Fog));

        return text;
    }

    // ---------- PlayerPrefs ----------

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
