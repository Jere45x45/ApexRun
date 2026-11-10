using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using Unity.Services.CloudSave.Models;
using Unity.Services.CloudSave.Models.Data.Player;
using Unity.Services.Leaderboards;
using Unity.Services.Leaderboards.Exceptions;
using Unity.Services.Leaderboards.Models;
using UnityEngine;

/// <summary>
/// Tabla de clasificación online de la contrarreloj (como PolyTrack): una fila
/// por jugador con su mejor vuelta, y el fantasma de esa vuelta para correr
/// contra él.
/// - Tabla: Unity Leaderboards, una por pista y sentido ("tt-race", "tt-race-r";
///   se crean con los archivos de Assets/Servicios/Tablas). Ordenada de menor a
///   mayor y guarda solo la mejor marca de cada jugador. Con cada marca va un
///   poco de información (Meta): clima, hora y humedad de esa vuelta.
/// - Fantasma: Cloud Save, dato público del jugador ("ghost_tt_race"), así
///   cualquiera lo puede bajar. Es la GhostRecording comprimida.
/// - Identidad: la cuenta anónima de los servicios de Unity (la misma del
///   multijugador, ver GameSession). Cambia si cambia la computadora.
/// - Lo elegido para la próxima carrera (rivales, mi mejor marca online) queda
///   acá y lo usa KartGhost.
/// Ojo: la marca y el fantasma los manda el juego, así que alguien con el juego
/// modificado podría subir uno falso. Para validarlos haría falta Cloud Code.
/// </summary>
public static class TimeTrialRecords
{
    /// <summary>Rivales que se pueden elegir a la vez.</summary>
    public const int MaxOpponents = 5;

    /// <summary>Color de cada rival, en el orden en que se eligen (el propio fantasma es celeste).</summary>
    public static readonly Color[] OpponentColors =
    {
        new Color(1f, 0.55f, 0.15f, 0.4f),
        new Color(0.95f, 0.3f, 0.8f, 0.4f),
        new Color(0.4f, 0.95f, 0.4f, 0.4f),
        new Color(1f, 0.88f, 0.2f, 0.4f),
        new Color(0.92f, 0.92f, 0.97f, 0.4f)
    };

    /// <summary>Una fila de la tabla.</summary>
    public class Entry
    {
        public string PlayerId;
        public string Name;

        /// <summary>Puesto, desde 1.</summary>
        public int Rank;

        /// <summary>Tiempo de la vuelta (s).</summary>
        public float Time;

        public DateTime Date;
        public WeatherScenario Weather;
        public TimeOfDay TimeOfDay;

        /// <summary>Qué tan mojada estaba la pista al terminar (0 a 1).</summary>
        public float Wetness;

        public bool IsMine;
    }

    /// <summary>Un rival elegido para la próxima carrera, con su fantasma ya bajado.</summary>
    public class Opponent
    {
        public Entry Entry;
        public GhostRecording Ghost;
        public Color Color;
    }

    public class Page
    {
        public List<Entry> Entries = new List<Entry>();
        public int Offset;
        public int Total;
    }

    [Serializable]
    private class Meta
    {
        public int v = 1;
        public int clima;
        public int hora;
        public float humedad;
    }

    // ---------- Para la próxima carrera (lo pone la tabla, lo usa KartGhost) ----------

    /// <summary>Tabla de la carrera que se va a correr (null: sin tabla, por ejemplo sin internet).</summary>
    public static string ActiveBoardId { get; set; }

    public static readonly List<Opponent> Opponents = new List<Opponent>();

    /// <summary>Mi mejor marca en la tabla (s), o -1 si no tengo.</summary>
    public static float MyOnlineBest { get; set; } = -1f;

    /// <summary>Mi fantasma de la tabla (por si en esta computadora no está), o null.</summary>
    public static GhostRecording MyOnlineGhost { get; set; }

    /// <summary>El último error, para mostrar.</summary>
    public static string LastError { get; private set; }

    public static bool IsSignedIn =>
        Unity.Services.Core.UnityServices.State == Unity.Services.Core.ServicesInitializationState.Initialized &&
        AuthenticationService.Instance.IsSignedIn;

    public static string MyPlayerId => IsSignedIn ? AuthenticationService.Instance.PlayerId : null;

    /// <summary>Tabla de la pista (por ahora una sola) en ese sentido.</summary>
    public static string BoardId(bool reversed) => reversed ? "tt-race-r" : "tt-race";

    private static string GhostKey(string boardId) => "ghost_" + boardId.Replace('-', '_');

