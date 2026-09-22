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
    [SerializeField] private float maxSteerAngle = 30f;
    [SerializeField] private float maxSpeedKmh = 120f;

    [Header("Estabilidad (opcional)")]
    [SerializeField] private Transform centerOfMass;

    private Rigidbody rb;

    // Acciones de input creadas por código (no requieren un Input Actions Asset)
    private InputAction moveAction;
    private InputAction brakeAction;

    private Vector2 moveInput;
    private bool isBraking;

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

        // --- Dirección (solo ruedas delanteras) ---
        float currentSteerAngle = maxSteerAngle * steerInput;
        frontLeftWheel.steerAngle = currentSteerAngle;
        frontRightWheel.steerAngle = currentSteerAngle;

        // --- Aceleración (tracción en las 4 ruedas), con límite de velocidad ---
        // Nota: en Unity 6+ el Rigidbody usa "linearVelocity". Si usás una versión
        // anterior (2021/2022/2023), cambiá "rb.linearVelocity" por "rb.velocity".
        float speedKmh = rb.linearVelocity.magnitude * 3.6f;
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