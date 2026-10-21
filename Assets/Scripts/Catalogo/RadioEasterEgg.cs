using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

/// <summary>
/// Easter Egg del Catálogo: al hacer clic en la radio del taller, la radio y
/// los autos (la camioneta y el Passat) bailan con sus animaciones, el kart se
/// suma rebotando, y la cámara los recorre como en un videoclip:
/// - entra a la radio con un dolly zoom (primero se cierra el zoom de golpe, y
///   mientras se acerca lo va abriendo: la radio queda del mismo tamaño y el
///   fondo se estira),
/// - pasa de uno a otro con latigazos (un giro rápido),
/// - y al final hace cortes rápidos al ritmo.
/// Al terminar, corta a la radio y vuelve sola a la estación en la que estaba.
///
/// - Dura "duration" segundos. La música (opcional) suena durante ese tiempo y
///   se apaga de a poco mientras la cámara vuelve.
/// - El ritmo (bpm) marca todo: el baile, el golpe de zoom y el balanceo de la
///   cámara en cada pulso, y cuánto dura cada toma (en pulsos). Las
///   animaciones del taller son de medio segundo: a 120 bpm, un rebote por pulso.
/// - Las animaciones vienen de los FBX. Se usa lo que cambia (el rebote), pero
///   cada objeto se queda donde está en la escena: los clips también guardan
///   la posición y la rotación que tenía el objeto en Blender, y aplicados tal
///   cual lo harían saltar a otro lugar.
/// - Cada toma es un punto de cámara de la escena (como en las estaciones):
///   para cambiarla, mover ese objeto. Si tiene un componente Camera, se usa su
///   campo de visión; esas cámaras solo marcan el lugar y quedan apagadas.
/// - Mientras dura, se esconde la interfaz del Catálogo y no se puede cambiar
///   de pestaña. Otro clic o Esc lo cortan.
/// </summary>
[DefaultExecutionOrder(100)] // Después del taller (CatalogWorkshop), que ubica el kart.
public class RadioEasterEgg : MonoBehaviour
{
    public enum Transition
    {
        Corte,
        Latigazo,
        DollyZoom,
        Suave
    }

    [Serializable]
    public class Dancer
    {
        public string name;

        [Tooltip("Objeto que baila. Se queda donde está en la escena: de la animación se usa lo que cambia (el rebote).")]
        public Transform target;

        [Tooltip("Animación del FBX del objeto.")]
        public AnimationClip clip;

        [Tooltip("Cuántos pulsos dura una vuelta de la animación. Las del taller duran medio segundo: 1 pulso a 120 bpm.")]
        [Min(0.25f)] public float beatsPerLoop = 1f;

        [Tooltip("Al aplastarse no se hunde ni flota: su parte más baja queda a la misma altura.")]
        public bool keepOnGround = true;
    }

    [Serializable]
    public class Shot
    {
        public string name;

        [Tooltip("Punto de la cámara (posición y rotación). Si tiene un componente Camera, se usa su campo de visión.")]
        public Transform cameraPoint;

        [Tooltip("Cuántos pulsos dura la toma.")]
        [Min(0.5f)] public float beats = 4f;

        [Tooltip("Cómo se llega desde la toma anterior. Corte: salta directo. Latigazo: un giro rápido. " +
                 "DollyZoom: se cierra el zoom de golpe y se acerca abriéndolo (lo que mira queda del mismo tamaño y el fondo se estira). " +
                 "Suave: se desliza.")]
        public Transition transition = Transition.Latigazo;

        [Tooltip("Balanceo de la cámara al ritmo, de un lado al otro (grados).")]
        [Range(0f, 20f)] public float sway = 4f;

        [Tooltip("Golpe de zoom en cada pulso (grados de campo de visión que se cierra).")]
        [Range(0f, 15f)] public float beatZoom = 3f;
    }

    // Estado de un objeto mientras baila.
    private class DancerState
    {
        public Dancer config;
        public Animator animator;
        public bool addedAnimator;
        public PlayableGraph graph;
        public AnimationClipPlayable playable;
        public Renderer[] renderers;

