using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Cámara del kart con varias perspectivas, como en BeamNG.drive:
/// - Teclas 1 a 9 (fila de números o teclado numérico): elige la cámara por su
///   número. Apretar otra vez el número de la cámara activa pasa a su
///   siguiente variante, si tiene.
/// - C (o Select/View del joystick): pasa a la cámara siguiente.
/// - Botón derecho del mouse + mover, o stick derecho: mirar alrededor (en las
///   cámaras que lo permiten). Rueda del mouse: acercar/alejar.
///
/// Cada perspectiva es un componente KartCameraMode en este mismo objeto; el
/// orden de la lista "modes" es el de las teclas. Este objeto se mueve a la
/// pose que pide la cámara activa: la Camera tiene que estar en él o ser un
/// hijo en (0, 0, 0) sin rotación.
/// </summary>
[DefaultExecutionOrder(1000)]
public class KartCameraController : MonoBehaviour
{
    private const int MaxModes = 9;

    // Si el kart se mueve más que esto entre dos frames, se lo teletransportó (respawn).
    private const float TeleportDistance = 15f;

    // Por debajo de esto, el stick derecho se considera suelto.
    private const float StickDeadZone = 0.15f;

    [Header("Kart")]
    [SerializeField] private Rigidbody target;

    [Header("Cámara")]
    [Tooltip("Si queda vacío, se usa la Camera de este objeto o de sus hijos.")]
    [SerializeField] private Camera controlledCamera;

    [Tooltip("Perspectivas en el orden de las teclas: la primera es el 1, la segunda el 2, etc. (hasta 9).")]
    [SerializeField] private KartCameraMode[] modes = new KartCameraMode[0];

    [Tooltip("Índice (desde 0) de la cámara con la que arranca. 0 = la tecla 1.")]
    [SerializeField, Min(0)] private int initialMode = 0;

    [Header("Mandos")]
    [Tooltip("Grados por cada píxel que se mueve el mouse (con el botón derecho apretado).")]
    [SerializeField, Min(0f)] private float mouseSensitivity = 0.15f;

    [Tooltip("Grados por segundo con el stick derecho a fondo.")]
    [SerializeField, Min(0f)] private float stickSensitivity = 180f;

    [Header("Aceleración (movimiento de cabeza)")]
    [Tooltip("Suavizado de la aceleración del kart (s). Saca el ruido de los neumáticos.")]
    [SerializeField, Min(0.001f)] private float accelerationSmoothTime = 0.08f;

    [Header("Aviso en pantalla")]
    [SerializeField] private bool showModeName = true;

    [SerializeField, Min(0f)] private float modeNameDuration = 1.5f;

    private readonly KartCameraContext context = new KartCameraContext();

    private int currentIndex = -1;
    private Vector3 lastTargetPosition;
    private bool hasLastTargetPosition;

    private Vector3 previousVelocity;
    private bool hasPreviousVelocity;
    private Vector3 smoothedAcceleration;

    private float modeNameTimer;
    private GUIStyle modeNameStyle;

    private InputAction[] selectActions;
    private InputAction cycleAction;
    private InputAction mouseLookAction;
    private InputAction mouseLookHoldAction;
    private InputAction stickLookAction;
    private InputAction zoomAction;

    /// <summary>La cámara activa (null si no hay ninguna).</summary>
    public KartCameraMode CurrentMode =>
        currentIndex >= 0 && currentIndex < modes.Length ? modes[currentIndex] : null;

    private void Awake()
    {
        if (controlledCamera == null)
        {
            controlledCamera = GetComponentInChildren<Camera>();
        }

        CreateInputActions();
    }

    private void OnEnable()
    {
        SetInputActionsEnabled(true);
    }

    private void OnDisable()
    {
        SetInputActionsEnabled(false);
    }

    private void OnDestroy()
    {
        if (selectActions != null)
        {
            for (int i = 0; i < selectActions.Length; i++)
            {
                selectActions[i].Dispose();
            }
        }

        cycleAction?.Dispose();
        mouseLookAction?.Dispose();
        mouseLookHoldAction?.Dispose();
        stickLookAction?.Dispose();
        zoomAction?.Dispose();
    }

