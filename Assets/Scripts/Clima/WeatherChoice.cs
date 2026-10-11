using UnityEngine;

/// <summary>Cómo es el clima durante una carrera.</summary>
public enum WeatherScenario : byte
{
    /// <summary>Seco toda la carrera.</summary>
    Dry,

    /// <summary>Llueve toda la carrera.</summary>
    Rain,

    /// <summary>Larga lloviendo y en algún momento para (la pista se seca de a poco).</summary>
    RainStops,

    /// <summary>Larga seco y en algún momento se larga a llover.</summary>
    RainStarts
}

/// <summary>Lo que elige el anfitrión antes de la carrera: un clima fijo o al azar.</summary>
public enum WeatherChoice
{
    Random,
    Dry,
    Rain,
    RainStops,
    RainStarts
}

/// <summary>
/// El clima que eligió el anfitrión en el menú (configuración de la carrera).
/// Pasa a la carrera (lo lee RaceWeather en el servidor) y queda guardado
/// para la próxima vez.
/// </summary>
public static class WeatherChoiceStore
{
    private const string Key = "ApexRun.Clima";

    private static WeatherChoice? current;

    public static WeatherChoice Current
    {
        get
        {
            if (current == null)
            {
                int saved = PlayerPrefs.GetInt(Key, (int)WeatherChoice.Random);
                current = System.Enum.IsDefined(typeof(WeatherChoice), saved)
                    ? (WeatherChoice)saved
                    : WeatherChoice.Random;
            }

            return current.Value;
        }
        set
        {
            current = value;
            PlayerPrefs.SetInt(Key, (int)value);
            PlayerPrefs.Save();
        }
    }

    /// <summary>La opción siguiente (para el botón que va pasando de una a otra).</summary>
    public static WeatherChoice Next(WeatherChoice choice)
    {
        int count = System.Enum.GetValues(typeof(WeatherChoice)).Length;
        return (WeatherChoice)(((int)choice + 1) % count);
    }

    /// <summary>La opción anterior.</summary>
    public static WeatherChoice Previous(WeatherChoice choice)
    {
        int count = System.Enum.GetValues(typeof(WeatherChoice)).Length;
        return (WeatherChoice)(((int)choice + count - 1) % count);
    }

    public static string Label(WeatherChoice choice)
    {
        switch (choice)
        {
            case WeatherChoice.Dry: return "SECO";
            case WeatherChoice.Rain: return "LLUVIA";
            case WeatherChoice.RainStops: return "LLUVIA QUE PARA";
            case WeatherChoice.RainStarts: return "SE LARGA A LLOVER";
            default: return "AL AZAR";
        }
    }

    public static string Label(WeatherScenario scenario)
    {
        switch (scenario)
        {
            case WeatherScenario.Rain: return "lluvia";
            case WeatherScenario.RainStops: return "lluvia que para";
            case WeatherScenario.RainStarts: return "se larga a llover";
            default: return "seco";
        }
    }
}