    // ------------------------------------------------------------------
    // Conexión
    // ------------------------------------------------------------------

    /// <summary>Entra a los servicios con ese nombre. False sin internet (LastError dice por qué).</summary>
    public static async Task<bool> SignInAsync(string playerName)
    {
        LastError = null;

        if (GameSession.Instance == null)
        {
            LastError = "No hay GameSession.";
            return false;
        }

        bool ok = await GameSession.Instance.EnsureSignedInAsync(playerName);

        if (!ok)
            LastError = GameLanguage.Text("Sin conexión con el servidor.", "Can't reach the server.");

        return ok;
    }

    // ------------------------------------------------------------------
    // Leer
    // ------------------------------------------------------------------

    /// <summary>Una página de la tabla (desde offset), o null si falló (LastError).</summary>
    public static async Task<Page> GetPageAsync(string boardId, int offset, int limit)
    {
        try
        {
            LeaderboardScoresPage scores = await LeaderboardsService.Instance.GetScoresAsync(boardId,
                new GetScoresOptions { Offset = offset, Limit = limit, IncludeMetadata = true });

            var page = new Page { Offset = scores.Offset, Total = scores.Total };

            foreach (LeaderboardEntry entry in scores.Results)
                page.Entries.Add(ToEntry(entry));

            return page;
        }
        catch (Exception exception)
        {
            LastError = Describe(exception);
            Debug.LogWarning("TimeTrialRecords: no se pudo leer la tabla " + boardId + ": " + exception.Message);
            return null;
        }
    }

    /// <summary>Mi fila, o null si todavía no tengo marca (o si falló: ver LastError).</summary>
    public static async Task<Entry> GetMineAsync(string boardId)
    {
        LastError = null;

        try
        {
            LeaderboardEntry entry = await LeaderboardsService.Instance.GetPlayerScoreAsync(boardId,
                new GetPlayerScoreOptions { IncludeMetadata = true });

            return ToEntry(entry);
        }
        catch (LeaderboardsException exception) when (exception.Reason == LeaderboardsExceptionReason.EntryNotFound)
        {
            return null;
        }
        catch (Exception exception)
        {
            LastError = Describe(exception);
            Debug.LogWarning("TimeTrialRecords: no se pudo leer mi marca: " + exception.Message);
            return null;
        }
    }

    /// <summary>El fantasma de un jugador en esa tabla, o null.</summary>
    public static async Task<GhostRecording> LoadGhostAsync(string boardId, string playerId)
    {
        try
        {
            string key = GhostKey(boardId);
            Dictionary<string, Item> items = await CloudSaveService.Instance.Data.Player.LoadAsync(
                new HashSet<string> { key },
                new LoadOptions(new PublicReadAccessClassOptions(playerId)));

            return items.TryGetValue(key, out Item item) ? GhostRecording.FromCompressedBase64(item.Value.GetAsString()) : null;
        }
        catch (Exception exception)
        {
            LastError = Describe(exception);
            Debug.LogWarning("TimeTrialRecords: no se pudo bajar el fantasma de " + playerId + ": " + exception.Message);
            return null;
        }
    }

    // ------------------------------------------------------------------
    // Subir
    // ------------------------------------------------------------------

    /// <summary>
    /// Sube una vuelta: primero el fantasma y después la marca (así la fila nunca
    /// queda sin fantasma). Devuelve mi fila nueva, o null si falló (LastError).
    /// </summary>
    public static async Task<Entry> SubmitAsync(string boardId, GhostRecording ghost)
    {
        LastError = null;

        try
        {
            await CloudSaveService.Instance.Data.Player.SaveAsync(
                new Dictionary<string, object> { { GhostKey(boardId), ghost.ToCompressedBase64() } },
                new Unity.Services.CloudSave.Models.Data.Player.SaveOptions(new PublicWriteAccessClassOptions()));

            var meta = new Meta
            {
                clima = (int)TrackConditions.Scenario,
                hora = (int)TrackConditions.TimeOfDay,
                humedad = Mathf.Round(TrackConditions.Wetness * 100f) / 100f
            };

            LeaderboardEntry entry = await LeaderboardsService.Instance.AddPlayerScoreAsync(boardId, ghost.LapTime,
                new AddPlayerScoreOptions { Metadata = meta });

            MyOnlineBest = (float)entry.Score;
            MyOnlineGhost = ghost;

            return ToEntry(entry);
        }
        catch (Exception exception)
        {
            LastError = Describe(exception);
            Debug.LogWarning("TimeTrialRecords: no se pudo subir la marca: " + exception.Message);
            return null;
        }
    }

