using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Anti-trampa de movimiento, en el servidor. Cada jugador mueve su propio
/// kart (la física corre en su computadora), así que el servidor revisa que
/// ese movimiento sea posible:
/// - Saltos: si el kart aparece lejos de golpe y no es un respawn válido
///   (cerca del punto de su último checkpoint o de la largada).
/// - Velocidad: si en el último segundo anduvo más rápido de lo que puede un
///   kart.
/// - Reloj: si el reloj del dueño se adelanta respecto del servidor (así un
///   movimiento rápido parecería lento).
/// Mide con las posiciones que manda el dueño y el momento en que las simuló
/// (KartNetworkTransform), así las trabas de red o del servidor no cuentan.
/// Cada falta es una advertencia. Con varias en poco tiempo, el servidor lo
/// saca de la partida y le dice por qué.
/// No revisa los karts del host (el suyo y los bots): esos los simula el
/// mismo servidor.
/// </summary>
public class KartMovementValidator : MonoBehaviour
{
    [SerializeField] private RaceRespawnManager respawnManager;

    [Header("Límites")]
    [Tooltip("Velocidad máxima posible (km/h), promediada en un segundo. Con margen: el kart más rápido llega a unos 190 km/h.")]
    [SerializeField, Min(1f)] private float maxSpeedKmh = 250f;

    [Tooltip("Movimiento de más entre dos posiciones (m), además de lo que se recorre a la velocidad máxima en ese tiempo, que se considera un salto.")]
    [SerializeField, Min(1f)] private float jumpDistance = 30f;

    [Tooltip("Distancia al punto de respawn (m) para aceptar un salto como respawn.")]
    [SerializeField, Min(0.1f)] private float respawnTolerance = 4f;

    [Tooltip("Cuánto puede adelantarse el reloj del dueño (s) en una ventana de 10 s, respecto de lo más adelantado que estuvo antes. Las trabas de red lo atrasan, nunca lo adelantan.")]
    [SerializeField, Min(0.2f)] private float maxClockGain = 1.5f;

    [Header("Sanción")]
    [Tooltip("Faltas para sacar al jugador de la partida.")]
    [SerializeField, Min(1)] private int maxStrikes = 3;

    [Tooltip("Las faltas más viejas que esto (s) no cuentan.")]
    [SerializeField, Min(1f)] private float strikeWindow = 30f;

    private const double SpeedWindow = 1.0;
    private const double ClockWindow = 10.0;

    // Si el servidor se traba (carga, editor, la computadora del host ocupada),
    // lo que mandaron los jugadores se le junta y llega todo de golpe, a veces
    // con paquetes perdidos: no se puede medir bien. Se deja de medir un rato.
    private const float HitchTime = 0.25f;
    private const double HitchGrace = 2.0;

    private double resumeAt;

    private struct Sample
    {
        public double Time;
        public Vector3 Position;
    }

    private class KartTrack
    {
        public NetworkKart Kart;
        public Rigidbody Body;
        public readonly Queue<Sample> Samples = new Queue<Sample>();
        public readonly Queue<float> Strikes = new Queue<float>();
        public bool HasLast;
        public Sample Last;
        public bool Kicked;

        // Reloj: diferencia entre el reloj del dueño y el del servidor. Lo más
        // adelantado de la ventana actual y de todas las anteriores.
        public double WindowStart;
        public double WindowMaxOffset = double.MinValue;
        public double MaxOffset = double.MinValue;
    }

    private readonly Dictionary<KartNetworkTransform, KartTrack> tracks = new Dictionary<KartNetworkTransform, KartTrack>();

    private void OnEnable()
    {
        NetworkKart.Spawned += HandleKartSpawned;
        NetworkKart.Despawned += HandleKartDespawned;
    }

    private void OnDisable()
    {
        NetworkKart.Spawned -= HandleKartSpawned;
        NetworkKart.Despawned -= HandleKartDespawned;

        foreach (KartNetworkTransform source in tracks.Keys)
        {
            if (source != null)
                source.OwnerStateReceived -= HandleOwnerState;
        }

        tracks.Clear();
    }

    private void Start()
    {
        // Karts que aparecieron antes que este componente.
        foreach (NetworkKart kart in FindObjectsByType<NetworkKart>(FindObjectsSortMode.None))
        {
            if (kart.IsSpawned)
                HandleKartSpawned(kart);
        }
    }

    private void HandleOwnerState(KartNetworkTransform source, Vector3 position, double ownerTime, bool teleport)
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (manager == null || !manager.IsServer || !tracks.TryGetValue(source, out KartTrack track) || track.Kicked)
            return;

        double now = Time.realtimeSinceStartupAsDouble;

        if (Time.unscaledDeltaTime > HitchTime)
            resumeAt = now + HitchGrace;

        if (now < resumeAt)
        {
            // Se vuelve a empezar a medir después de la traba.
            track.HasLast = false;
            track.Samples.Clear();
            track.WindowMaxOffset = double.MinValue;
            return;
        }

