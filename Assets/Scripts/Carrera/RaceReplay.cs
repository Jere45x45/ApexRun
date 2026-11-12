using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Repetición de la carrera.
/// - Graba todos los karts (los propios, los bots y los de otros jugadores)
///   desde un poco antes de la largada: posición, rotación y ángulo de las
///   ruedas delanteras, sampleRate por segundo.
/// - En los resultados aparece el botón Repetición. Se ve la carrera con
///   copias de los karts (mismos modelos y colores; las ruedas giran según
///   la velocidad y las delanteras doblan).
/// - Cámaras: Persecución (detrás del kart elegido) y TV (fijas al costado
///   de la pista, siguen al kart con zoom y cortan a la siguiente cuando el
///   kart pasa).
/// - Controles: Espacio pausa, flechas izq./der. 5 s, arriba/abajo velocidad (0,25x a 4x), Tab
///   cambia de kart (Shift+Tab al revés), C cambia de cámara, Esc sale. La
///   barra de abajo se puede arrastrar.
/// - Efectos: también se graba lo que larga cada rueda (humo, tierra, agua;
///   KartEffects.GetWheelFx) y cada golpe con chispas (KartEffects.SparksEmitted),
///   y en la repetición salen de las copias en el mismo momento.
/// - Sonido: cada copia tiene su motor (una copia del sonido del kart, manejada
///   con las rpm que salen de la velocidad grabada: DriveExternally).
/// Mientras se ve, se ocultan el HUD, los resultados y los karts de verdad (y su
/// motor); al salir vuelve todo como estaba. Se arma solo en las escenas con
/// RaceManager (no hace falta tocar la escena).
///
/// Repetición de la tabla (RaceSession.GhostViewer): la pista se carga solo para
/// ver vueltas de la contrarreloj (TimeTrialRecords.ViewerGhosts), sin correr.
/// Cada vuelta es un kart normal (no transparente), con las piezas de esa vuelta
/// (KartLook) y el nombre arriba (N lo oculta). Las ruedas delanteras doblan
/// según cómo gira el kart. Al salir se vuelve a la tabla del menú.
/// </summary>
public class RaceReplay : MonoBehaviour
{
    [Tooltip("Muestras por segundo de cada kart.")]
    [SerializeField, Min(5f)] private float sampleRate = 20f;

    [Tooltip("La repetición empieza este tiempo antes de la largada (s).")]
    [SerializeField, Min(0f)] private float preRoll = 2f;

    [Tooltip("Después de que termina la carrera se graba un rato más (s).")]
    [SerializeField, Min(0f)] private float postRoll = 4f;

    [Header("Cámara de persecución")]
    [SerializeField] private float chaseDistance = 5f;
    [SerializeField] private float chaseHeight = 1.9f;
    [SerializeField, Min(0.01f)] private float chaseSmooth = 0.12f;

    [Header("Cámaras de TV")]
    [Tooltip("Cada cuántos metros de pista hay una cámara.")]
    [SerializeField, Min(10f)] private float tvSpacing = 70f;

    [Tooltip("Qué tan lejos del borde de la pista (m).")]
    [SerializeField, Min(0f)] private float tvSideOffset = 7f;

    [SerializeField, Min(0f)] private float tvHeight = 3.5f;

    [Tooltip("Cuánto se ve alrededor del kart (m): menos = más zoom.")]
    [SerializeField, Min(0.5f)] private float tvFraming = 4.5f;

    private static readonly float[] Speeds = { 0.25f, 0.5f, 1f, 2f, 4f };

    private enum CameraMode
    {
        Chase,
        TV
    }

    private struct Sample
    {
        public float Time;
        public Vector3 Position;
        public Quaternion Rotation;
        public float Steer;

        // Lo que largaba cada rueda (FL, FR, RL, RR).
        public KartEffects.WheelFx Fx0, Fx1, Fx2, Fx3;

        public void SetFx(int wheel, KartEffects.WheelFx fx)
        {
            switch (wheel)
            {
                case 0: Fx0 = fx; break;
                case 1: Fx1 = fx; break;
                case 2: Fx2 = fx; break;
                default: Fx3 = fx; break;
            }
        }

        public KartEffects.WheelFx Fx(int wheel)
        {
            switch (wheel)
            {
                case 0: return Fx0;
                case 1: return Fx1;
                case 2: return Fx2;
                default: return Fx3;
            }
        }
    }

    /// <summary>Un golpe con chispas.</summary>
    private struct SparkEvent
    {
        public float Time;
        public Vector3 Point;
        public Vector3 Normal;
        public Vector3 Carry;
        public int Count;
        public float Strength;
    }

    /// <summary>Lo grabado de un kart.</summary>
    private class Track
    {
        public KartBehaviour Kart;
        public KartEffects Effects;
        public string Name;
        public readonly List<Sample> Samples = new List<Sample>();

        // Repetición de la tabla: piezas para vestir el kart, color del nombre y tiempo de la vuelta.
        public KartParts Parts;
        public Color LabelColor = Color.white;
        public float LapTime = -1f;
    }

    /// <summary>La copia de un kart que se ve en la repetición.</summary>
    private class Actor
    {
        public Track Track;
        public Transform Root;
        public Transform[] Wheels = new Transform[4];
        public Vector3[] WheelPositions = new Vector3[4];
        public Quaternion[] WheelModelRotations = new Quaternion[4];
        public float WheelRadius;
        public float Spin;
        public Vector3 LastPosition;

        // Partículas "a medias" de cada rueda (ver KartEffects.EmitWheelFx).
        public Vector3[] Carries = new Vector3[4];

        // Motor: copias del sonido del kart y lo que se les manda.
        public readonly List<KartEngineAudio> Synth = new List<KartEngineAudio>();
        public readonly List<KartEngineGranularAudio> Recorded = new List<KartEngineGranularAudio>();
        public readonly List<AudioSource> Sources = new List<AudioSource>();
        public float Speed;
        public float Load;

        public TextMeshPro Label;
    }

    private RaceManager raceManager;
    private RaceResultsUI resultsUI;
    private RacePauseMenu pauseMenu;
    private RaceHUD hud;
    private TrackCenterline centerline;

    private readonly List<Track> tracks = new List<Track>();
    private readonly Dictionary<KartBehaviour, Track> trackByKart = new Dictionary<KartBehaviour, Track>();
    private readonly List<SparkEvent> sparks = new List<SparkEvent>();
    private int sparkCursor;

    private bool recording;
    private double recordStart = -1.0;
    private float nextSample;
    private float raceStartTime = -1f;
    private float stopAt = -1f;

    // Reproducción.
    private bool open;
    private bool playing;
    private float time;
    private float startTime;
    private int speedIndex = 2;
    private int focus;
    private CameraMode cameraMode = CameraMode.Chase;

    private Transform actorsRoot;
    private readonly List<Actor> actors = new List<Actor>();
    private Camera replayCamera;
    private Vector3 chaseVelocity;

    // Cámaras de TV: posición y metro de pista donde está cada una.
    private readonly List<Vector3> tvPositions = new List<Vector3>();
    private readonly List<float> tvDistances = new List<float>();
    private int tvCurrent = -1;

    // Lo que se oculta mientras se ve y se vuelve a mostrar al salir.
    private readonly List<Behaviour> hiddenBehaviours = new List<Behaviour>();
    private readonly List<Renderer> hiddenRenderers = new List<Renderer>();

    // Interfaz.
    private Canvas overlay;
    private TMP_Text statusText;
    private TMP_Text cameraText;
    private Slider timeline;
    private bool settingTimeline;
    private Button replayButton;