        // Cómo está en la escena.
        public Vector3 basePosition;
        public Quaternion baseRotation;
        public Vector3 baseScale;
        public float baseBottom;

        // Cómo lo deja el clip al principio (lo que se compara para saber qué cambia).
        public Vector3 clipPosition;
        public Quaternion clipRotation;
        public Vector3 clipScale;
    }

    private enum Phase
    {
        Off,
        Show,
        Return
    }

    [Header("Referencias")]
    [Tooltip("Taller del Catálogo: le presta la cámara mientras dura y no deja cambiar de pestaña.")]
    [SerializeField] private CatalogWorkshop workshop;

    [Tooltip("Cámara del Catálogo (Main Camera).")]
    [SerializeField] private Camera viewCamera;

    [Tooltip("Collider de la radio: un clic en él arranca el Easter Egg.")]
    [SerializeField] private Collider radio;

    [Tooltip("Interfaz que se esconde mientras dura (el Canvas del Catálogo).")]
    [SerializeField] private CanvasGroup[] hideDuringShow = new CanvasGroup[0];

    [Header("Duración")]
    [Tooltip("Cuánto dura el Easter Egg (s): el baile, la música y el recorrido de la cámara. Después la cámara vuelve a la estación.")]
    [SerializeField, Min(1f)] private float duration = 12f;

    [Tooltip("Ritmo (pulsos por minuto). Cuando se agregue la música, poner el de la canción.")]
    [SerializeField, Range(40f, 240f)] private float bpm = 120f;

    [Tooltip("Lo que tarda la cámara en volver a la estación al terminar (s).")]
    [SerializeField, Min(0.1f)] private float returnTime = 1.4f;

    [Tooltip("Se puede cortar antes con otro clic o con Esc.")]
    [SerializeField] private bool canSkip = true;

    [Header("Baile")]
    [SerializeField] private Dancer[] dancers = new Dancer[0];

    [Tooltip("Kart del Catálogo: también rebota al ritmo. Vacío = no baila.")]
    [SerializeField] private Transform kart;

    [Tooltip("Cuánto se aplasta el kart en cada pulso (fracción de su alto).")]
    [SerializeField, Range(0f, 0.3f)] private float kartBounce = 0.08f;

    [Header("Cámara")]
    [Tooltip("Tomas, en orden.")]
    [SerializeField] private Shot[] shots = new Shot[0];

    [Tooltip("Cuando se terminan las tomas y todavía queda tiempo, se repiten desde esta (empieza en 0).")]
    [SerializeField, Min(0)] private int repeatFrom = 4;

    [Tooltip("Lo que dura un latigazo (s).")]
    [SerializeField, Min(0.05f)] private float whipTime = 0.3f;

    [Tooltip("Cuánto gira la cámara en un latigazo (grados).")]
    [SerializeField, Range(10f, 180f)] private float whipAngle = 75f;

    [Tooltip("Lo que dura la entrada con dolly zoom (s).")]
    [SerializeField, Min(0.1f)] private float dollyZoomTime = 1.1f;

    [Tooltip("Distancia entre la toma del dolly zoom y lo que mira (m): eso es lo que queda del mismo tamaño.")]
    [SerializeField, Min(0.1f)] private float dollyZoomSubjectDistance = 0.9f;

    [Tooltip("Cuánto sube la cámara a mitad de camino en el dolly zoom y al volver (m), para pasar por arriba del kart.")]
    [SerializeField, Min(0f)] private float travelArc = 0.5f;

    [Header("Música")]
    [Tooltip("Canción (vacío = sin música). Si es más corta que la duración, se repite.")]
    [SerializeField] private AudioClip music;

    [Tooltip("De dónde sale la música. Vacío = se crea una en este objeto (sin sonido 3D).")]
    [SerializeField] private AudioSource musicSource;

    [SerializeField, Range(0f, 1f)] private float musicVolume = 0.8f;

    // Lo que tarda la entrada suave a una toma (s).
    private const float SmoothTime = 0.6f;

    // Lo que tardan los objetos en quedarse quietos al terminar (s).
    private const float SettleTime = 0.3f;

