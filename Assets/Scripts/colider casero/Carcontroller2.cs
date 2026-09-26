using UnityEngine;
using UnityEngine.InputSystem;

// Poné este script en el empty "Kart" (el padre de todo) o en el "Chasis".
// Necesita tener (o encontrar en el padre/hijo) un Rigidbody.
[RequireComponent(typeof(Rigidbody))]
public class CarController2 : MonoBehaviour
{
    [Header("Wheel Colliders (arrastrá los 4 emptys con WheelCollider)")]
    [SerializeField] private WheelCollider frontLeftWheel;
    [SerializeField] private WheelCollider frontRightWheel;
    [SerializeField] private WheelCollider rearLeftWheel;
    [SerializeField] private WheelCollider rearRightWheel;

    [Header("Meshes de las ruedas (opcional, dejar vacío si no tenés)")]
    [SerializeField] private Transform frontLeftMesh;
    [SerializeField] private Transform frontRightMesh;
    [SerializeField] private Transform rearLeftMesh;
    [SerializeField] private Transform rearRightMesh;

    [Header("Configuración del auto")]
    [SerializeField] private float motorTorque = 1500f;
    [SerializeField] private float brakeTorque = 3000f;
    [SerializeField] private float maxSpeedKmh = 120f;

    [Header("Dirección: ángulo según velocidad")]
    [Tooltip("Ángulo máximo de dirección a baja velocidad (maniobrabilidad).")]
    [SerializeField] private float maxSteerAngle = 30f;

    [Tooltip("Ángulo máximo de dirección a alta velocidad (estabilidad).")]
    [SerializeField] private float minSteerAngle = 12f;

    [Tooltip("Velocidad (km/h) a partir de la cual el ángulo llega a su mínimo. Por debajo de esto, se interpola.")]
    [SerializeField] private float steeringReductionSpeed = 80f;

    [Header("Suavizado de dirección")]
    [Tooltip("Grados por segundo a los que el ángulo de dirección se mueve hacia el objetivo. Más bajo = más suave/lento.")]
    [SerializeField] private float steerSpeedDegPerSec = 240f;

    [Header("Estabilidad (opcional)")]
    [SerializeField] private Transform centerOfMass;

    private Rigidbody rb;

    // Acciones de input creadas por código (no requieren un Input Actions Asset)
    private InputAction moveAction;
    private InputAction brakeAction;

    private Vector2 moveInput;
    private bool isBraking;

    // Ángulo de dirección actualmente aplicado, se mueve gradualmente hacia el objetivo
    private float currentAppliedSteerAngle;

    private void Awake()
    {
        // Busca el Rigidbody en este objeto o en sus padres/hijos
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = GetComponentInParent<Rigidbody>();
        if (rb == null) rb = GetComponentInChildren<Rigidbody>();

        if (centerOfMass != null)
            rb.centerOfMass = centerOfMass.localPosition;

        // --- Input: movimiento (WASD / flechas / stick del gamepad) ---
        moveAction = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");
        moveAction.AddBinding("<Gamepad>/leftStick");

        // --- Input: freno de mano (Espacio / botón sur del gamepad) ---
        brakeAction = new InputAction("Brake", InputActionType.Button, "<Keyboard>/space");
        brakeAction.AddBinding("<Gamepad>/buttonSouth");
    }

    private void OnEnable()
    {
        moveAction.Enable();
        brakeAction.Enable();
    }

    private void OnDisable()
    {
        moveAction.Disable();
        brakeAction.Disable();
    }

    private void Update()
    {
        moveInput = moveAction.ReadValue<Vector2>();
        isBraking = brakeAction.IsPressed();

        // Sincroniza la posición visual de las ruedas con el WheelCollider (si asignaste meshes)
        UpdateWheelMesh(frontLeftWheel, frontLeftMesh);
        UpdateWheelMesh(frontRightWheel, frontRightMesh);
        UpdateWheelMesh(rearLeftWheel, rearLeftMesh);
        UpdateWheelMesh(rearRightWheel, rearRightMesh);
    }

    private void FixedUpdate()
    {
        float steerInput = moveInput.x;   // A/D o flechas izq/der
        float accelInput = moveInput.y;   // W/S o flechas arriba/abajo

        float speedKmh = rb.linearVelocity.magnitude * 3.6f;

        // --- Ángulo máximo de dirección disponible según la velocidad actual ---
        // A baja velocidad, se permite el ángulo completo (maxSteerAngle).
        // A medida que la velocidad se acerca a steeringReductionSpeed, el ángulo
        // disponible se reduce hacia minSteerAngle. Esto evita pedirle a la rueda
        // delantera más fuerza lateral de la que puede sostener sin saturarse
        // y perder agarre (derrape/understeer a alta velocidad).
        float speedFactor = Mathf.Clamp01(speedKmh / steeringReductionSpeed);
        float availableSteerAngle = Mathf.Lerp(maxSteerAngle, minSteerAngle, speedFactor);

        // --- Dirección (solo ruedas delanteras), suavizada ---
        float targetSteerAngle = availableSteerAngle * steerInput;

        currentAppliedSteerAngle = Mathf.MoveTowards(
            currentAppliedSteerAngle,
            targetSteerAngle,
            steerSpeedDegPerSec * Time.fixedDeltaTime
        );

        frontLeftWheel.steerAngle = currentAppliedSteerAngle;
        frontRightWheel.steerAngle = currentAppliedSteerAngle;

        // --- Aceleración (tracción en las 4 ruedas), con límite de velocidad ---
        // Nota: en Unity 6+ el Rigidbody usa "linearVelocity". Si usás una versión
        // anterior (2021/2022/2023), cambiá "rb.linearVelocity" por "rb.velocity".
        float currentMotorTorque = (speedKmh < maxSpeedKmh) ? motorTorque * accelInput : 0f;

        frontLeftWheel.motorTorque = currentMotorTorque;
        frontRightWheel.motorTorque = currentMotorTorque;
        rearLeftWheel.motorTorque = currentMotorTorque;
        rearRightWheel.motorTorque = currentMotorTorque;

        // --- Freno ---
        float currentBrakeTorque = isBraking ? brakeTorque : 0f;
        frontLeftWheel.brakeTorque = currentBrakeTorque;
        frontRightWheel.brakeTorque = currentBrakeTorque;
        rearLeftWheel.brakeTorque = currentBrakeTorque;
        rearRightWheel.brakeTorque = currentBrakeTorque;
    }

    private void UpdateWheelMesh(WheelCollider collider, Transform mesh)
    {
        if (mesh == null || collider == null) return;

        collider.GetWorldPose(out Vector3 position, out Quaternion rotation);
        mesh.position = position;
        mesh.rotation = rotation;
    }
}