    private void Start()
    {
        if (controlledCamera == null)
        {
            Debug.LogError("KartCameraController no encontró una Camera.", this);
        }

        // Sin kart todavía es normal: en la carrera en red lo asigna
        // LocalPlayerKartBinder (SetTarget) cuando aparece el kart del jugador.

        SelectMode(Mathf.Clamp(initialMode, 0, Mathf.Max(0, modes.Length - 1)));
    }

    /// <summary>Cambia el kart que sigue la cámara (por ejemplo, al spawnear el del jugador).</summary>
    public void SetTarget(Rigidbody newTarget)
    {
        target = newTarget;
        hasLastTargetPosition = false;
        hasPreviousVelocity = false;
        smoothedAcceleration = Vector3.zero;

        if (CurrentMode != null && target != null)
        {
            FillTargetContext();
            CurrentMode.Activate(context);
        }
    }

    /// <summary>
    /// Elige la cámara por índice (0 = tecla 1). Si ya es la activa, pasa a su
    /// siguiente variante.
    /// </summary>
    public void SelectMode(int index)
    {
        if (index < 0 || index >= modes.Length || modes[index] == null)
            return;

        if (target != null)
        {
            FillTargetContext();
        }

        if (index == currentIndex)
        {
            if (target != null)
            {
                CurrentMode.NextVariant(context);
            }
        }
        else
        {
            currentIndex = index;

            if (target != null)
            {
                CurrentMode.Activate(context);
            }
        }

        modeNameTimer = modeNameDuration;
    }

    private void FixedUpdate()
    {
        if (target == null)
            return;

        // Aceleración del kart en sus ejes, suavizada: la usa, por ejemplo, la
        // cabeza del piloto. Δv / Δt entre pasos de física.
        Vector3 velocity = target.linearVelocity;

        if (hasPreviousVelocity)
        {
            Vector3 acceleration = (velocity - previousVelocity) / Time.fixedDeltaTime;
            Vector3 localAcceleration = Quaternion.Inverse(target.rotation) * acceleration;

            smoothedAcceleration = Vector3.Lerp(
                smoothedAcceleration,
                localAcceleration,
                1f - Mathf.Exp(-Time.fixedDeltaTime / accelerationSmoothTime)
            );
        }

        previousVelocity = velocity;
        hasPreviousVelocity = true;
    }

    private void Update()
    {
        ReadModeInput();
        ReadLookInput();

        if (modeNameTimer > 0f)
        {
            modeNameTimer -= Time.unscaledDeltaTime;
        }
    }

    private void LateUpdate()
    {
        KartCameraMode mode = CurrentMode;

        if (target == null || mode == null)
            return;

        FillTargetContext();

        // Si el kart saltó de lugar (respawn), la cámara va directo, sin barrer la pista.
        Vector3 targetPosition = target.transform.position;

        if (hasLastTargetPosition &&
            (targetPosition - lastTargetPosition).sqrMagnitude > TeleportDistance * TeleportDistance)
        {
            hasPreviousVelocity = false;
            smoothedAcceleration = Vector3.zero;
            context.LocalAcceleration = Vector3.zero;
            mode.Activate(context);
        }

        lastTargetPosition = targetPosition;
        hasLastTargetPosition = true;

        mode.UpdateCamera(context, Time.deltaTime, out Vector3 position, out Quaternion rotation);

        transform.SetPositionAndRotation(position, rotation);

        if (controlledCamera != null)
        {
            controlledCamera.fieldOfView = mode.FieldOfView;
            controlledCamera.nearClipPlane = mode.NearClipPlane;
        }

        // Los mandos de mirar se consumen una vez por frame.
        context.LookDelta = Vector2.zero;
        context.Zoom = 0f;
    }