    private Phase phase;
    private float showTime;
    private float danceTime;
    private float returnElapsed;

    private int shotIndex;
    private float shotStart;
    private float shotEnd;
    private float whipSide = 1f;

    // Pose de la cámara al llegar a la toma actual (de donde arranca la transición).
    private Vector3 fromPosition;
    private Quaternion fromRotation;
    private float fromFieldOfView;

    // Última pose que se le puso a la cámara.
    private Vector3 lastPosition;
    private Quaternion lastRotation;
    private float lastFieldOfView;

    // Cómo estaba la cámara en la estación (adonde vuelve).
    private Vector3 restPosition;
    private Quaternion restRotation;
    private float restFieldOfView;
    private Vector2 restLensShift;

    private Vector3 returnFromPosition;
    private Quaternion returnFromRotation;
    private float returnFromFieldOfView;

    private Vector3 kartBaseScale = Vector3.one;

    private readonly List<DancerState> dancing = new List<DancerState>();

    private struct InterfaceState
    {
        public float alpha;
        public bool interactable;
        public bool blocksRaycasts;
    }

    private readonly List<InterfaceState> interfaceStates = new List<InterfaceState>();

    /// <summary>True mientras dura el Easter Egg (incluida la vuelta de la cámara).</summary>
    public bool IsPlaying => phase != Phase.Off;

    private float Beat => 60f / Mathf.Max(1f, bpm);

    private void Awake()
    {
        // Las cámaras de las tomas solo marcan el lugar.
        foreach (Shot shot in shots)
        {
            if (shot == null || shot.cameraPoint == null)
                continue;

            Camera marker = shot.cameraPoint.GetComponent<Camera>();

            if (marker != null && marker != viewCamera)
                marker.enabled = false;

            AudioListener listener = shot.cameraPoint.GetComponent<AudioListener>();

            if (listener != null)
                listener.enabled = false;
        }

        if (musicSource == null)
        {
            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.spatialBlend = 0f;
        }
    }

    private void OnDisable()
    {
        if (phase != Phase.Off)
            Finish();
    }

    private void Update()
    {
        if (phase == Phase.Off)
        {
            if (WasClicked() && CanStart() && IsPointerOnRadio(PointerPosition()))
                Play();

            return;
        }

        if (phase == Phase.Show && canSkip && (WasClicked() || WasEscapePressed()))
            BeginReturn();
    }

    private void LateUpdate()
    {
        if (phase == Phase.Off)
            return;

        float deltaTime = Time.deltaTime;
        danceTime += deltaTime;

        if (phase == Phase.Show)
        {
            showTime += deltaTime;

            if (showTime >= duration)
                BeginReturn();
        }

        float weight = 1f;

        if (phase == Phase.Show)
        {
            UpdateShowCamera();
            SetInterfaceAlpha(1f - Mathf.Clamp01(showTime / 0.25f));
        }
        else
        {
            returnElapsed += deltaTime;
            float u = Mathf.Clamp01(returnElapsed / returnTime);

            UpdateReturnCamera(u);
            SetInterfaceAlpha(Mathf.Clamp01((u - 0.5f) / 0.5f));

            if (musicSource != null)
                musicSource.volume = musicVolume * (1f - u);

            weight = 1f - Mathf.Clamp01(returnElapsed / SettleTime);

            if (u >= 1f)
            {
                Finish();
                return;
            }
        }

        UpdateDancers(weight);
        UpdateKart(weight);
    }

    /// <summary>Arranca el Easter Egg (lo que hace el clic en la radio).</summary>
    public void Play()
    {
        if (phase != Phase.Off || viewCamera == null)
            return;

        phase = Phase.Show;
        showTime = 0f;
        danceTime = 0f;
        returnElapsed = 0f;

        Transform cameraTransform = viewCamera.transform;
        restPosition = cameraTransform.position;
        restRotation = cameraTransform.rotation;
        restFieldOfView = viewCamera.fieldOfView;
        restLensShift = viewCamera.lensShift;

        lastPosition = restPosition;
        lastRotation = restRotation;
        lastFieldOfView = restFieldOfView;

        shotIndex = -1;
        shotStart = 0f;
        shotEnd = 0f;

        if (workshop != null)
            workshop.CameraHeld = true;

        StartDancers();

        if (kart != null)
            kartBaseScale = kart.localScale;

        HideInterface();

        if (music != null && musicSource != null)
        {
            musicSource.clip = music;
            musicSource.loop = true;
            musicSource.volume = musicVolume;
            musicSource.Play();
        }
    }

