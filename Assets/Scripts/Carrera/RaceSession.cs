using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Lo que dura más que una carrera: el fin de semana (qualy y carrera) y el
/// campeonato.
/// - La decide la computadora del anfitrión (o la de un jugador): se arma en
///   el menú (StartNew) y avanza con los botones de los resultados (después de
///   la qualy, A LA CARRERA; después de la carrera, SIGUIENTE FECHA).
/// - A los demás les llega por red (RaceSetupSync) y la leen de acá igual.
/// - Fase: Qualifying (tanda con tiempo: vale la mejor vuelta y arma la
///   grilla) o Race.
/// - Sentido: normal o al revés (TrackDirection).
/// - Campeonato: Rounds fechas (0 = carrera suelta). Puntos estilo F1 (25,
///   18, 15, 12, 10, 8, 6, 4, 2, 1). Cada fecha alterna el sentido.
/// - Karts: cada uno tiene una clave que se mantiene entre escenas: "P:" y el
///   id del jugador, o "B:" y el nombre del bot (Bot1, Bot2...).
/// </summary>
public static class RaceSession
{
    public enum SessionPhase
    {
        Race = 0,
        Qualifying = 1
    }

    /// <summary>Puntos por posición (1.º, 2.º...). Del 11.º para atrás, nada.</summary>
    public static readonly int[] Points = { 25, 18, 15, 12, 10, 8, 6, 4, 2, 1 };

    /// <summary>Una fila de la tabla del campeonato.</summary>
    public struct Standing
    {
        public string Key;
        public string Name;
        public int Points;

        /// <summary>Puesto en la última carrera (0 si no corrió).</summary>
        public int LastPosition;

        /// <summary>Puntos de la última carrera.</summary>
        public int LastPoints;
    }

    private class Entry
    {
        public string Name;
        public readonly Dictionary<int, int> PointsByRound = new Dictionary<int, int>();
        public readonly Dictionary<int, int> PositionByRound = new Dictionary<int, int>();

        public int Total
        {
            get
            {
                int total = 0;

                foreach (int points in PointsByRound.Values)
                    total += points;

                return total;
            }
        }
    }

    private static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();

    public static SessionPhase Phase { get; set; } = SessionPhase.Race;

    public static bool IsQualifying => Phase == SessionPhase.Qualifying;

    /// <summary>Sentido de esta sesión (al revés = true).</summary>
    public static bool Reversed { get; set; }

    /// <summary>Sentido de la primera fecha (para volver a empezar el campeonato).</summary>
    public static bool FirstReversed { get; private set; }

    /// <summary>Duración de la qualy (s). 0 = sin qualy.</summary>
    public static float QualifyingSeconds { get; set; }

    public static bool HasQualifying => QualifyingSeconds > 0f;

    /// <summary>Fecha actual (1 a Rounds).</summary>
    public static int Round { get; set; } = 1;

    /// <summary>Cantidad de fechas del campeonato. 0 = carrera suelta.</summary>
    public static int Rounds { get; set; }

    public static bool IsChampionship => Rounds > 0;

    public static bool IsLastRound => !IsChampionship || Round >= Rounds;

    /// <summary>Semilla de los bots (así se ven igual en la qualy y en todas las fechas).</summary>
    public static int BotSeed { get; private set; } = 12345;

    /// <summary>Orden de largada (claves de los karts), o null: la grilla de siempre.</summary>
    public static IReadOnlyList<string> GridOrder => gridOrder;

    private static List<string> gridOrder;

    /// <summary>
    /// La tabla del campeonato como texto (así viaja por red). En el anfitrión
    /// la arma Rebuild; en los demás la pone RaceSetupSync.
    /// </summary>
    public static string StandingsText { get; set; } = "";

    /// <summary>Cambió algo (fase, fecha, tabla).</summary>
    public static event Action Changed;

    // ------------------------------------------------------------------
    // Anfitrión
    // ------------------------------------------------------------------

    /// <summary>
    /// Lo elegido en el menú. rounds = 0: carrera suelta. qualifyingSeconds = 0:
    /// sin qualy. reversed: sentido de la primera fecha.
    /// </summary>
    public static void StartNew(int rounds, float qualifyingSeconds, bool reversed)
    {
        Rounds = Mathf.Max(0, rounds);
        Round = 1;
        QualifyingSeconds = Mathf.Max(0f, qualifyingSeconds);
        Reversed = reversed;
        FirstReversed = reversed;
        Phase = HasQualifying ? SessionPhase.Qualifying : SessionPhase.Race;
        BotSeed = UnityEngine.Random.Range(1, int.MaxValue);
        gridOrder = null;
        entries.Clear();
        Rebuild();
    }

    /// <summary>Terminó la qualy: la carrera larga en ese orden.</summary>
    public static void GoToRace(IReadOnlyList<string> order)
    {
        gridOrder = order != null && order.Count > 0 ? new List<string>(order) : null;
        Phase = SessionPhase.Race;
        Changed?.Invoke();
    }

