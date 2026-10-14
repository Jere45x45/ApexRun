using UnityEngine;

/// <summary>
/// Configuración de la carrera que elige el anfitrión (o el jugador, en un
/// jugador) en el menú. Queda guardada para la próxima vez (PlayerPrefs).
/// El clima va aparte, en WeatherChoiceStore.
/// - Vueltas: de 1 a 10.
/// - Partida privada (solo multijugador): no aparece en la lista de
///   servidores; se entra solo con el código.
/// </summary>
public static class RaceSetupStore
{
    public const int MinLaps = 1;
    public const int MaxLaps = 10;
    public const int DefaultLaps = 3;

    private const string LapsKey = "ApexRun.Vueltas";
    private const string PrivateKey = "ApexRun.Privada";

    private static int? laps;
    private static bool? privateServer;

    /// <summary>True si el servidor que se crea es privado (solo con código).</summary>
    public static bool PrivateServer
    {
        get
        {
            if (privateServer == null)
                privateServer = PlayerPrefs.GetInt(PrivateKey, 0) == 1;

            return privateServer.Value;
        }
        set
        {
            privateServer = value;
            PlayerPrefs.SetInt(PrivateKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

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