    /// <summary>Corta el Easter Egg: la cámara vuelve a la estación.</summary>
    public void Stop()
    {
        if (phase == Phase.Show)
            BeginReturn();
    }

    /// <summary>True si el puntero (en píxeles de la pantalla) está sobre la radio y no hay nada adelante.</summary>
    public bool IsPointerOnRadio(Vector2 screenPosition)
    {
        if (viewCamera == null || radio == null)
            return false;

        Ray ray = viewCamera.ScreenPointToRay(screenPosition);

        return Physics.Raycast(ray, out RaycastHit hit, 500f, ~0, QueryTriggerInteraction.Ignore) &&
               hit.collider == radio;
    }

    private bool CanStart()
    {
        if (workshop != null && workshop.IsBusy)
            return false;

        // El clic fue en un botón o en la lista de piezas.
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return false;

        return true;
    }

    private static bool WasClicked()
    {
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
    }

    private static bool WasEscapePressed()
    {
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
    }

    private static Vector2 PointerPosition()
    {
        return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
    }

    // ---------------------------------------------------------------- Cámara

    private void UpdateShowCamera()
    {
        if (!HasShots())
            return;

        while (showTime >= shotEnd)
            NextShot();

        GetShotCamera(shots[shotIndex], showTime - shotStart, out Vector3 position, out Quaternion rotation, out float fieldOfView);
        ApplyCamera(position, rotation, fieldOfView);

        // El encuadre corrido de las estaciones (para la lista de piezas) se
        // centra: la interfaz está escondida.
        viewCamera.lensShift = Vector2.Lerp(restLensShift, Vector2.zero, Mathf.Clamp01(showTime / 0.3f));
    }

    private void NextShot()
    {
        int next = shotIndex + 1;

        if (next >= shots.Length)
            next = Mathf.Clamp(repeatFrom, 0, shots.Length - 1);

        // Las tomas sin punto de cámara se saltean.
        for (int i = 0; i < shots.Length && (shots[next] == null || shots[next].cameraPoint == null); i++)
            next = (next + 1) % shots.Length;

        shotIndex = next;
        shotStart = shotEnd;
        shotEnd = shotStart + Mathf.Max(0.5f, shots[next].beats) * Beat;

        fromPosition = lastPosition;
        fromRotation = lastRotation;
        fromFieldOfView = lastFieldOfView;

        // El latigazo gira para el lado en el que está la toma nueva.
        Vector3 toNext = shots[next].cameraPoint.position - fromPosition;
        float side = Vector3.Cross(fromRotation * Vector3.forward, toNext).y;
        whipSide = side >= 0f ? 1f : -1f;
    }

