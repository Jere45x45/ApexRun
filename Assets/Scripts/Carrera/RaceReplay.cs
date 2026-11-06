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
/// Mientras se ve, se ocultan el HUD, los resultados y los karts de verdad;
/// al salir vuelve todo como estaba. Se arma solo en las escenas con
/// RaceManager (no hace falta tocar la escena).
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

    public bool IsOpen => open;

    // ------------------------------------------------------------------
    // Grabación
    // ------------------------------------------------------------------

    private void Start()
    {
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

        if (open)
            Close();
    }

    private void HandleStateChanged(RaceManager.RaceState state)
    {
        switch (state)
        {
            case RaceManager.RaceState.Countdown:
            case RaceManager.RaceState.Racing:
                if (!recording && stopAt < 0f)
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
        if (recording)
            Record();

        if (open)
            Play();
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

        // El jugador primero; si no, el primero grabado.
        focus = 0;

        for (int i = 0; i < tracks.Count; i++)
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

        if (actorsRoot != null)
            Destroy(actorsRoot.gameObject);

        actors.Clear();

        if (replayCamera != null)
            Destroy(replayCamera.gameObject);

        if (overlay != null)
            Destroy(overlay.gameObject);

        RestoreScene();

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
        }

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
        UpdateOverlay(duration);
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

        foreach (Track track in tracks)
            actors.Add(BuildActor(track));
    }

    private Actor BuildActor(Track track)
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
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
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

        actor.LastPosition = track.Samples.Count > 0 ? track.Samples[0].Position : Vector3.zero;
        return actor;
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
        title.text = "<color=#E03030>" + GameLanguage.Text("REPETICIÓN", "REPLAY") + "</color>";

        statusText = Label(top, "Estado", font, 24f, TextAlignmentOptions.Center);
        Stretch(statusText.rectTransform, new Vector2(28f, 0f), new Vector2(-28f, 0f));

        cameraText = Label(top, "Camara", font, 24f, TextAlignmentOptions.Right);
        Stretch(cameraText.rectTransform, new Vector2(28f, 0f), new Vector2(-28f, 0f));

        // Abajo: barra de tiempo y ayuda.
        RectTransform bottom = Panel(go.transform, "Abajo", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 92f));

        timeline = BuildSlider(bottom);

        TMP_Text help = Label(bottom, "Ayuda", font, 19f, TextAlignmentOptions.Center);
        Stretch(help.rectTransform, new Vector2(28f, 8f), new Vector2(-28f, -52f));
        help.text = GameLanguage.Text(
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
