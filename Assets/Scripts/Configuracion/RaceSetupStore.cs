using UnityEngine;

/// <summary>
/// Configuración de la carrera que elige el anfitrión (o el jugador, en un
/// jugador) en el menú. Queda guardada para la próxima vez (PlayerPrefs).
/// El clima va aparte, en WeatherChoiceStore.
/// - Vueltas: de 1 a 10.
/// - Partida privada (solo multijugador): no aparece en la lista de
///   servidores; se entra solo con el código.
/// - Bots: cuántos (de 0 a 6) y de qué nivel. Un jugador y multijugador
///   guardan la cantidad aparte: en multijugador arranca en 0, así solo hay
///   bots si el que crea la partida los pone.
/// </summary>
public static class RaceSetupStore
{
    public const int MinLaps = 1;
    public const int MaxLaps = 10;
    public const int DefaultLaps = 3;

    public const int MaxBots = 6;
    public const int DefaultSinglePlayerBots = 3;
    public const int DefaultMultiplayerBots = 0;

    private const string LapsKey = "ApexRun.Vueltas";
    private const string PrivateKey = "ApexRun.Privada";
    private const string SinglePlayerBotsKey = "ApexRun.BotsUnJugador";
    private const string MultiplayerBotsKey = "ApexRun.BotsMultijugador";
    private const string BotLevelKey = "ApexRun.NivelBots";

    private static int? laps;
    private static bool? privateServer;
    private static int? singlePlayerBots;
    private static int? multiplayerBots;
    private static RaceDifficulty? botDifficulty;

    /// <summary>
    /// Bots de la carrera que se está por correr: los pone el menú al
    /// confirmar (un jugador o crear servidor). -1 = no pasó por el menú (por
    /// ejemplo, al probar Race directo en el editor): se usan los de un jugador.
    /// </summary>
    public static int BotsForRace { get; set; } = -1;

    /// <summary>Cuántos bots poner en esta carrera.</summary>
    public static int RaceBots => Mathf.Clamp(BotsForRace >= 0 ? BotsForRace : GetBots(false), 0, MaxBots);

    /// <summary>Cantidad de bots guardada para un jugador (false) o multijugador (true).</summary>
    public static int GetBots(bool multiplayer)
    {
        if (multiplayer)
        {
            if (multiplayerBots == null)
                multiplayerBots = Mathf.Clamp(PlayerPrefs.GetInt(MultiplayerBotsKey, DefaultMultiplayerBots), 0, MaxBots);

            return multiplayerBots.Value;
        }

        if (singlePlayerBots == null)
            singlePlayerBots = Mathf.Clamp(PlayerPrefs.GetInt(SinglePlayerBotsKey, DefaultSinglePlayerBots), 0, MaxBots);

        return singlePlayerBots.Value;
    }

    public static void SetBots(bool multiplayer, int value)
    {
        value = Mathf.Clamp(value, 0, MaxBots);

        if (multiplayer)
            multiplayerBots = value;
        else
            singlePlayerBots = value;

        PlayerPrefs.SetInt(multiplayer ? MultiplayerBotsKey : SinglePlayerBotsKey, value);
        PlayerPrefs.Save();
    }

    /// <summary>Nivel de los bots (fácil, medio o difícil).</summary>
    public static RaceDifficulty BotDifficulty
    {
        get
        {
            if (botDifficulty == null)
                botDifficulty = (RaceDifficulty)Mathf.Clamp(PlayerPrefs.GetInt(BotLevelKey, (int)RaceDifficulty.Medium), 0, 2);

            return botDifficulty.Value;
        }
        set
        {
            botDifficulty = value;
            PlayerPrefs.SetInt(BotLevelKey, (int)value);
            PlayerPrefs.Save();
        }
    }

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