    /// <summary>Pose de la cámara en una toma: el punto de la toma, el ritmo y la transición desde la anterior.</summary>
    private void GetShotCamera(Shot shot, float localTime, out Vector3 position, out Quaternion rotation, out float fieldOfView)
    {
        Transform point = shot.cameraPoint;
        Vector3 pointPosition = point.position;
        Quaternion pointRotation = point.rotation;
        float pointFieldOfView = GetFieldOfView(point);

        float transitionLength;

        switch (shot.transition)
        {
            case Transition.Latigazo: transitionLength = whipTime; break;
            case Transition.DollyZoom: transitionLength = dollyZoomTime; break;
            case Transition.Suave: transitionLength = SmoothTime; break;
            default: transitionLength = 0f; break;
        }

        // El ritmo: la cámara se balancea de un lado al otro (un lado por
        // pulso) y en cada pulso el zoom da un golpe hacia adentro. Después de
        // una entrada lenta, el ritmo arranca de a poco para que no salte.
        bool slowEntry = shot.transition == Transition.DollyZoom || shot.transition == Transition.Suave;
        float rhythm = slowEntry ? Mathf.SmoothStep(0f, 1f, (localTime - transitionLength) / 0.3f) : 1f;

        float beats = danceTime / Beat;
        float pulse = Mathf.Exp(-(beats - Mathf.Floor(beats)) * 7f);

        position = pointPosition;
        rotation = pointRotation * Quaternion.AngleAxis(shot.sway * Mathf.Sin(beats * Mathf.PI) * rhythm, Vector3.forward);
        fieldOfView = pointFieldOfView - shot.beatZoom * pulse * rhythm;

        if (localTime >= transitionLength)
            return;

        float u = Mathf.Clamp01(localTime / transitionLength);

        switch (shot.transition)
        {
            case Transition.Latigazo:
                // Primera mitad: se va girando rápido desde la toma anterior.
                // Segunda: llega a la nueva todavía girando, desde el otro lado.
                if (u < 0.5f)
                {
                    float k = u * 2f;
                    position = fromPosition;
                    rotation = Quaternion.AngleAxis(whipSide * whipAngle * k * k, Vector3.up) * fromRotation;
                    fieldOfView = fromFieldOfView;
                }
                else
                {
                    float k = 1f - (u - 0.5f) * 2f;
                    rotation = Quaternion.AngleAxis(-whipSide * whipAngle * k * k, Vector3.up) * rotation;
                }

                break;

            case Transition.DollyZoom:
            {
                // Lo que mira la toma: queda del mismo tamaño en todo el viaje.
                Vector3 subject = pointPosition + pointRotation * Vector3.forward * dollyZoomSubjectDistance;
                float e = Ease(u);

                position = Vector3.Lerp(fromPosition, pointPosition, e) + Vector3.up * travelArc * Mathf.Sin(Mathf.PI * e);

                // En el primer cuarto apunta y cierra el zoom de golpe; después
                // se acerca abriéndolo.
                float aim = Mathf.SmoothStep(0f, 1f, u / 0.25f);
                Vector3 toSubject = subject - position;
                Quaternion look = toSubject.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(toSubject, Vector3.up) : pointRotation;
                rotation = Quaternion.Slerp(fromRotation, look, aim);

                float distance = Mathf.Max(0.05f, toSubject.magnitude);
                float dolly = 2f * Mathf.Atan(Mathf.Tan(pointFieldOfView * 0.5f * Mathf.Deg2Rad) * dollyZoomSubjectDistance / distance) * Mathf.Rad2Deg;
                fieldOfView = Mathf.Lerp(fromFieldOfView, dolly, aim);
                break;
            }

            case Transition.Suave:
            {
                float e = Ease(u);
                position = Vector3.Lerp(fromPosition, position, e);
                rotation = Quaternion.Slerp(fromRotation, rotation, e);
                fieldOfView = Mathf.Lerp(fromFieldOfView, fieldOfView, e);
                break;
            }
        }
    }

    private void BeginReturn()
    {
        if (phase != Phase.Show)
            return;

        phase = Phase.Return;
        returnElapsed = 0f;

        returnFromPosition = lastPosition;
        returnFromRotation = lastRotation;
        returnFromFieldOfView = lastFieldOfView;

        // Si la cámara quedó lejos (afuera, con los autos), corta a la primera
        // toma (la radio) y vuelve desde ahí: en línea recta atravesaría las paredes.
        if (HasShots() && shotIndex >= 0)
        {
            Transform first = shots[0] != null ? shots[0].cameraPoint : null;

            if (first != null && shots[shotIndex].cameraPoint != first)
            {
                returnFromPosition = first.position;
                returnFromRotation = first.rotation;
                returnFromFieldOfView = GetFieldOfView(first);
            }
        }
    }

    private void UpdateReturnCamera(float u)
    {
        float e = Ease(u);

        Vector3 position = Vector3.Lerp(returnFromPosition, restPosition, e) + Vector3.up * travelArc * Mathf.Sin(Mathf.PI * e);
        Quaternion rotation = Quaternion.Slerp(returnFromRotation, restRotation, e);
        float fieldOfView = Mathf.Lerp(returnFromFieldOfView, restFieldOfView, e);

        ApplyCamera(position, rotation, fieldOfView);
        viewCamera.lensShift = Vector2.Lerp(Vector2.zero, restLensShift, e);
    }

