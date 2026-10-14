using UnityEngine;

/// <summary>
/// Configuración de la carrera que elige el anfitrión (o el jugador, en un
/// jugador) en el menú. Queda guardada para la próxima vez (PlayerPrefs).
/// El clima va aparte, en WeatherChoiceStore.
/// </summary>
public static class RaceSetupStore
{
    public const int MinLaps = 1;
    public const int MaxLaps = 10;
    public const int DefaultLaps = 3;

    private const string LapsKey = "ApexRun.Vueltas";

    private static int? laps;

    public static int Laps
    {
        get
        {
            if (laps == null)
                laps = Mathf.Clamp(PlayerPrefs.GetInt(LapsKey, DefaultLaps), MinLaps, MaxLaps);

            return laps.Value;
        }
        set
        {
            laps = Mathf.Clamp(value, MinLaps, MaxLaps);
            PlayerPrefs.SetInt(LapsKey, laps.Value);
            PlayerPrefs.Save();
        }
    }
}