    private void OnGUI()
    {
        KartCameraMode mode = CurrentMode;

        if (!showModeName || modeNameTimer <= 0f || mode == null)
            return;

        if (modeNameStyle == null)
        {
            modeNameStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 22,
                alignment = TextAnchor.MiddleCenter
            };
        }

        string text = (currentIndex + 1) + " · " + mode.DisplayName;
        Vector2 size = modeNameStyle.CalcSize(new GUIContent(text)) + new Vector2(24f, 12f);
        Rect rect = new Rect((Screen.width - size.x) * 0.5f, 24f, size.x, size.y);

        Color previousColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(modeNameTimer / 0.3f));
        GUI.Box(rect, text, modeNameStyle);
        GUI.color = previousColor;
    }

    private void FillTargetContext()
    {
        context.Target = target.transform;
        context.Body = target;
        context.LocalAcceleration = smoothedAcceleration;
    }

    private void ReadModeInput()
    {
        for (int i = 0; i < selectActions.Length; i++)
        {
            if (selectActions[i].WasPressedThisFrame())
            {
                SelectMode(i);
                return;
            }
        }

        if (cycleAction.WasPressedThisFrame() && modes.Length > 0)
        {
            int next = currentIndex;

            // Salta los lugares vacíos de la lista.
            for (int step = 0; step < modes.Length; step++)
            {
                next = (next + 1) % modes.Length;

                if (modes[next] != null)
                    break;
            }

            SelectMode(next);
        }
    }

    private void ReadLookInput()
    {
        Vector2 look = Vector2.zero;
        bool looking = false;

        if (mouseLookHoldAction.IsPressed())
        {
            look += mouseLookAction.ReadValue<Vector2>() * mouseSensitivity;
            looking = true;
        }

        Vector2 stick = stickLookAction.ReadValue<Vector2>();

        if (stick.magnitude > StickDeadZone)
        {
            look += stick * stickSensitivity * Time.unscaledDeltaTime;
            looking = true;
        }

        context.LookDelta += look;
        context.IsLooking = looking;

        float scroll = zoomAction.ReadValue<float>();

        if (Mathf.Abs(scroll) > 0.01f)
        {
            context.Zoom += Mathf.Sign(scroll);
        }
    }

    private void CreateInputActions()
    {
        selectActions = new InputAction[MaxModes];

        for (int i = 0; i < MaxModes; i++)
        {
            int number = i + 1;

            selectActions[i] = new InputAction("CameraSelect" + number, InputActionType.Button);
            selectActions[i].AddBinding("<Keyboard>/" + number);
            selectActions[i].AddBinding("<Keyboard>/numpad" + number);
        }

        cycleAction = new InputAction("CameraCycle", InputActionType.Button);
        cycleAction.AddBinding("<Keyboard>/c");
        cycleAction.AddBinding("<Gamepad>/select");

        mouseLookAction = new InputAction("CameraMouseLook", InputActionType.Value, expectedControlType: "Vector2");
        mouseLookAction.AddBinding("<Mouse>/delta");

        mouseLookHoldAction = new InputAction("CameraMouseLookHold", InputActionType.Button);
        mouseLookHoldAction.AddBinding("<Mouse>/rightButton");

        stickLookAction = new InputAction("CameraStickLook", InputActionType.Value, expectedControlType: "Vector2");
        stickLookAction.AddBinding("<Gamepad>/rightStick");

        zoomAction = new InputAction("CameraZoom", InputActionType.Value, expectedControlType: "Axis");
        zoomAction.AddBinding("<Mouse>/scroll/y");
    }

    private void SetInputActionsEnabled(bool enabled)
    {
        if (selectActions == null)
            return;

        for (int i = 0; i < selectActions.Length; i++)
        {
            if (enabled) selectActions[i].Enable();
            else selectActions[i].Disable();
        }

        InputAction[] others = { cycleAction, mouseLookAction, mouseLookHoldAction, stickLookAction, zoomAction };

        for (int i = 0; i < others.Length; i++)
        {
            if (enabled) others[i].Enable();
            else others[i].Disable();
        }
    }
}