        // El reloj del dueño no va para atrás (por si llega algo desordenado).
        if (track.HasLast)
            ownerTime = System.Math.Max(ownerTime, track.Last.Time);

        if (CheckClock(track, manager, ownerTime))
            return;

        Sample sample = new Sample { Time = ownerTime, Position = position };

        if (!track.HasLast)
        {
            track.HasLast = true;
            track.Last = sample;
            track.Samples.Enqueue(sample);
            return;
        }

        Sample last = track.Last;
        track.Last = sample;

        double elapsed = ownerTime - last.Time;
        float step = Vector3.Distance(last.Position, position);

        // Lo que pudo recorrer desde la posición anterior, más el margen.
        float allowed = jumpDistance + maxSpeedKmh / 3.6f * (float)elapsed;

        if (teleport || step > allowed)
        {
            track.Samples.Clear();
            track.Samples.Enqueue(sample);

            if (step > allowed && (respawnManager == null || !respawnManager.IsRespawnPosition(track.Body, position, respawnTolerance)))
                AddStrike(track, manager, $"salto de {step:F0} m en {elapsed:F2} s, de {last.Position:F0} a {position:F0}");

            return;
        }

        track.Samples.Enqueue(sample);

        // Velocidad promedio del último segundo.
        while (track.Samples.Count > 1 && ownerTime - track.Samples.Peek().Time > SpeedWindow)
            track.Samples.Dequeue();

        Sample oldest = track.Samples.Peek();
        double window = ownerTime - oldest.Time;

        if (window < SpeedWindow * 0.9)
            return;

        float speedKmh = Vector3.Distance(oldest.Position, position) / (float)window * 3.6f;

        if (speedKmh > maxSpeedKmh)
        {
            track.Samples.Clear();
            track.Samples.Enqueue(sample);
            AddStrike(track, manager, $"velocidad de {speedKmh:F0} km/h");
        }
    }

    /// <summary>
    /// Reloj del dueño: la diferencia con el reloj del servidor solo baja
    /// cuando algo llega tarde (red, servidor trabado). Si sube de golpe, el
    /// dueño está inventando tiempo. Se compara lo más adelantado de cada
    /// ventana de 10 s con lo más adelantado de antes. Devuelve true si hubo
    /// falta.
    /// </summary>
    private bool CheckClock(KartTrack track, NetworkManager manager, double ownerTime)
    {
        double now = Time.realtimeSinceStartupAsDouble;
        double offset = ownerTime - now;

        if (track.WindowMaxOffset == double.MinValue)
            track.WindowStart = now;

        track.WindowMaxOffset = System.Math.Max(track.WindowMaxOffset, offset);

        if (now - track.WindowStart < ClockWindow)
            return false;

        double windowMax = track.WindowMaxOffset;
        double previousMax = track.MaxOffset;

        track.WindowMaxOffset = double.MinValue;
        track.MaxOffset = System.Math.Max(previousMax, windowMax);

        if (previousMax == double.MinValue || windowMax - previousMax <= maxClockGain)
            return false;

        track.Samples.Clear();
        track.HasLast = false;
        AddStrike(track, manager, $"reloj adelantado {windowMax - previousMax:F1} s");
        return true;
    }

    private void AddStrike(KartTrack track, NetworkManager manager, string reason)
    {
        float now = Time.unscaledTime;

        while (track.Strikes.Count > 0 && now - track.Strikes.Peek() > strikeWindow)
            track.Strikes.Dequeue();

        track.Strikes.Enqueue(now);

        ulong clientId = track.Kart.OwnerClientId;

        Debug.LogWarning($"Anti-trampa: {track.Kart.DisplayName} (cliente {clientId}) {reason}. Falta {track.Strikes.Count} de {maxStrikes}.", track.Kart);

        if (track.Strikes.Count < maxStrikes)
            return;

        track.Kicked = true;
        manager.DisconnectClient(clientId, "Te sacaron de la partida: el kart se movió de una forma imposible.");
    }

    private void HandleKartSpawned(NetworkKart kart)
    {
        NetworkManager manager = NetworkManager.Singleton;

        // Solo los karts que mueve otra computadora.
        if (kart == null || manager == null || !manager.IsServer || kart.OwnerClientId == NetworkManager.ServerClientId)
            return;

        KartNetworkTransform source = kart.GetComponent<KartNetworkTransform>();

        if (source == null)
        {
            Debug.LogError("KartMovementValidator necesita un KartNetworkTransform en el kart.", kart);
            return;
        }

        if (tracks.ContainsKey(source))
            return;

        tracks.Add(source, new KartTrack
        {
            Kart = kart,
            Body = kart.GetComponent<Rigidbody>()
        });

        source.OwnerStateReceived += HandleOwnerState;
    }

    private void HandleKartDespawned(NetworkKart kart)
    {
        KartNetworkTransform source = kart != null ? kart.GetComponent<KartNetworkTransform>() : null;

        if (source == null || !tracks.Remove(source))
            return;

        source.OwnerStateReceived -= HandleOwnerState;
    }
}