    private void ApplyCamera(Vector3 position, Quaternion rotation, float fieldOfView)
    {
        fieldOfView = Mathf.Clamp(fieldOfView, 1f, 170f);
        viewCamera.transform.SetPositionAndRotation(position, rotation);
        viewCamera.fieldOfView = fieldOfView;

        lastPosition = position;
        lastRotation = rotation;
        lastFieldOfView = fieldOfView;
    }

    private bool HasShots()
    {
        foreach (Shot shot in shots)
        {
            if (shot != null && shot.cameraPoint != null)
                return true;
        }

        return false;
    }

    private float GetFieldOfView(Transform point)
    {
        Camera marker = point.GetComponent<Camera>();
        return marker != null ? marker.fieldOfView : restFieldOfView;
    }

    // ----------------------------------------------------------------- Baile

    private void StartDancers()
    {
        dancing.Clear();

        foreach (Dancer dancer in dancers)
        {
            if (dancer == null || dancer.target == null || dancer.clip == null)
                continue;

            var state = new DancerState { config = dancer };
            Transform target = dancer.target;

            state.basePosition = target.localPosition;
            state.baseRotation = target.localRotation;
            state.baseScale = target.localScale;
            state.renderers = target.GetComponentsInChildren<Renderer>();
            state.baseBottom = GetBottom(state.renderers, target.position.y);

            state.animator = target.GetComponent<Animator>();

            if (state.animator == null)
            {
                state.animator = target.gameObject.AddComponent<Animator>();
                state.addedAnimator = true;
            }

            state.animator.applyRootMotion = false;
            state.animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // La animación se evalúa a mano (en LateUpdate), así se la puede
            // corregir en el mismo cuadro, antes de que se dibuje.
            state.graph = PlayableGraph.Create("Easter Egg - " + target.name);
            state.graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

            var output = AnimationPlayableOutput.Create(state.graph, "Baile", state.animator);
            state.playable = AnimationClipPlayable.Create(state.graph, dancer.clip);
            state.playable.SetApplyFootIK(false);
            output.SetSourcePlayable(state.playable);
            state.graph.Play();

            // Cómo deja el clip al objeto al principio.
            state.playable.SetTime(0.0);
            state.graph.Evaluate(0f);
            state.clipPosition = target.localPosition;
            state.clipRotation = target.localRotation;
            state.clipScale = target.localScale;

            RestorePose(state);
            dancing.Add(state);
        }
    }

    /// <summary>
    /// Aplica el clip y se queda solo con lo que cambia respecto de su
    /// principio, sobre la pose de la escena. weight = 0: quieto como estaba.
    /// </summary>
    private void UpdateDancers(float weight)
    {
        foreach (DancerState state in dancing)
        {
            Transform target = state.config.target;

            if (target == null || !state.graph.IsValid())
                continue;

            float loop = Mathf.Max(0.25f, state.config.beatsPerLoop) * Beat;
            float clipTime = Mathf.Repeat(danceTime, loop) / loop * state.config.clip.length;

            state.playable.SetTime(clipTime);
            state.graph.Evaluate(0f);

            Vector3 animatedPosition = target.localPosition;
            Quaternion animatedRotation = target.localRotation;
            Vector3 animatedScale = target.localScale;

            Vector3 scaleRatio = new Vector3(
                Ratio(animatedScale.x, state.clipScale.x),
                Ratio(animatedScale.y, state.clipScale.y),
                Ratio(animatedScale.z, state.clipScale.z));

            target.localPosition = state.basePosition + (animatedPosition - state.clipPosition) * weight;
            target.localRotation = state.baseRotation *
                Quaternion.Slerp(Quaternion.identity, Quaternion.Inverse(state.clipRotation) * animatedRotation, weight);
            target.localScale = Vector3.Scale(state.baseScale, Vector3.LerpUnclamped(Vector3.one, scaleRatio, weight));

            // Al aplastarse o estirarse desde su centro, se hundiría o flotaría:
            // se lo sube o se lo baja para que su parte más baja quede donde estaba.
            if (state.config.keepOnGround)
            {
                float bottom = GetBottom(state.renderers, target.position.y);
                target.position += Vector3.up * (state.baseBottom - bottom);
            }
        }
    }