    /// <summary>Siguiente fecha del campeonato: el otro sentido y, si hay, qualy.</summary>
    public static void GoToNextRound()
    {
        if (!IsChampionship || IsLastRound)
            return;

        Round++;
        Reversed = !Reversed;
        Phase = HasQualifying ? SessionPhase.Qualifying : SessionPhase.Race;
        gridOrder = null;
        Changed?.Invoke();
    }

    /// <summary>Terminó el campeonato: otro igual desde la fecha 1 (mismos bots).</summary>
    public static void RestartChampionship()
    {
        int seed = BotSeed;
        StartNew(Rounds, QualifyingSeconds, FirstReversed);
        BotSeed = seed;
    }

    /// <summary>
    /// Resultado de la carrera de esta fecha (orden de llegada). Si se vuelve a
    /// correr la misma fecha, reemplaza el resultado anterior.
    /// </summary>
    public static void RecordRace(IReadOnlyList<KeyValuePair<string, string>> finishOrder)
    {
        if (!IsChampionship || finishOrder == null)
            return;

        // La fecha se vuelve a correr: se borra lo anterior.
        foreach (Entry entry in entries.Values)
        {
            entry.PointsByRound.Remove(Round);
            entry.PositionByRound.Remove(Round);
        }

        for (int i = 0; i < finishOrder.Count; i++)
        {
            string key = finishOrder[i].Key;

            if (!entries.TryGetValue(key, out Entry entry))
            {
                entry = new Entry();
                entries[key] = entry;
            }

            entry.Name = finishOrder[i].Value;
            entry.PositionByRound[Round] = i + 1;
            entry.PointsByRound[Round] = i < Points.Length ? Points[i] : 0;
        }

        Rebuild();
    }

    /// <summary>Arma StandingsText con la tabla ordenada (más puntos primero).</summary>
    private static void Rebuild()
    {
        var list = new List<KeyValuePair<string, Entry>>(entries);

        list.Sort((a, b) =>
        {
            int byPoints = b.Value.Total.CompareTo(a.Value.Total);

            if (byPoints != 0)
                return byPoints;

            // Empate: el mejor puesto en la última carrera.
            int aLast = a.Value.PositionByRound.TryGetValue(Round, out int ap) ? ap : 99;
            int bLast = b.Value.PositionByRound.TryGetValue(Round, out int bp) ? bp : 99;
            return aLast.CompareTo(bLast);
        });

        var builder = new StringBuilder();

        foreach (KeyValuePair<string, Entry> pair in list)
        {
            pair.Value.PositionByRound.TryGetValue(Round, out int lastPosition);
            pair.Value.PointsByRound.TryGetValue(Round, out int lastPoints);

            builder.Append(Clean(pair.Key)).Append('\t')
                .Append(Clean(pair.Value.Name)).Append('\t')
                .Append(pair.Value.Total.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(lastPosition.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(lastPoints.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        StandingsText = builder.ToString();
        Changed?.Invoke();
    }

    /// <summary>Lo llama RaceSetupSync en los clientes al recibir cambios.</summary>
    public static void NotifyChanged() => Changed?.Invoke();

    // ------------------------------------------------------------------
    // Todos
    // ------------------------------------------------------------------

    /// <summary>La tabla del campeonato (ya ordenada).</summary>
    public static List<Standing> GetStandings()
    {
        var result = new List<Standing>();

        if (string.IsNullOrEmpty(StandingsText))
            return result;

        foreach (string line in StandingsText.Split('\n'))
        {
            string[] parts = line.Split('\t');

            if (parts.Length < 5)
                continue;

            int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int points);
            int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int lastPosition);
            int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int lastPoints);

            result.Add(new Standing { Key = parts[0], Name = parts[1], Points = points, LastPosition = lastPosition, LastPoints = lastPoints });
        }

        return result;
    }

    /// <summary>Clave de un kart que se mantiene entre escenas (solo sirve en el anfitrión, que crea los karts).</summary>
    public static string KartKey(Rigidbody kart)
    {
        if (kart == null)
            return "";

        if (kart.GetComponent<CustomIABots>() != null)
            return "B:" + kart.name;

        Unity.Netcode.NetworkObject networkObject = kart.GetComponent<Unity.Netcode.NetworkObject>();
        return "P:" + (networkObject != null ? networkObject.OwnerClientId : 0UL).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>"QUALY", "CARRERA", "FECHA 2/4 · CARRERA"... para títulos.</summary>
    public static string Title()
    {
        string phase = IsQualifying ? GameLanguage.Text("CLASIFICACIÓN", "QUALIFYING") : GameLanguage.Text("CARRERA", "RACE");

        if (!IsChampionship)
            return phase;

        return GameLanguage.Text("FECHA ", "ROUND ") + Round + "/" + Rounds + "  -  " + phase;
    }

    private static string Clean(string value)
    {
        return string.IsNullOrEmpty(value) ? "" : value.Replace('\t', ' ').Replace('\n', ' ');
    }
}