    // Repetición de la tabla.
    private bool viewer;
    private bool viewerStarted;
    private bool viewerLeaving;
    private bool showNames = true;
    private float viewerWaitUntil = -1f;

    public bool IsOpen => open;

    // ------------------------------------------------------------------
    // Grabación
    // ------------------------------------------------------------------

    private void Start()
    {
        viewer = RaceSession.GhostViewer;

        resultsUI = FindFirstObjectByType<RaceResultsUI>(FindObjectsInactive.Include);
        pauseMenu = FindFirstObjectByType<RacePauseMenu>(FindObjectsInactive.Include);
        hud = FindFirstObjectByType<RaceHUD>(FindObjectsInactive.Include);
        centerline = FindFirstObjectByType<TrackCenterline>(FindObjectsInactive.Include);

        if (raceManager != null)
        {
            raceManager.StateChanged += HandleStateChanged;
            HandleStateChanged(raceManager.CurrentState);
        }

        KartEffects.SparksEmitted += HandleSparks;

        CreateResultsButton();
    }

    private void OnDestroy()
    {
        if (raceManager != null)
            raceManager.StateChanged -= HandleStateChanged;

        KartEffects.SparksEmitted -= HandleSparks;

        // Se cierra la escena: limpiar sin volver al menú (ya se está yendo).
        viewerLeaving = true;

        if (open)
            Close();
    }

    private void HandleStateChanged(RaceManager.RaceState state)
    {
        switch (state)
        {
            case RaceManager.RaceState.Countdown:
            case RaceManager.RaceState.Racing:
                // En la qualy no se graba (no hay repetición).
                if (!recording && stopAt < 0f && !RaceSession.IsQualifying)
                {
                    recording = true;

                    if (recordStart < 0.0)
                        recordStart = Time.timeAsDouble;
                }

                if (state == RaceManager.RaceState.Racing && raceStartTime < 0f)
                    raceStartTime = Now;
                break;

            case RaceManager.RaceState.Finished:
                if (recording && stopAt < 0f)
                    stopAt = Now + postRoll;
                break;
        }
    }

    private void HandleSparks(Vector3 point, Vector3 normal, Vector3 carry, int count, float strength)
    {
        if (!recording)
            return;

        sparks.Add(new SparkEvent { Time = Now, Point = point, Normal = normal, Carry = carry, Count = count, Strength = strength });
    }

    /// <summary>Segundos desde que se empezó a grabar (hora del juego: en pausa no avanza).</summary>
    private float Now => recordStart < 0.0 ? 0f : (float)(Time.timeAsDouble - recordStart);

    private void LateUpdate()
    {
        // En la qualy no hay repetición (ni botón).
        if (replayButton != null && replayButton.gameObject.activeSelf == RaceSession.IsQualifying)
            replayButton.gameObject.SetActive(!RaceSession.IsQualifying);

        if (viewer && !viewerStarted)
            TryStartViewer();

        if (recording)
            Record();

        if (open)
            Play();
    }

    // ------------------------------------------------------------------
    // Repetición de la tabla
    // ------------------------------------------------------------------

    /// <summary>
    /// Apenas está el kart del jugador (sirve de molde: mallas, ruedas, motor),
    /// arma una pista por vuelta de la tabla y la abre.
    /// </summary>
    private void TryStartViewer()
    {
        if (viewerWaitUntil < 0f)
            viewerWaitUntil = Time.unscaledTime + 15f;

        KartBehaviour template = FindTemplateKart();

        if (template == null || template.Vehicle == null || !template.Vehicle.IsConfigured)
        {
            // Sin kart: algo salió mal, volver al menú.
            if (Time.unscaledTime > viewerWaitUntil)
            {
                viewerStarted = true;
                LeaveViewer();
            }

            return;
        }

        viewerStarted = true;
        tracks.Clear();

        float wheelbase = Wheelbase(template);

        foreach (TimeTrialRecords.Opponent ghost in TimeTrialRecords.ViewerGhosts)
        {
            if (ghost == null || ghost.Ghost == null || ghost.Ghost.Samples == null || ghost.Ghost.Samples.Length < 2)
                continue;

            var track = new Track
            {
                Kart = template,
                Effects = null,
                Name = ghost.Entry != null ? ghost.Entry.Name : "?",
                Parts = ghost.Parts,
                LabelColor = ghost.Color,
                LapTime = ghost.Ghost.LapTime
            };

            FillFromGhost(track, ghost.Ghost, wheelbase);
            FillEffects(track, template);
            tracks.Add(track);
        }

        if (tracks.Count == 0)
        {
            LeaveViewer();
            return;
        }

        raceStartTime = 0f;
        Open();
    }

    private KartBehaviour FindTemplateKart()
    {
        if (raceManager != null)
        {
            foreach (KartBehaviour kart in raceManager.RaceKarts)
            {
                if (kart != null && kart.IsSimulated && kart.UsesPlayerAssists)
                    return kart;
            }
        }

        foreach (KartBehaviour kart in FindObjectsByType<KartBehaviour>(FindObjectsSortMode.None))
        {
            if (kart != null && kart.IsSimulated && kart.GetComponent<NetworkKart>() != null && kart.GetComponent<NetworkKart>().IsLocalPlayer)
                return kart;
        }

        return null;
    }

    /// <summary>Distancia entre los ejes del kart (m), para sacar el ángulo de las ruedas.</summary>
    private static float Wheelbase(KartBehaviour kart)
    {
        KartVehicle vehicle = kart.Vehicle;

        if (vehicle == null || vehicle.Wheels == null || vehicle.Wheels.Length < 4)
            return 1.05f;

        float front = (vehicle.Wheels[0].LocalPosition.z + vehicle.Wheels[1].LocalPosition.z) * 0.5f;
        float rear = (vehicle.Wheels[2].LocalPosition.z + vehicle.Wheels[3].LocalPosition.z) * 0.5f;
        float wheelbase = Mathf.Abs(front - rear);
        return wheelbase > 0.3f ? wheelbase : 1.05f;
    }

    /// <summary>
    /// Pasa una vuelta de la tabla a muestras de la repetición. El fantasma no
    /// guarda el volante: sale de cuánto gira el kart para lo que avanza
    /// (ángulo = atan(entre ejes × giro / velocidad)), suavizado.
    /// </summary>
    private static void FillFromGhost(Track track, GhostRecording ghost, float wheelbase)
    {
        GhostRecording.Sample[] samples = ghost.Samples;
        int count = samples.Length;
        var steer = new float[count];

        for (int i = 0; i < count; i++)
        {
            int a = Mathf.Max(0, i - 1);
            int b = Mathf.Min(count - 1, i + 1);
            float dt = samples[b].Time - samples[a].Time;

            if (dt <= 0.0001f)
                continue;

            Vector3 forward = samples[i].Rotation * Vector3.forward;
            float speed = Vector3.Dot(samples[b].Position - samples[a].Position, forward) / dt;

            if (speed < 1.5f)
                continue;

            float yawA = Yaw(samples[a].Rotation);
            float yawB = Yaw(samples[b].Rotation);
            float yawRate = Mathf.DeltaAngle(yawA, yawB) * Mathf.Deg2Rad / dt;

            steer[i] = Mathf.Clamp(Mathf.Atan(wheelbase * yawRate / speed) * Mathf.Rad2Deg, -28f, 28f);
        }

        for (int i = 0; i < count; i++)
        {
            // Promedio de las muestras de al lado (saca el temblequeo).
            float sum = 0f;
            int n = 0;

            for (int k = Mathf.Max(0, i - 3); k <= Mathf.Min(count - 1, i + 3); k++)
            {
                sum += steer[k];
                n++;
            }

            track.Samples.Add(new Sample
            {
                Time = samples[i].Time,
                Position = samples[i].Position,
                Rotation = samples[i].Rotation,
                Steer = n > 0 ? sum / n : 0f
            });
        }
    }

