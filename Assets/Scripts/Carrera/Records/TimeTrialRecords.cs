using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudCode;
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
/// - Tabla: Unity Leaderboards, una por pista, sentido y pista seca o mojada
///   ("tt-race", "tt-race-r", "tt-race-wet", "tt-race-r-wet"; se crean con los
///   archivos de Assets/Servicios/Tablas). Una vuelta va a la mojada si al
///   terminarla la pista estaba mojada (IsWetNow). Ordenada de menor a
///   mayor y guarda solo la mejor marca de cada jugador. Con cada marca va un
///   poco de información (Meta): clima, hora y humedad de esa vuelta.
/// - Fantasma: Cloud Save, dato público del jugador ("ghost_tt_race"), así
///   cualquiera lo puede bajar. Es la GhostRecording comprimida.
/// - Identidad: la cuenta anónima de los servicios de Unity (la misma del
///   multijugador, ver GameSession). Cambia si cambia la computadora.
/// - Nombre: el de la contrarreloj es aparte del de multijugador (se elige en la
///   tabla, LocalName). Va en la Meta de la marca y en un dato público
///   ("tt_nombre"), así cambiarlo se ve en la tabla aunque no mejores la marca.
/// - Piezas: con cada marca van las piezas del kart (KartParts), para que el
///   fantasma y la repetición se vean con ese kart.
/// - Lo elegido para la próxima carrera (rivales, mi mejor marca online) queda
///   acá y lo usa KartGhost.
/// - Subir: el juego NO escribe en la tabla ni en Cloud Save (Access Control no lo
///   deja). Manda la vuelta al script de Cloud Code "SubirMarca"
///   (Assets/Servicios/CloudCode), que la revisa (recorrido, velocidades, agarre,
///   largada, copias, límite de envíos) y la guarda con permisos de servidor. El
///   nombre de la contrarreloj pasa igual por "GuardarNombre". Leer sigue siendo
///   directo (la tabla, los fantasmas y los nombres son públicos).
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

        /// <summary>Piezas del kart de esa vuelta (vacías en las marcas viejas).</summary>
        public KartParts Parts;
    }

    /// <summary>Cómo estaba la pista al terminar una vuelta (lo que se manda con la marca).</summary>
    public struct LapConditions
    {
        public WeatherScenario Weather;
        public TimeOfDay TimeOfDay;
        public float Wetness;

        public static LapConditions Now => new LapConditions
        {
            Weather = TrackConditions.Scenario,
            TimeOfDay = TrackConditions.TimeOfDay,
            Wetness = TrackConditions.Wetness
        };

        /// <summary>La misma regla que IsWetNow (y que el servidor).</summary>
        public bool IsWet => Wetness > WetThreshold || Weather == WeatherScenario.Rain || Weather == WeatherScenario.Storm;
    }

    // Respuestas de los scripts de Cloud Code.
    [Serializable]
    private class SubmitReply
    {
        public bool ok;
        public string codigo;
        public float tiempo;
        public int puesto;
        public float mejor;
    }

    [Serializable]
    private class NameReply
    {
        public bool ok;
        public string codigo;
        public string nombre;
    }

    /// <summary>Un rival elegido para la próxima carrera, con su fantasma ya bajado.</summary>
    public class Opponent
    {
        public Entry Entry;
        public GhostRecording Ghost;
        public Color Color;

        public KartParts Parts => Entry != null ? Entry.Parts : default;
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
        public int v = 2;
        public int clima;
        public int hora;
        public float humedad;

        // Desde v2.
        public string nombre;
        public string motor;
        public string ruedas;
        public string aero;
        public string volante;

        // Cuándo se hizo (segundos Unix, UTC): la lista de la tabla no trae la fecha.
        public long fecha;
    }

    // ---------- Para la próxima carrera (lo pone la tabla, lo usa KartGhost) ----------

    /// <summary>Tabla de la carrera que se va a correr (null: sin tabla, por ejemplo sin internet).</summary>
    public static string ActiveBoardId { get; set; }

    public static readonly List<Opponent> Opponents = new List<Opponent>();

    /// <summary>Mi mejor marca en la tabla (s), o -1 si no tengo.</summary>
    public static float MyOnlineBest { get; set; } = -1f;

    /// <summary>Mi fantasma de la tabla (por si en esta computadora no está), o null.</summary>
    public static GhostRecording MyOnlineGhost { get; set; }

    // ---------- Repetición de la tabla (la arma la tabla, la muestra RaceReplay) ----------

    /// <summary>Vueltas para ver en la repetición de la tabla (RaceSession.GhostViewer).</summary>
    public static readonly List<Opponent> ViewerGhosts = new List<Opponent>();

    /// <summary>Al volver al menú, abrir la tabla otra vez (se sale de la repetición).</summary>
    public static bool ReturnToBoard { get; set; }

    /// <summary>Rivales elegidos la última vez, por tabla (se vuelven a marcar al abrirla).</summary>
    public static readonly List<Entry> LastSelection = new List<Entry>();
    public static string LastSelectionBoard { get; set; }

    // ---------- Nombre de la contrarreloj ----------

    private const string NameKey = "ApexRun.NombreContrarreloj";
    private const string NameDataKey = "tt_nombre";
    public const int MaxNameLength = 16;

    /// <summary>Mi nombre en la tabla (aparte del de multijugador). Vacío si todavía no se eligió.</summary>
    public static string LocalName => CleanName(PlayerPrefs.GetString(NameKey, ""));

    /// <summary>Mi nombre en la tabla, o fallback si todavía no elegí uno.</summary>
    public static string LocalNameOr(string fallback)
    {
        string name = LocalName;
        return string.IsNullOrEmpty(name) ? CleanName(fallback) : name;
    }

    /// <summary>Guarda mi nombre de la tabla (en esta computadora). Devuelve el nombre limpio.</summary>
    public static string SetLocalName(string name)
    {
        name = CleanName(name);
        PlayerPrefs.SetString(NameKey, name);
        PlayerPrefs.Save();
        return name;
    }

    public static string CleanName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";

        name = name.Replace('\n', ' ').Replace('\r', ' ').Replace('<', ' ').Replace('>', ' ').Trim();

        while (name.Contains("  "))
            name = name.Replace("  ", " ");

        return name.Length > MaxNameLength ? name.Substring(0, MaxNameLength).Trim() : name;
    }

    /// <summary>Publica mi nombre de la tabla (para que los demás lo vean aunque no mejore la marca).</summary>
    public static async Task<bool> PublishNameAsync(string name)
    {
        name = CleanName(name);

        if (!IsSignedIn || string.IsNullOrEmpty(name))
            return false;

        try
        {
            NameReply reply = await CloudCodeService.Instance.CallEndpointAsync<NameReply>("GuardarNombre",
                new Dictionary<string, object> { { "nombre", name } });

            if (reply == null || !reply.ok)
                Debug.LogWarning("TimeTrialRecords: el servidor no guardó el nombre (" + (reply != null ? reply.codigo : "sin respuesta") + ").");

            return reply != null && reply.ok;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("TimeTrialRecords: no se pudo guardar el nombre: " + exception.Message);
            return false;
        }
    }

    /// <summary>El último error, para mostrar.</summary>
    public static string LastError { get; private set; }

    public static bool IsSignedIn =>
        Unity.Services.Core.UnityServices.State == Unity.Services.Core.ServicesInitializationState.Initialized &&
        AuthenticationService.Instance.IsSignedIn;

    public static string MyPlayerId => IsSignedIn ? AuthenticationService.Instance.PlayerId : null;

    /// <summary>Tabla de la pista (por ahora una sola) en ese sentido, seca o mojada.</summary>
    public static string BoardId(bool reversed, bool wet) => (reversed ? "tt-race-r" : "tt-race") + (wet ? "-wet" : "");

    public static bool IsWetBoard(string boardId) => boardId != null && boardId.EndsWith("-wet");

    /// <summary>True si la tabla es de este sentido (seca o mojada).</summary>
    public static bool IsBoardOf(string boardId, bool reversed) =>
        boardId != null && (boardId == BoardId(reversed, false) || boardId == BoardId(reversed, true));

    /// <summary>Desde esta humedad (0 a 1) la vuelta es de lluvia (igual que el "Mojado" de la tabla).</summary>
    public const float WetThreshold = 0.25f;

    /// <summary>True si la pista está mojada ahora (para saber a qué tabla va una vuelta).</summary>
    public static bool IsWetNow() =>
        TrackConditions.Wetness > WetThreshold ||
        TrackConditions.Scenario == WeatherScenario.Rain || TrackConditions.Scenario == WeatherScenario.Storm;

    /// <summary>Pestaña de la tabla que va con el clima elegido en el menú (al azar o dinámico: null).</summary>
    public static bool? WetForMenuWeather()
    {
        switch (WeatherChoiceStore.Current)
        {
            case WeatherChoice.Rain:
            case WeatherChoice.Storm:
                return true;
            case WeatherChoice.Clear:
            case WeatherChoice.Cloudy:
                return false;
            default:
                return null;
        }
    }

    /// <summary>La última pestaña de la tabla que se miró (seca o mojada).</summary>
    public static bool LastTabWet { get; set; }

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

            await LoadNamesAsync(page.Entries);
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

    /// <summary>
    /// El nombre de la contrarreloj de cada fila (el que eligió después de hacer
    /// la marca). Si no tiene, queda el de la marca.
    /// </summary>
    private static async Task LoadNamesAsync(List<Entry> entries)
    {
        var loads = new List<Task<string>>();

        foreach (Entry entry in entries)
            loads.Add(entry.IsMine ? Task.FromResult<string>(null) : LoadNameAsync(entry.PlayerId));

        await Task.WhenAll(loads);

        for (int i = 0; i < entries.Count; i++)
        {
            string name = CleanName(loads[i].Result);

            if (!string.IsNullOrEmpty(name))
                entries[i].Name = name;
        }
    }

    /// <summary>Mi nombre de la tabla guardado online (por ejemplo, al entrar a mi cuenta en otra computadora), o null.</summary>
    public static async Task<string> LoadMyNameAsync()
    {
        string id = MyPlayerId;
        return id != null ? CleanName(await LoadNameAsync(id)) : null;
    }

    private static async Task<string> LoadNameAsync(string playerId)
    {
        try
        {
            Dictionary<string, Item> items = await CloudSaveService.Instance.Data.Player.LoadAsync(
                new HashSet<string> { NameDataKey },
                new LoadOptions(new PublicReadAccessClassOptions(playerId)));

            return items.TryGetValue(NameDataKey, out Item item) ? item.Value.GetAsString() : null;
        }
        catch (Exception)
        {
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
    public static async Task<Entry> SubmitAsync(string boardId, GhostRecording ghost, string name, KartParts parts, LapConditions conditions)
    {
        LastError = null;
        name = CleanName(name);

        try
        {
            // Las piezas y el clima van agrupados (Cloud Code acepta hasta 10 parámetros).
            // La humedad va sin redondear: el servidor decide seca o mojada con la misma regla.
            var args = new Dictionary<string, object>
            {
                { "tabla", boardId },
                { "fantasma", ghost.ToCompressedBase64() },
                { "nombre", name ?? "" },
                {
                    "piezas", new Dictionary<string, object>
                    {
                        { "motor", parts.Engine ?? "" },
                        { "ruedas", parts.Wheels ?? "" },
                        { "aero", parts.AeroKit ?? "" },
                        { "volante", parts.SteeringWheel ?? "" }
                    }
                },
                {
                    "condiciones", new Dictionary<string, object>
                    {
                        { "clima", (int)conditions.Weather },
                        { "hora", (int)conditions.TimeOfDay },
                        { "humedad", conditions.Wetness }
                    }
                }
            };

            SubmitReply reply = await CloudCodeService.Instance.CallEndpointAsync<SubmitReply>("SubirMarca", args);

            if (reply == null || !reply.ok)
            {
                string code = reply != null ? reply.codigo : null;
                LastError = DescribeSubmit(code, reply != null ? reply.mejor : 0f);
                Debug.LogWarning("TimeTrialRecords: el servidor no aceptó la vuelta (" + (code ?? "sin respuesta") + ").");
                return null;
            }

            if (boardId == ActiveBoardId)
            {
                MyOnlineBest = reply.tiempo;
                MyOnlineGhost = ghost;
            }

            // Mi fila nueva, como la ve la tabla.
            Entry mine = await GetMineAsync(boardId);

            if (mine == null)
                mine = new Entry { PlayerId = MyPlayerId, Rank = reply.puesto, Time = reply.tiempo, IsMine = true };

            if (!string.IsNullOrEmpty(name))
                mine.Name = name;

            return mine;
        }
        catch (Exception exception)
        {
            LastError = Describe(exception);
            Debug.LogWarning("TimeTrialRecords: no se pudo subir la marca: " + exception.Message);
            return null;
        }
    }

    /// <summary>Por qué el servidor no aceptó la vuelta (el código de SubirMarca).</summary>
    private static string DescribeSubmit(string code, float best)
    {
        switch (code)
        {
            case "no-mejora":
                return best > 0f
                    ? GameLanguage.Text("Tu marca en esa tabla es " + FormatTime(best), "Your best on that leaderboard is " + FormatTime(best))
                    : GameLanguage.Text("No mejora tu marca", "Not your best");
            case "muchos":
            case "ocupado":
                return GameLanguage.Text("Demasiados envíos: probá en un rato", "Too many uploads, try later");
            case "copia":
                return GameLanguage.Text("La vuelta es igual a la de otro piloto", "That lap copies another driver's");
            case "tabla":
            case "clima":
            case "piezas":
            case "datos":
                return GameLanguage.Text("La vuelta llegó con datos inválidos", "The lap data was invalid");
            case "servidor":
            case null:
                return GameLanguage.Text("No se pudo subir a la tabla", "Couldn't upload");
            default:
                // recorrido, velocidad, agarre, largada, tiempo
                return GameLanguage.Text("La vuelta no pasó la revisión del servidor", "The lap failed the server check");
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

        // Mi fila: con el nombre que tengo elegido ahora.
        bool mineNamed = result.IsMine && !string.IsNullOrEmpty(LocalName);

        if (mineNamed)
            result.Name = LocalName;

        if (!string.IsNullOrEmpty(entry.Metadata))
        {
            try
            {
                Meta meta = JsonUtility.FromJson<Meta>(entry.Metadata);
                result.Weather = (WeatherScenario)meta.clima;
                result.TimeOfDay = (TimeOfDay)meta.hora;
                result.Wetness = meta.humedad;

                if (!mineNamed && !string.IsNullOrWhiteSpace(meta.nombre))
                    result.Name = CleanName(meta.nombre);

                if (result.Date == default && meta.fecha > 0)
                    result.Date = DateTimeOffset.FromUnixTimeSeconds(meta.fecha).UtcDateTime;

                result.Parts = new KartParts { Engine = meta.motor, Wheels = meta.ruedas, AeroKit = meta.aero, SteeringWheel = meta.volante };
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