    // ------------------------------------------------------------------
    // Ayudas
    // ------------------------------------------------------------------

    private static Entry ToEntry(LeaderboardEntry entry)
    {
        var result = new Entry
        {
            PlayerId = entry.PlayerId,
            Name = DisplayName(entry.PlayerName),
            Rank = entry.Rank + 1,
            Time = (float)entry.Score,
            Date = entry.UpdatedTime,
            IsMine = entry.PlayerId == MyPlayerId
        };

        if (!string.IsNullOrEmpty(entry.Metadata))
        {
            try
            {
                Meta meta = JsonUtility.FromJson<Meta>(entry.Metadata);
                result.Weather = (WeatherScenario)meta.clima;
                result.TimeOfDay = (TimeOfDay)meta.hora;
                result.Wetness = meta.humedad;
            }
            catch (Exception)
            {
                // Metadata vieja o rota: sin clima.
            }
        }

        return result;
    }

    /// <summary>Nombre para mostrar: sin el "#1234" que agregan los servicios y con espacios.</summary>
    public static string DisplayName(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            return "Piloto";

        int hash = serviceName.IndexOf('#');

        if (hash > 0)
            serviceName = serviceName.Substring(0, hash);

        return serviceName.Replace('_', ' ').Trim();
    }

    public static string FormatTime(float seconds)
    {
        int minutes = Mathf.FloorToInt(seconds / 60f);
        return minutes + ":" + (seconds - minutes * 60f).ToString("00.000", CultureInfo.InvariantCulture);
    }

    /// <summary>"hoy", "ayer", "hace 3 días", "hace 2 meses"...</summary>
    public static string HowLongAgo(DateTime date)
    {
        if (date == default)
            return "";

        TimeSpan ago = DateTime.UtcNow - date.ToUniversalTime();
        int days = Mathf.Max(0, (int)ago.TotalDays);

        if (days == 0) return GameLanguage.Text("hoy", "today");
        if (days == 1) return GameLanguage.Text("ayer", "yesterday");
        if (days < 30) return GameLanguage.Text("hace " + days + " días", days + " days ago");

        int months = days / 30;

        if (months < 12)
            return months == 1 ? GameLanguage.Text("hace 1 mes", "1 month ago") : GameLanguage.Text("hace " + months + " meses", months + " months ago");

        int years = days / 365;
        return years == 1 ? GameLanguage.Text("hace 1 año", "1 year ago") : GameLanguage.Text("hace " + years + " años", years + " years ago");
    }

    /// <summary>Clima de una marca, corto ("Seco", "Lluvia", "Noche · lluvia").</summary>
    public static string ConditionsLabel(Entry entry)
    {
        string sky;

        switch (entry.Weather)
        {
            case WeatherScenario.Clear: sky = GameLanguage.Text("Seco", "Dry"); break;
            case WeatherScenario.Cloudy: sky = GameLanguage.Text("Nublado", "Cloudy"); break;
            case WeatherScenario.Rain: sky = GameLanguage.Text("Lluvia", "Rain"); break;
            case WeatherScenario.Storm: sky = GameLanguage.Text("Tormenta", "Storm"); break;

            // Lluvia que para o que empieza: lo que importa es si la pista estaba mojada.
            default:
                sky = entry.Wetness > 0.25f ? GameLanguage.Text("Mojado", "Wet") : GameLanguage.Text("Seco", "Dry");
                break;
        }

        return entry.TimeOfDay == TimeOfDay.Night ? GameLanguage.Text("Noche · ", "Night · ") + sky : sky;
    }

    private static string Describe(Exception exception)
    {
        if (exception is LeaderboardsException leaderboards)
        {
            switch (leaderboards.Reason)
            {
                case LeaderboardsExceptionReason.LeaderboardNotFound:
                    return GameLanguage.Text("La tabla todavía no existe en el servidor.", "This leaderboard doesn't exist on the server yet.");
                case LeaderboardsExceptionReason.NoInternetConnection:
                    return GameLanguage.Text("Sin internet.", "No internet connection.");
                case LeaderboardsExceptionReason.TooManyRequests:
                    return GameLanguage.Text("Demasiados pedidos: probá en un rato.", "Too many requests, try again later.");
            }
        }

        return GameLanguage.Text("No se pudo conectar con la tabla.", "Couldn't reach the leaderboard.");
    }
}