    private static readonly RaycastHit[] groundHits = new RaycastHit[8];
    private static readonly Dictionary<Collider, TrackSurfaceData> surfaceCache = new Dictionary<Collider, TrackSurfaceData>();

    /// <summary>
    /// Humo, tierra y agua de una vuelta de la tabla (el fantasma no los guarda):
    /// para cada muestra y cada rueda, la superficie de abajo (un rayo), la
    /// velocidad del contacto y cuánto desliza de costado, con las mismas
    /// cuentas que los karts de verdad (KartEffects.ComputeFor) y la humedad de ahora.
    /// </summary>
    private static void FillEffects(Track track, KartBehaviour template)
    {
        KartEffects effects = template.GetComponent<KartEffects>();
        KartVehicle vehicle = template.Vehicle;

        if (effects == null || vehicle == null || vehicle.Wheels == null || vehicle.Wheels.Length < 4)
            return;

        float radius = vehicle.IsConfigured ? vehicle.Tire.radius : 0.14f;
        float wetness = Mathf.Clamp01(TrackConditions.Wetness);
        List<Sample> samples = track.Samples;
        int count = samples.Count;
        surfaceCache.Clear();

        for (int i = 0; i < count; i++)
        {
            int a = Mathf.Max(0, i - 1);
            int b = Mathf.Min(count - 1, i + 1);
            float dt = samples[b].Time - samples[a].Time;

            if (dt <= 0.0001f)
                continue;

            Sample sample = samples[i];
            Vector3 velocity = (samples[b].Position - samples[a].Position) / dt;

            // Una reaparición no es velocidad.
            if (velocity.sqrMagnitude > 60f * 60f)
                continue;

            float yawRate = Mathf.DeltaAngle(Yaw(samples[a].Rotation), Yaw(samples[b].Rotation)) * Mathf.Deg2Rad / dt;
            Vector3 angular = Vector3.up * yawRate;
            Vector3 up = sample.Rotation * Vector3.up;

            for (int w = 0; w < 4; w++)
            {
                KartWheel wheel = vehicle.Wheels[w];
                bool rear = wheel.LocalPosition.z < 0f;
                Vector3 center = sample.Position + sample.Rotation * wheel.LocalPosition;

                if (!CastGround(center + up * 0.3f, -up, radius + 0.45f, out RaycastHit hit))
                    continue;

                if (!surfaceCache.TryGetValue(hit.collider, out TrackSurfaceData surface))
                {
                    TrackSurface found = hit.collider.GetComponentInParent<TrackSurface>();
                    surface = found != null ? found.SurfaceData : null;
                    surfaceCache[hit.collider] = surface;
                }

                Vector3 pointVelocity = velocity + Vector3.Cross(angular, hit.point - sample.Position);
                float steer = rear ? 0f : sample.Steer;
                Vector3 wheelForward = sample.Rotation * Quaternion.Euler(0f, steer, 0f) * Vector3.forward;
                Vector3 wheelRight = Vector3.Cross(hit.normal, wheelForward).normalized;
                float slide = Mathf.Abs(Vector3.Dot(pointVelocity, wheelRight));

                sample.SetFx(w, effects.ComputeFor(surface, pointVelocity, slide, rear, wetness));
            }

            samples[i] = sample;
        }
    }

