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
/// Cada falta es una advertencia. Con varias en poco tiempo, el servidor lo
/// saca de la partida y le dice por qué.
/// No revisa los karts del host (el suyo y los bots): esos los simula el
/// mismo servidor.
/// </summary>
public class KartMovementValidator : MonoBehaviour
{
    [SerializeField] private RaceRespawnManager respawnManager;

    [Header("Límites")]
    [Tooltip("Velocidad máxima posible (km/h), promediada en un segundo. Con margen: el kart más rápido llega a unos 190 km/h y, después de un corte de red, las copias se ponen al día un poco más rápido.")]
    [SerializeField, Min(1f)] private float maxSpeedKmh = 250f;

    [Tooltip("Movimiento de más entre dos muestras (m), además de lo que se recorre a la velocidad máxima en ese tiempo, que se considera un salto.")]
    [SerializeField, Min(1f)] private float jumpDistance = 30f;

    [Tooltip("Distancia al punto de respawn (m) para aceptar un salto como respawn.")]
    [SerializeField, Min(0.1f)] private float respawnTolerance = 4f;

    [Header("Sanción")]
    [Tooltip("Faltas para sacar al jugador de la partida.")]
    [SerializeField, Min(1)] private int maxStrikes = 3;

    [Tooltip("Las faltas más viejas que esto (s) no cuentan.")]
    [SerializeField, Min(1f)] private float strikeWindow = 30f;

    private const float SampleInterval = 0.1f;
    private const float SpeedWindow = 1f;

    // Si el servidor se traba más que esto (carga, editor, ventana en segundo
    // plano), las copias se ponen al día de golpe y parecería que saltan: se
    // deja de medir un rato.
    private const float HitchTime = 0.25f;
    private const float HitchGrace = 2f;

    private struct Sample
    {
        public float Time;
        public Vector3 Position;
    }

    private class KartTrack
    {
        public NetworkKart Kart;
        public Rigidbody Body;
        public readonly Queue<Sample> Samples = new Queue<Sample>();
        public readonly Queue<float> Strikes = new Queue<float>();
        public bool Kicked;
    }

    private readonly List<KartTrack> tracks = new List<KartTrack>();

    private float nextSample;
    private float resumeAt;

    private void OnEnable()
    {
        NetworkKart.Spawned += HandleKartSpawned;
        NetworkKart.Despawned += HandleKartDespawned;
    }

    private void OnDisable()
    {
        NetworkKart.Spawned -= HandleKartSpawned;
        NetworkKart.Despawned -= HandleKartDespawned;
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

    private void Update()
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (manager == null || !manager.IsServer)
            return;

        // Tiempo real: Time.time no avanza más de 0,33 s por frame, y después
        // de una traba mediría menos tiempo del que pasó.
        float now = Time.unscaledTime;

        if (Time.unscaledDeltaTime > HitchTime)
            resumeAt = now + HitchGrace;

        if (now < resumeAt)
        {
            foreach (KartTrack track in tracks)
                track.Samples.Clear();

            return;
        }

        if (now < nextSample)
            return;

        nextSample = now + SampleInterval;

        for (int i = tracks.Count - 1; i >= 0; i--)
        {
            KartTrack track = tracks[i];

            if (track.Kart == null || track.Body == null)
            {
                tracks.RemoveAt(i);
                continue;
            }

            Check(track, manager, now);
        }
    }

    private void Check(KartTrack track, NetworkManager manager, float now)
    {
        if (track.Kicked)
            return;

        Vector3 position = track.Body.position;

        if (track.Samples.Count > 0)
        {
            Sample last = LastSample(track);
            float step = Vector3.Distance(last.Position, position);

            // Lo que pudo recorrer desde la muestra anterior, más el margen.
            float allowed = jumpDistance + maxSpeedKmh / 3.6f * (now - last.Time);

            if (step > allowed)
            {
                track.Samples.Clear();

                if (respawnManager == null || !respawnManager.IsRespawnPosition(track.Body, position, respawnTolerance))
                    AddStrike(track, manager, now, $"salto de {step:F0} m en {now - last.Time:F2} s, de {last.Position:F0} a {position:F0}");

                track.Samples.Enqueue(new Sample { Time = now, Position = position });
                return;
            }
        }

        track.Samples.Enqueue(new Sample { Time = now, Position = position });

        // Velocidad promedio del último segundo.
        while (track.Samples.Count > 1 && now - track.Samples.Peek().Time > SpeedWindow)
            track.Samples.Dequeue();

        Sample oldest = track.Samples.Peek();
        float elapsed = now - oldest.Time;

        if (elapsed < SpeedWindow * 0.9f)
            return;

        float speedKmh = Vector3.Distance(oldest.Position, position) / elapsed * 3.6f;

        if (speedKmh > maxSpeedKmh)
        {
            track.Samples.Clear();
            track.Samples.Enqueue(new Sample { Time = now, Position = position });
            AddStrike(track, manager, now, $"velocidad de {speedKmh:F0} km/h en {elapsed:F2} s, de {oldest.Position:F0} a {position:F0}");
        }
    }

    private void AddStrike(KartTrack track, NetworkManager manager, float now, string reason)
    {
        while (track.Strikes.Count > 0 && now - track.Strikes.Peek() > strikeWindow)
            track.Strikes.Dequeue();

        track.Strikes.Enqueue(now);

        ulong clientId = track.Kart.OwnerClientId;

        Debug.LogWarning($"Anti-trampa: {track.Kart.DisplayName} (cliente {clientId}) {reason}. Falta {track.Strikes.Count} de {maxStrikes}.", track.Kart);

        if (track.Strikes.Count < maxStrikes)
            return;

        track.Kicked = true;
        manager.DisconnectClient(clientId, "Te sacaron de la partida: el kart se movió de una forma imposible (" + reason + ").");
    }

    private static Sample LastSample(KartTrack track)
    {
        Sample last = default;

        foreach (Sample sample in track.Samples)
            last = sample;

        return last;
    }

    private void HandleKartSpawned(NetworkKart kart)
    {
        NetworkManager manager = NetworkManager.Singleton;

        // Solo los karts que mueve otra computadora.
        if (kart == null || manager == null || !manager.IsServer || kart.OwnerClientId == NetworkManager.ServerClientId)
            return;

        foreach (KartTrack track in tracks)
        {
            if (track.Kart == kart)
                return;
        }

        tracks.Add(new KartTrack
        {
            Kart = kart,
            Body = kart.GetComponent<Rigidbody>()
        });
    }

    private void HandleKartDespawned(NetworkKart kart)
    {
        tracks.RemoveAll(track => track.Kart == kart);
    }
}