    private void StopDancers()
    {
        foreach (DancerState state in dancing)
        {
            if (state.graph.IsValid())
                state.graph.Destroy();

            if (state.config.target != null)
                RestorePose(state);

            if (state.addedAnimator && state.animator != null)
                Destroy(state.animator);
        }

        dancing.Clear();
    }

    private static void RestorePose(DancerState state)
    {
        Transform target = state.config.target;
        target.localPosition = state.basePosition;
        target.localRotation = state.baseRotation;
        target.localScale = state.baseScale;
    }

    /// <summary>El kart rebota: se aplasta en cada pulso y vuelve.</summary>
    private void UpdateKart(float weight)
    {
        if (kart == null)
            return;

        float beats = danceTime / Beat;
        float pulse = Mathf.Exp(-(beats - Mathf.Floor(beats)) * 6f);
        float squash = kartBounce * (pulse - 0.3f) * weight;

        kart.localScale = Vector3.Scale(kartBaseScale, new Vector3(1f + squash * 0.5f, 1f - squash, 1f + squash * 0.5f));
    }

    private static float GetBottom(Renderer[] renderers, float fallback)
    {
        float bottom = float.MaxValue;

        foreach (Renderer renderer in renderers)
        {
            if (renderer != null && renderer.enabled)
                bottom = Mathf.Min(bottom, renderer.bounds.min.y);
        }

        return bottom < float.MaxValue ? bottom : fallback;
    }

    private static float Ratio(float value, float reference)
    {
        return Mathf.Abs(reference) > 1e-5f ? value / reference : 1f;
    }

    // ------------------------------------------------------------- Interfaz

    private void HideInterface()
    {
        interfaceStates.Clear();

        foreach (CanvasGroup group in hideDuringShow)
        {
            if (group == null)
            {
                interfaceStates.Add(default);
                continue;
            }

            interfaceStates.Add(new InterfaceState
            {
                alpha = group.alpha,
                interactable = group.interactable,
                blocksRaycasts = group.blocksRaycasts
            });

            group.interactable = false;
            group.blocksRaycasts = false;
        }
    }

    private void SetInterfaceAlpha(float visible)
    {
        for (int i = 0; i < hideDuringShow.Length && i < interfaceStates.Count; i++)
        {
            if (hideDuringShow[i] != null)
                hideDuringShow[i].alpha = interfaceStates[i].alpha * visible;
        }
    }

    private void RestoreInterface()
    {
        for (int i = 0; i < hideDuringShow.Length && i < interfaceStates.Count; i++)
        {
            CanvasGroup group = hideDuringShow[i];

            if (group == null)
                continue;

            group.alpha = interfaceStates[i].alpha;
            group.interactable = interfaceStates[i].interactable;
            group.blocksRaycasts = interfaceStates[i].blocksRaycasts;
        }

        interfaceStates.Clear();
    }

    // ----------------------------------------------------------------- Final

    /// <summary>Deja todo como estaba: la cámara en la estación, los objetos quietos, la interfaz y la música.</summary>
    private void Finish()
    {
        phase = Phase.Off;

        if (viewCamera != null)
        {
            viewCamera.transform.SetPositionAndRotation(restPosition, restRotation);
            viewCamera.fieldOfView = restFieldOfView;
            viewCamera.lensShift = restLensShift;
        }

        StopDancers();

        if (kart != null)
            kart.localScale = kartBaseScale;

        RestoreInterface();

        if (musicSource != null && musicSource.isPlaying)
            musicSource.Stop();

        if (workshop != null)
            workshop.CameraHeld = false;
    }

    // Arranca y frena suave.
    private static float Ease(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t * (t * (6f * t - 15f) + 10f);
    }
}