    /// <summary>El piso más cercano abajo de un punto (sin karts).</summary>
    private static bool CastGround(Vector3 origin, Vector3 direction, float length, out RaycastHit best)
    {
        best = default;
        int hits = Physics.RaycastNonAlloc(origin, direction, groundHits, length, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        float bestDistance = float.MaxValue;

        for (int i = 0; i < hits; i++)
        {
            RaycastHit hit = groundHits[i];

            if (hit.collider.attachedRigidbody != null || hit.distance >= bestDistance)
                continue;

            best = hit;
            bestDistance = hit.distance;
        }

        return bestDistance < float.MaxValue;
    }

    /// <summary>Hacia dónde apunta la trompa, en grados alrededor del eje vertical.</summary>
    private static float Yaw(Quaternion rotation)
    {
        Vector3 forward = rotation * Vector3.forward;
        return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
    }

    /// <summary>Sale de la repetición de la tabla: vuelve al menú, a la tabla.</summary>
    private void LeaveViewer()
    {
        if (viewerLeaving)
            return;

        viewerLeaving = true;
        RaceSession.GhostViewer = false;
        TimeTrialRecords.ReturnToBoard = true;

        if (GameSession.Instance != null)
            GameSession.Instance.LeaveToMenu();
        else
            SceneManager.LoadScene("Inicio");
    }

    private void Record()
    {
        float now = Now;

        if (stopAt >= 0f && now > stopAt)
        {
            recording = false;
            return;
        }

        if (now < nextSample)
            return;

        nextSample = now + 1f / sampleRate;

        foreach (KartBehaviour kart in raceManager.RaceKarts)
        {
            if (kart == null || !kart.gameObject.activeInHierarchy)
                continue;

            if (!trackByKart.TryGetValue(kart, out Track track))
            {
                Rigidbody body = kart.GetComponent<Rigidbody>();
                track = new Track { Kart = kart, Effects = kart.GetComponent<KartEffects>(), Name = body != null ? RaceStandingsTower.DisplayName(body) : kart.name };
                trackByKart[kart] = track;
                tracks.Add(track);
            }

            // La pose que se dibuja (interpolada; en los karts de red, la que llega).
            Transform t = kart.transform;
            var sample = new Sample { Time = now, Position = t.position, Rotation = t.rotation, Steer = kart.SteerAngle };

            if (track.Effects != null)
            {
                sample.Fx0 = track.Effects.GetWheelFx(0);
                sample.Fx1 = track.Effects.GetWheelFx(1);
                sample.Fx2 = track.Effects.GetWheelFx(2);
                sample.Fx3 = track.Effects.GetWheelFx(3);
            }

            track.Samples.Add(sample);
        }
    }

    /// <summary>Cuánto dura lo grabado (s).</summary>
    private float Duration
    {
        get
        {
            float end = 0f;

            foreach (Track track in tracks)
            {
                if (track.Samples.Count > 0)
                    end = Mathf.Max(end, track.Samples[track.Samples.Count - 1].Time);
            }

            return end;
        }
    }

    // ------------------------------------------------------------------
    // Abrir y cerrar
    // ------------------------------------------------------------------

    public void Open()
    {
        if (open || tracks.Count == 0)
            return;

        open = true;
        playing = true;
        speedIndex = 2;

        startTime = raceStartTime >= 0f ? Mathf.Max(0f, raceStartTime - preRoll) : 0f;
        time = startTime;

        sparkCursor = 0;

        while (sparkCursor < sparks.Count && sparks[sparkCursor].Time <= time)
            sparkCursor++;

        // El jugador primero; si no, el primero grabado. En la de la tabla, la
        // primera vuelta (la que se eligió).
        focus = 0;

        for (int i = 0; i < tracks.Count && !viewer; i++)
        {
            if (tracks[i].Kart != null && tracks[i].Kart.UsesPlayerAssists && tracks[i].Kart.IsSimulated)
                focus = i;
        }

        BuildActors();
        HideScene();
        BuildCamera();
        BuildOverlay();
        BuildTvCameras();

        tvCurrent = -1;
        SnapCamera();
    }

    private void Close()
    {
        open = false;

        foreach (Actor actor in actors)
        {
            foreach (AudioSource source in actor.Sources)
            {
                if (source != null)
                    source.Stop();
            }
        }

        if (actorsRoot != null)
            Destroy(actorsRoot.gameObject);

        actors.Clear();

        if (replayCamera != null)
            Destroy(replayCamera.gameObject);

        if (overlay != null)
            Destroy(overlay.gameObject);

        RestoreScene();

        // La de la tabla no tiene carrera atrás: vuelve al menú.
        if (viewer)
        {
            LeaveViewer();
            return;
        }

        if (replayButton != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(replayButton.gameObject);
    }

    /// <summary>Oculta el HUD, los resultados, las cámaras y los karts de verdad.</summary>
    private void HideScene()
    {
        hiddenBehaviours.Clear();
        hiddenRenderers.Clear();

        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            HideBehaviour(canvas);

        foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            HideBehaviour(camera);

        // Que Esc no abra el menú de pausa (si todavía hay karts corriendo).
        HideBehaviour(pauseMenu);

        foreach (KartBehaviour kart in raceManager.RaceKarts)
        {
            if (kart == null)
                continue;

            foreach (Renderer renderer in kart.GetComponentsInChildren<Renderer>())
                HideRenderer(renderer);

            // El motor de verdad no: suenan las copias.
            foreach (KartEngineAudio engine in kart.GetComponentsInChildren<KartEngineAudio>())
                HideBehaviour(engine);

            foreach (KartEngineGranularAudio engine in kart.GetComponentsInChildren<KartEngineGranularAudio>())
                HideBehaviour(engine);

            // En la de la tabla nadie maneja el kart de verdad.
            if (viewer)
                HideBehaviour(kart.GetComponent<PlayerKartInput>());
        }

        // El mouse queda libre (barra y SALIR).
        HideBehaviour(FindFirstObjectByType<RaceCursorLock>());
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // El fantasma de la contrarreloj.
        KartGhost ghost = FindFirstObjectByType<KartGhost>();
        GameObject ghostKart = GameObject.Find("FantasmaKart");

        if (ghost != null && ghostKart != null)
        {
            foreach (Renderer renderer in ghostKart.GetComponentsInChildren<Renderer>(true))
                HideRenderer(renderer);
        }
    }

    private void HideBehaviour(Behaviour behaviour)
    {
        if (behaviour == null || !behaviour.enabled)
            return;

        behaviour.enabled = false;
        hiddenBehaviours.Add(behaviour);
    }

    private void HideRenderer(Renderer renderer)
    {
        if (renderer == null || !renderer.enabled)
            return;

        renderer.enabled = false;
        hiddenRenderers.Add(renderer);
    }

    private void RestoreScene()
    {
        foreach (Behaviour behaviour in hiddenBehaviours)
        {
            if (behaviour != null)
                behaviour.enabled = true;
        }

        foreach (Renderer renderer in hiddenRenderers)
        {
            if (renderer != null)
                renderer.enabled = true;
        }

        hiddenBehaviours.Clear();
        hiddenRenderers.Clear();
    }

    // ------------------------------------------------------------------
    // Reproducción
    // ------------------------------------------------------------------

    private void Play()
    {
        // Salió con Esc: no hay nada más que mostrar.
        if (!HandleInput())
            return;

        float duration = Duration;
        float previous = time;

        if (playing)
        {
            time += Time.unscaledDeltaTime * Speeds[speedIndex];

            if (time >= duration)
            {
                time = duration;
                playing = false;
            }
        }

        foreach (Actor actor in actors)
            PoseActor(actor, Time.unscaledDeltaTime * (playing ? Speeds[speedIndex] : 0f));

        if (time > previous)
            EmitEffects(previous, time);

        UpdateCamera();

        foreach (Actor actor in actors)
        {
            UpdateEngine(actor);
            UpdateLabel(actor);
        }

        UpdateOverlay(duration);
    }

    /// <summary>
    /// El motor de cada copia: rpm según la velocidad grabada (un Rotax: ralentí
    /// ~2.600, ~14.000 a fondo) y acelerador según si estaba ganando velocidad.
    /// En pausa se calla.
    /// </summary>
    private void UpdateEngine(Actor actor)
    {
        if (actor.Sources.Count == 0)
            return;

        bool audible = playing && actor.Root.gameObject.activeSelf;

        foreach (AudioSource source in actor.Sources)
        {
            if (source == null)
                continue;

            if (audible && !source.isPlaying)
                source.Play();
            else if (!audible && source.isPlaying)
                source.Pause();
        }

        if (!audible)
            return;

        List<Sample> samples = actor.Track.Samples;
        int index = FindSample(samples, time);
        Sample a = samples[Mathf.Max(0, index - 1)];
        Sample b = samples[Mathf.Min(samples.Count - 1, index + 1)];
        float span = b.Time - a.Time;

        float speed = 0f;
        float accel = 0f;

        if (span > 0.001f)
        {
            Vector3 forward = actor.Root.forward;
            speed = Mathf.Max(0f, Vector3.Dot(b.Position - a.Position, forward) / span);

            // Una reaparición no es velocidad.
            if (speed > 60f)
                speed = actor.Speed;

            accel = (speed - actor.Speed) / Mathf.Max(0.001f, Time.unscaledDeltaTime * Speeds[speedIndex]);
        }

        float smoothing = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 8f);
        actor.Speed = Mathf.Lerp(actor.Speed, speed, smoothing);

        float targetLoad = accel > 0.4f ? 1f : accel < -1.5f ? 0f : 0.35f;
        actor.Load = Mathf.Lerp(actor.Load, targetLoad, smoothing);

        float rpm = Mathf.Lerp(2600f, 14200f, Mathf.Clamp01(actor.Speed / 34f));
        float chain = actor.Speed / (2f * Mathf.PI * Mathf.Max(0.05f, actor.WheelRadius)) * 80f;

        foreach (KartEngineAudio engine in actor.Synth)
        {
            if (engine != null)
                engine.DriveExternally(rpm, actor.Load, chain);
        }

        foreach (KartEngineGranularAudio engine in actor.Recorded)
        {
            if (engine != null)
                engine.DriveExternally(rpm, actor.Load);
        }
    }

    /// <summary>El nombre arriba del kart (repetición de la tabla), de frente a la cámara.</summary>
    private void UpdateLabel(Actor actor)
    {
        if (actor.Label == null)
            return;

        // El que sigue la cámara de persecución ya se sabe quién es (dice arriba).
        bool followed = cameraMode == CameraMode.Chase && actor == FocusActor;
        bool show = showNames && !followed && actor.Root.gameObject.activeSelf && replayCamera != null;

        if (actor.Label.gameObject.activeSelf != show)
            actor.Label.gameObject.SetActive(show);

        if (!show)
            return;

        Transform label = actor.Label.transform;
        label.position = actor.Root.position + Vector3.up * 1.05f;
        label.rotation = Quaternion.LookRotation(label.position - replayCamera.transform.position);

        // Que se lea igual de lejos (hasta un punto).
        float distance = Vector3.Distance(label.position, replayCamera.transform.position);
        label.localScale = Vector3.one * Mathf.Clamp(distance / 9f, 0.6f, 3f);
    }

    /// <summary>Teclado. Devuelve false si se salió de la repetición.</summary>
    private bool HandleInput()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
            return true;

        if (keyboard.escapeKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame)
        {
            Close();
            return false;
        }

        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            // Al final, Espacio la vuelve a empezar.
            if (!playing && time >= Duration - 0.01f)
                Seek(startTime);

            playing = !playing;
        }

        if (keyboard.leftArrowKey.wasPressedThisFrame)
            Seek(time - 5f);

        if (keyboard.rightArrowKey.wasPressedThisFrame)
            Seek(time + 5f);

        if (keyboard.upArrowKey.wasPressedThisFrame)
            speedIndex = Mathf.Min(speedIndex + 1, Speeds.Length - 1);

        if (keyboard.downArrowKey.wasPressedThisFrame)
            speedIndex = Mathf.Max(speedIndex - 1, 0);

        if (keyboard.tabKey.wasPressedThisFrame && actors.Count > 0)
        {
            bool back = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            focus = (focus + (back ? actors.Count - 1 : 1)) % actors.Count;
            tvCurrent = -1;
            SnapCamera();
        }

        if (viewer && keyboard.nKey.wasPressedThisFrame)
            showNames = !showNames;

        if (keyboard.cKey.wasPressedThisFrame)
        {
            cameraMode = cameraMode == CameraMode.Chase ? CameraMode.TV : CameraMode.Chase;
            tvCurrent = -1;
            SnapCamera();
        }

        return true;
    }

    private void Seek(float target)
    {
        time = Mathf.Clamp(target, startTime, Duration);

        // Las chispas siguen desde acá (no se largan las que se saltearon).
        sparkCursor = 0;

        while (sparkCursor < sparks.Count && sparks[sparkCursor].Time <= time)
            sparkCursor++;

        // Saltando, las ruedas no giran de golpe y la cámara corta.
        foreach (Actor actor in actors)
            PoseActor(actor, 0f, true);

        tvCurrent = -1;
        SnapCamera();
    }

    private void PoseActor(Actor actor, float deltaTime, bool jump = false)
    {
        List<Sample> samples = actor.Track.Samples;

        // Antes de que apareciera o después de que se fue (red): no se ve.
        bool present = samples.Count > 1 && time >= samples[0].Time && time <= samples[samples.Count - 1].Time + 0.5f;

        if (actor.Root.gameObject.activeSelf != present)
            actor.Root.gameObject.SetActive(present);

        if (!present)
            return;

        Evaluate(samples, time, out Vector3 position, out Quaternion rotation, out float steer);
        actor.Root.SetPositionAndRotation(position, rotation);

        // Ruedas: giran lo que avanzó el kart.
        if (!jump)
        {
            float moved = Vector3.Dot(position - actor.LastPosition, rotation * Vector3.forward);
            actor.Spin = Mathf.Repeat(actor.Spin + moved / Mathf.Max(0.05f, actor.WheelRadius) * Mathf.Rad2Deg, 360f);
        }

        actor.LastPosition = position;

        for (int i = 0; i < actor.Wheels.Length; i++)
        {
            Transform wheel = actor.Wheels[i];

            if (wheel == null)
                continue;

            float wheelSteer = i < 2 ? steer : 0f;
            wheel.localPosition = actor.WheelPositions[i];
            wheel.localRotation = Quaternion.Euler(0f, wheelSteer, 0f) * Quaternion.Euler(actor.Spin, 0f, 0f) * actor.WheelModelRotations[i];
        }
    }

    /// <summary>
    /// Vuelve a largar los efectos de lo que avanzó la repetición (de from a
    /// to): lo de cada rueda de cada copia y las chispas de ese rato.
    /// </summary>
    private void EmitEffects(float from, float to)
    {
        if (replayCamera == null)
            return;

        Vector3 cameraPosition = replayCamera.transform.position;
        float advance = to - from;

        foreach (Actor actor in actors)
        {
            List<Sample> samples = actor.Track.Samples;

            if (!actor.Root.gameObject.activeSelf || samples.Count < 2)
                continue;

            int index = FindSample(samples, to);
            Sample sample = samples[index];
            Sample next = samples[Mathf.Min(index + 1, samples.Count - 1)];

            float span = next.Time - sample.Time;
            Vector3 velocity = span > 0.001f ? (next.Position - sample.Position) / span : Vector3.zero;

            // Una reaparición no es velocidad.
            if (velocity.sqrMagnitude > 80f * 80f)
                velocity = Vector3.zero;

            Vector3 up = actor.Root.up;

            for (int i = 0; i < 4; i++)
            {
                KartEffects.WheelFx fx = sample.Fx(i);

                if (fx.IsEmpty)
                {
                    actor.Carries[i] = Vector3.zero;
                    continue;
                }

                Vector3 point = actor.Root.TransformPoint(actor.WheelPositions[i]) - up * actor.WheelRadius;
                float detail = KartEffects.DetailFrom(cameraPosition, point);

                KartEffects.EmitWheelFx(fx, point, up, velocity, ref actor.Carries[i], advance * detail);
            }
        }

        while (sparkCursor < sparks.Count && sparks[sparkCursor].Time <= to)
        {
            SparkEvent spark = sparks[sparkCursor++];

            if (spark.Time <= from)
                continue;

            float detail = KartEffects.DetailFrom(cameraPosition, spark.Point);

            if (detail > 0f)
                KartEffects.EmitSparks(spark.Point, spark.Normal, spark.Carry, Mathf.RoundToInt(spark.Count * Mathf.Max(0.3f, detail)), spark.Strength);
        }
    }

    /// <summary>La última muestra que no pasó de time.</summary>
    private static int FindSample(List<Sample> samples, float time)
    {
        int low = 0;
        int high = samples.Count - 1;

        if (time <= samples[0].Time)
            return 0;

        if (time >= samples[high].Time)
            return high;

        while (high - low > 1)
        {
            int middle = (low + high) / 2;

            if (samples[middle].Time <= time)
                low = middle;
            else
                high = middle;
        }

        return low;
    }

    private static void Evaluate(List<Sample> samples, float time, out Vector3 position, out Quaternion rotation, out float steer)
    {
        int last = samples.Count - 1;

        if (time <= samples[0].Time)
        {
            position = samples[0].Position;
            rotation = samples[0].Rotation;
            steer = samples[0].Steer;
            return;
        }

        if (time >= samples[last].Time)
        {
            position = samples[last].Position;
            rotation = samples[last].Rotation;
            steer = samples[last].Steer;
            return;
        }

        int low = 0;
        int high = last;

        while (high - low > 1)
        {
            int middle = (low + high) / 2;

            if (samples[middle].Time <= time)
                low = middle;
            else
                high = middle;
        }

        Sample a = samples[low];
        Sample b = samples[high];
        float k = Mathf.InverseLerp(a.Time, b.Time, time);

        // Un salto grande entre dos muestras es una reaparición: sin interpolar.
        if ((b.Position - a.Position).sqrMagnitude > 15f * 15f)
            k = k < 0.5f ? 0f : 1f;

        position = Vector3.Lerp(a.Position, b.Position, k);
        rotation = Quaternion.Slerp(a.Rotation, b.Rotation, k);
        steer = Mathf.Lerp(a.Steer, b.Steer, k);
    }

    // ------------------------------------------------------------------
    // Copias de los karts
    // ------------------------------------------------------------------

    private static readonly MethodInfo WheelModelMethod =
        typeof(KartBehaviour).GetMethod("GetWheelModel", BindingFlags.Instance | BindingFlags.NonPublic);

    private void BuildActors()
    {
        actorsRoot = new GameObject("Repeticion").transform;
        SceneManager.MoveGameObjectToScene(actorsRoot.gameObject, gameObject.scene);

        TMP_FontAsset font = viewer && hud != null ? RaceStandingsTower.FindHudFont(hud, bold: true) : null;

        foreach (Track track in tracks)
        {
            // Con las piezas de esa vuelta (repetición de la tabla); si no, como está el kart.
            Actor actor = null;
            KartLook.WithParts(track.Kart, track.Parts, skip => actor = BuildActor(track, skip));
            actors.Add(actor);

            if (viewer)
                actor.Label = BuildLabel(actor, font);
        }
    }

    private TextMeshPro BuildLabel(Actor actor, TMP_FontAsset font)
    {
        var go = new GameObject("Nombre");
        go.transform.SetParent(actorsRoot, false);

        var label = go.AddComponent<TextMeshPro>();

        if (font != null)
            label.font = font;

        Color color = actor.Track.LabelColor;
        color.a = 1f;

        label.text = actor.Track.Name + (actor.Track.LapTime > 0f
            ? "  <size=70%>" + TimeTrialRecords.FormatTime(actor.Track.LapTime) + "</size>"
            : "");
        label.fontSize = 2.2f;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.rectTransform.sizeDelta = new Vector2(6f, 1f);
        return label;
    }

    private Actor BuildActor(Track track, ICollection<Transform> skip)
    {
        var actor = new Actor { Track = track, WheelRadius = 0.14f };
        actor.Root = new GameObject(track.Name).transform;
        actor.Root.SetParent(actorsRoot, false);

        KartBehaviour kart = track.Kart;

        if (kart == null)
            return actor;

        KartVehicle vehicle = kart.Vehicle;

        if (vehicle != null && vehicle.IsConfigured)
            actor.WheelRadius = vehicle.Tire.radius;

        Transform kartTransform = kart.transform;
        Matrix4x4 toKart = kartTransform.worldToLocalMatrix;

        // Las ruedas: cada una con un pivote que se mueve como en el juego
        // (KartWheelVisual): en su centro, con la dirección y el giro.
        var wheelModels = new Transform[4];
        KartModelController models = kart.GetComponentInChildren<KartModelController>(true);
        bool rimFacesRight = models == null || models.WheelRimFacesRight;

        for (int i = 0; i < 4; i++)
        {
            wheelModels[i] = WheelModelMethod != null ? WheelModelMethod.Invoke(kart, new object[] { i }) as Transform : null;

            if (vehicle == null || i >= vehicle.Wheels.Length)
                continue;

            KartWheel wheel = vehicle.Wheels[i];
            bool left = wheel.LocalPosition.x < 0f;

            // De acá salen los efectos aunque la rueda no tenga modelo.
            actor.WheelPositions[i] = wheel.LocalPosition;

            if (wheelModels[i] == null)
                continue;

            var pivot = new GameObject("Rueda" + i).transform;
            pivot.SetParent(actor.Root, false);

            actor.Wheels[i] = pivot;
            actor.WheelModelRotations[i] = left == rimFacesRight ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
        }

        foreach (Renderer renderer in kart.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || KartLook.IsSkipped(renderer, skip))
                continue;

            // ¿Es parte de una rueda? Va colgado de su pivote.
            int wheelIndex = -1;

            for (int i = 0; i < 4; i++)
            {
                if (wheelModels[i] != null && actor.Wheels[i] != null && renderer.transform.IsChildOf(wheelModels[i]))
                    wheelIndex = i;
            }

            Transform parent = wheelIndex >= 0 ? actor.Wheels[wheelIndex] : actor.Root;
            Matrix4x4 toParent = wheelIndex >= 0 ? wheelModels[wheelIndex].worldToLocalMatrix : toKart;

            CopyRenderer(renderer, parent, toParent);
        }

        CopyEngineSound(kart, actor);

        actor.LastPosition = track.Samples.Count > 0 ? track.Samples[0].Position : Vector3.zero;
        return actor;
    }

    /// <summary>
    /// Copia el sonido del motor que usa el kart (sintetizado o grabado, el que
    /// esté prendido) a la copia, en el mismo lugar. Lo maneja UpdateEngine.
    /// </summary>
    private static void CopyEngineSound(KartBehaviour kart, Actor actor)
    {
        Transform kartTransform = kart.transform;
        var sources = new List<GameObject>();

        foreach (KartEngineAudio engine in kart.GetComponentsInChildren<KartEngineAudio>())
        {
            if (engine.enabled && !sources.Contains(engine.gameObject))
                sources.Add(engine.gameObject);
        }

        foreach (KartEngineGranularAudio engine in kart.GetComponentsInChildren<KartEngineGranularAudio>())
        {
            if (engine.enabled && !sources.Contains(engine.gameObject))
                sources.Add(engine.gameObject);
        }

        foreach (GameObject source in sources)
        {
            // Solo el objeto del sonido (no lo que cuelga de él).
            if (source == kart.gameObject)
                continue;

            GameObject copy = Instantiate(source, actor.Root);
            copy.name = source.name;
            copy.transform.localPosition = kartTransform.InverseTransformPoint(source.transform.position);
            copy.transform.localRotation = Quaternion.Inverse(kartTransform.rotation) * source.transform.rotation;

            foreach (Transform child in copy.transform)
                Destroy(child.gameObject);

            foreach (KartEngineAudio engine in copy.GetComponents<KartEngineAudio>())
            {
                if (engine.enabled)
                    actor.Synth.Add(engine);
            }

            foreach (KartEngineGranularAudio engine in copy.GetComponents<KartEngineGranularAudio>())
            {
                if (engine.enabled)
                    actor.Recorded.Add(engine);
            }

            foreach (AudioSource audio in copy.GetComponents<AudioSource>())
                actor.Sources.Add(audio);
        }
    }

    private static void CopyRenderer(Renderer renderer, Transform parent, Matrix4x4 toParent)
    {
        Mesh mesh;
        Matrix4x4 local;

        if (renderer is SkinnedMeshRenderer skinned)
        {
            // El piloto, con la pose de ahora y sin la escala (el modelo está
            // en centímetros, ver KartGhost).
            mesh = new Mesh();
            skinned.BakeMesh(mesh, false);
            local = toParent * Matrix4x4.TRS(skinned.transform.position, skinned.transform.rotation, Vector3.one);
        }
        else if (renderer is MeshRenderer)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            mesh = filter != null ? filter.sharedMesh : null;
            local = toParent * renderer.transform.localToWorldMatrix;
        }
        else
        {
            return;
        }

        if (mesh == null)
            return;

        var part = new GameObject(renderer.name);
        part.transform.SetParent(parent, false);
        part.transform.localPosition = local.GetColumn(3);
        part.transform.localRotation = local.rotation;
        part.transform.localScale = local.lossyScale;

        part.AddComponent<MeshFilter>().sharedMesh = mesh;

        var copy = part.AddComponent<MeshRenderer>();
        copy.sharedMaterials = renderer.sharedMaterials;
        copy.shadowCastingMode = renderer.shadowCastingMode;
        copy.receiveShadows = renderer.receiveShadows;
    }

    // ------------------------------------------------------------------
    // Cámaras
    // ------------------------------------------------------------------

    private void BuildCamera()
    {
        var go = new GameObject("CamaraRepeticion");
        SceneManager.MoveGameObjectToScene(go, gameObject.scene);
        replayCamera = go.AddComponent<Camera>();

        // Igual que la cámara del juego (cielo, distancia de dibujo).
        Camera reference = null;

        foreach (Behaviour behaviour in hiddenBehaviours)
        {
            if (behaviour is Camera camera && camera.targetTexture == null)
            {
                reference = camera;
                break;
            }
        }

        if (reference != null)
        {
            replayCamera.CopyFrom(reference);
            replayCamera.targetTexture = null;
        }

        replayCamera.depth = 50f;
        replayCamera.fieldOfView = 60f;
    }

    private Actor FocusActor => actors.Count > 0 ? actors[Mathf.Clamp(focus, 0, actors.Count - 1)] : null;

    private void SnapCamera()
    {
        chaseVelocity = Vector3.zero;
        UpdateCamera(true);
    }

    private void UpdateCamera(bool snap = false)
    {
        Actor target = FocusActor;

        if (replayCamera == null || target == null || !target.Root.gameObject.activeSelf)
            return;

        Vector3 position = target.Root.position;
        Vector3 forward = Vector3.ProjectOnPlane(target.Root.forward, Vector3.up);

        if (forward.sqrMagnitude < 0.001f)
            forward = target.Root.forward;

        forward.Normalize();

        if (cameraMode == CameraMode.Chase || tvPositions.Count == 0)
        {
            Vector3 desired = position - forward * chaseDistance + Vector3.up * chaseHeight;

            replayCamera.transform.position = snap
                ? desired
                : Vector3.SmoothDamp(replayCamera.transform.position, desired, ref chaseVelocity, chaseSmooth, Mathf.Infinity, Time.unscaledDeltaTime);

            replayCamera.transform.rotation = Quaternion.LookRotation(position + forward * 2f + Vector3.up * 0.4f - replayCamera.transform.position);
            replayCamera.fieldOfView = 60f;
            return;
        }

        // TV: la cámara fija más cercana adelante del kart; cuando el kart la
        // pasa, corta a la siguiente.
        float kartDistance = centerline.GetDistance(position);

        if (tvCurrent < 0 || Ahead(tvCurrent, kartDistance) < -30f || !Visible(tvPositions[tvCurrent], position))
            tvCurrent = PickTvCamera(kartDistance, position);

        Vector3 cameraPosition = tvPositions[tvCurrent];
        Vector3 look = position + Vector3.up * 0.4f;
        float distance = Vector3.Distance(cameraPosition, look);

        replayCamera.transform.position = cameraPosition;
        replayCamera.transform.rotation = Quaternion.LookRotation(look - cameraPosition);
        replayCamera.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(tvFraming / Mathf.Max(1f, distance)) * Mathf.Rad2Deg, 8f, 60f);
    }

    /// <summary>Cuántos metros de pista adelante del kart está una cámara de TV (negativo = ya la pasó).</summary>
    private float Ahead(int camera, float kartDistance)
    {
        float length = centerline.Length;
        float delta = tvDistances[camera] - kartDistance;

        // La pista es un circuito: la diferencia más corta.
        delta = Mathf.Repeat(delta + length * 0.5f, length) - length * 0.5f;
        return delta;
    }

    private int PickTvCamera(float kartDistance, Vector3 kartPosition)
    {
        int best = 0;
        float bestScore = float.MaxValue;

        for (int i = 0; i < tvPositions.Count; i++)
        {
            float ahead = Ahead(i, kartDistance);

            // Mejor una que esté un poco adelante y que lo vea.
            if (ahead < -10f || ahead > tvSpacing * 1.5f)
                continue;

            float score = Mathf.Abs(ahead - tvSpacing * 0.35f);

            if (!Visible(tvPositions[i], kartPosition))
                score += 1000f;

            if (score < bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        return best;
    }

    /// <summary>True si desde la cámara se ve el kart (nada en el medio, salvo karts).</summary>
    private static bool Visible(Vector3 from, Vector3 kart)
    {
        Vector3 target = kart + Vector3.up * 0.5f;
        Vector3 direction = target - from;
        float distance = direction.magnitude;

        if (distance < 0.5f)
            return true;

        foreach (RaycastHit hit in Physics.RaycastAll(from, direction / distance, distance - 1.5f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.attachedRigidbody == null)
                return false;
        }

        return true;
    }

    /// <summary>Arma las cámaras de TV a lo largo de la pista, del lado que tenga vista libre.</summary>
    private void BuildTvCameras()
    {
        tvPositions.Clear();
        tvDistances.Clear();

        if (centerline == null || !centerline.IsValid)
            return;

        float length = centerline.Length;
        float halfWidth = centerline.AverageWidth * 0.5f;
        int count = Mathf.Max(4, Mathf.RoundToInt(length / tvSpacing));

        for (int i = 0; i < count; i++)
        {
            float distance = length * i / count;
            Vector3 point = centerline.GetPointAtDistance(distance);
            Vector3 direction = centerline.GetDirectionAtDistance(distance);
            Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;

            // Se prueba de un lado, del otro (alternando) y más cerca; si no, arriba.
            float first = i % 2 == 0 ? 1f : -1f;
            Vector3 chosen = point + Vector3.up * (tvHeight + 6f);
            bool found = false;

            foreach (float offset in new[] { halfWidth + tvSideOffset, halfWidth + tvSideOffset * 0.4f })
            {
                foreach (float sign in new[] { first, -first })
                {
                    Vector3 candidate = point + side * (sign * offset) + Vector3.up * tvHeight;

                    if (Visible(candidate, point) && Visible(candidate, point + direction * 25f) && Visible(candidate, point - direction * 25f))
                    {
                        chosen = candidate;
                        found = true;
                        break;
                    }
                }

                if (found)
                    break;
            }

            tvPositions.Add(chosen);
            tvDistances.Add(distance);
        }
    }

    // ------------------------------------------------------------------
    // Interfaz
    // ------------------------------------------------------------------

    private void CreateResultsButton()
    {
        if (resultsUI == null)
            return;

        FieldInfo field = typeof(RaceResultsUI).GetField("menuButton", BindingFlags.Instance | BindingFlags.NonPublic);
        Button menu = field != null ? field.GetValue(resultsUI) as Button : null;

        if (menu == null)
            return;

        replayButton = Instantiate(menu, menu.transform.parent);
        replayButton.name = "BotonRepeticion";
        replayButton.onClick = new Button.ButtonClickedEvent();
        replayButton.onClick.AddListener(Open);

        // El texto lo pone este script (no la traducción del botón original).
        foreach (LocalizedText localized in replayButton.GetComponentsInChildren<LocalizedText>(true))
            Destroy(localized);

        foreach (TMP_Text label in replayButton.GetComponentsInChildren<TMP_Text>(true))
            label.text = GameLanguage.Text("Repetición", "Replay");

        // Sin un grupo que los acomode, va arriba del botón Menú.
        if (menu.transform.parent.GetComponent<LayoutGroup>() == null)
        {
            var rect = (RectTransform)replayButton.transform;
            var menuRect = (RectTransform)menu.transform;
            rect.anchoredPosition = menuRect.anchoredPosition + new Vector2(0f, menuRect.rect.height + 16f);
        }
        else
        {
            replayButton.transform.SetSiblingIndex(menu.transform.GetSiblingIndex());
        }
    }

    private void BuildOverlay()
    {
        var go = new GameObject("InterfazRepeticion", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        SceneManager.MoveGameObjectToScene(go, gameObject.scene);

        overlay = go.GetComponent<Canvas>();
        overlay.renderMode = RenderMode.ScreenSpaceOverlay;
        overlay.sortingOrder = 500;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1536f, 864f);
        scaler.matchWidthOrHeight = 0.5f;

        TMP_FontAsset font = hud != null ? RaceStandingsTower.FindHudFont(hud, bold: false) : null;
        TMP_FontAsset bold = hud != null ? RaceStandingsTower.FindHudFont(hud, bold: true) : null;

        // Arriba: REPETICIÓN, estado y tiempo; a la derecha, cámara y kart.
        RectTransform top = Panel(go.transform, "Arriba", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -78f), new Vector2(0f, 0f));

        TMP_Text title = Label(top, "Titulo", bold, 30f, TextAlignmentOptions.Left);
        Stretch(title.rectTransform, new Vector2(28f, 0f), new Vector2(-28f, 0f));
        title.text = "<color=#E03030>" + (viewer
            ? GameLanguage.Text("REPETICIÓN · TABLA", "REPLAY · LEADERBOARD")
            : GameLanguage.Text("REPETICIÓN", "REPLAY")) + "</color>";

        statusText = Label(top, "Estado", font, 24f, TextAlignmentOptions.Center);
        Stretch(statusText.rectTransform, new Vector2(28f, 0f), new Vector2(-28f, 0f));

        cameraText = Label(top, "Camara", font, 24f, TextAlignmentOptions.Right);
        Stretch(cameraText.rectTransform, new Vector2(28f, 0f), new Vector2(-28f, 0f));

        // Abajo: barra de tiempo y ayuda.
        RectTransform bottom = Panel(go.transform, "Abajo", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 92f));

        timeline = BuildSlider(bottom);

        TMP_Text help = Label(bottom, "Ayuda", font, 19f, TextAlignmentOptions.Center);
        Stretch(help.rectTransform, new Vector2(28f, 8f), new Vector2(-28f, -52f));
        help.text = viewer
            ? GameLanguage.Text(
                "ESPACIO pausa     IZQ / DER 5 s     ARRIBA / ABAJO velocidad     TAB kart     C cámara     N nombres     ESC volver",
                "SPACE pause     LEFT / RIGHT 5 s     UP / DOWN speed     TAB kart     C camera     N names     ESC back")
            : GameLanguage.Text(
                "ESPACIO pausa     IZQ / DER 5 s     ARRIBA / ABAJO velocidad     TAB kart     C cámara     ESC salir",
                "SPACE pause     LEFT / RIGHT 5 s     UP / DOWN speed     TAB kart     C camera     ESC exit");

        // Botón Salir (para el mouse), abajo a la derecha encima de la barra.
        Button exit = BuildButton(go.transform, bold, GameLanguage.Text("SALIR", "EXIT"));
        var exitRect = (RectTransform)exit.transform;
        exitRect.anchorMin = exitRect.anchorMax = new Vector2(1f, 0f);
        exitRect.pivot = new Vector2(1f, 0f);
        exitRect.anchoredPosition = new Vector2(-28f, 104f);
        exitRect.sizeDelta = new Vector2(150f, 48f);
        exit.onClick.AddListener(() => { if (open) Close(); });
    }

    private static RectTransform Panel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        Image image = go.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.55f);
        image.raycastTarget = false;
        return rect;
    }

    private static TMP_Text Label(Transform parent, string name, TMP_FontAsset font, float size, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        var text = go.GetComponent<TextMeshProUGUI>();

        if (font != null)
            text.font = font;

        text.fontSize = size;
        text.color = Color.white;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private Slider BuildSlider(Transform parent)
    {
        var go = new GameObject("Barra", typeof(RectTransform), typeof(Slider));
        go.transform.SetParent(parent, false);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(28f, -40f);
        rect.offsetMax = new Vector2(-28f, -16f);

        Image background = NewImage("Fondo", rect, new Color(1f, 1f, 1f, 0.2f));
        StretchCentered(background.rectTransform, 8f);

        var fillArea = new GameObject("Relleno", typeof(RectTransform)).GetComponent<RectTransform>();
        fillArea.SetParent(rect, false);
        StretchCentered(fillArea, 8f);

        Image fill = NewImage("Color", fillArea, new Color(0.88f, 0.19f, 0.19f, 1f));
        fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = Vector2.one;
        fill.rectTransform.offsetMin = Vector2.zero;
        fill.rectTransform.offsetMax = Vector2.zero;

        var handleArea = new GameObject("Manija", typeof(RectTransform)).GetComponent<RectTransform>();
        handleArea.SetParent(rect, false);
        handleArea.anchorMin = Vector2.zero;
        handleArea.anchorMax = Vector2.one;
        handleArea.offsetMin = new Vector2(10f, 0f);
        handleArea.offsetMax = new Vector2(-10f, 0f);

        Image handle = NewImage("Punto", handleArea, Color.white);
        handle.rectTransform.sizeDelta = new Vector2(20f, 0f);
        handle.rectTransform.anchorMin = new Vector2(0f, 0f);
        handle.rectTransform.anchorMax = new Vector2(0f, 1f);
        handle.raycastTarget = true;

        var slider = go.GetComponent<Slider>();
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.navigation = new Navigation { mode = Navigation.Mode.None };
        slider.onValueChanged.AddListener(value =>
        {
            if (!settingTimeline)
                Seek(Mathf.Lerp(startTime, Duration, value));
        });

        // Que se pueda agarrar en cualquier parte de la barra.
        background.raycastTarget = true;
        return slider;
    }

    private static void StretchCentered(RectTransform rect, float height)
    {
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.sizeDelta = new Vector2(0f, height);
        rect.anchoredPosition = Vector2.zero;
    }

    private static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);

        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Button BuildButton(Transform parent, TMP_FontAsset font, string text)
    {
        var go = new GameObject("BotonSalir", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        Image image = go.GetComponent<Image>();
        image.color = new Color(0.63f, 0f, 0f, 0.9f);

        TMP_Text label = Label(go.transform, "Texto", font, 24f, TextAlignmentOptions.Center);
        Stretch(label.rectTransform, Vector2.zero, Vector2.zero);
        label.text = text;

        return go.GetComponent<Button>();
    }

    private void UpdateOverlay(float duration)
    {
        if (statusText == null)
            return;

        string state = playing ? "" : GameLanguage.Text("PAUSA  ", "PAUSED  ");
        string speed = Speeds[speedIndex].ToString("0.##", CultureInfo.InvariantCulture) + "x";

        // El tiempo, contado desde la largada (antes, negativo).
        float race = raceStartTime >= 0f ? time - raceStartTime : time - startTime;
        float total = raceStartTime >= 0f ? duration - raceStartTime : duration - startTime;

        statusText.text = state + speed + "     " + FormatTime(race) + " / " + FormatTime(total);

        Actor target = FocusActor;
        string camera = cameraMode == CameraMode.Chase ? GameLanguage.Text("PERSECUCIÓN", "CHASE") : "TV";
        cameraText.text = camera + "  -  " + (target != null ? target.Track.Name : "-");

        settingTimeline = true;
        timeline.value = duration > startTime ? Mathf.InverseLerp(startTime, duration, time) : 0f;
        settingTimeline = false;
    }

    private static string FormatTime(float seconds)
    {
        string sign = seconds < 0f ? "-" : "";
        seconds = Mathf.Abs(seconds);
        int minutes = Mathf.FloorToInt(seconds / 60f);
        return sign + minutes + ":" + (seconds - minutes * 60f).ToString("00.0", CultureInfo.InvariantCulture);
    }

    // ------------------------------------------------------------------
    // Armado automático (así no hace falta tocar la escena)
    // ------------------------------------------------------------------

    private static bool installerHooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void HookInstaller()
    {
        if (!installerHooked)
        {
            SceneManager.sceneLoaded += (scene, mode) => InstallIn(scene);
            installerHooked = true;
        }

        InstallIn(SceneManager.GetActiveScene());
    }

    private static void InstallIn(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            RaceManager manager = root.GetComponentInChildren<RaceManager>(true);

            if (manager == null)
                continue;

            if (FindFirstObjectByType<RaceReplay>() != null)
                return;

            var go = new GameObject("Repeticion (grabadora)");
            SceneManager.MoveGameObjectToScene(go, scene);

            var replay = go.AddComponent<RaceReplay>();
            replay.raceManager = manager;
            return;
        }
    }
}
