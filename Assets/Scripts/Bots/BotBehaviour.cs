using UnityEngine;
using UnityEngine.Serialization;

public class BotBehaviour : MonoBehaviour
{
    // Los FormerlySerializedAs mantienen lo que ya estaba asignado en las
    // escenas de entrenamiento con los nombres viejos de los campos.
    [Header("Wheel Colliders")]
    [FormerlySerializedAs("frontLeftWheel")]
    [SerializeField] private WheelCollider frontLeftWheelCollider;
    [FormerlySerializedAs("frontRightWheel")]
    [SerializeField] private WheelCollider frontRightWheelCollider;
    [FormerlySerializedAs("rearLeftWheel")]
    [SerializeField] private WheelCollider rearLeftWheelCollider;
    [FormerlySerializedAs("rearRightWheel")]
    [SerializeField] private WheelCollider rearRightWheelCollider;

    [Header("Wheel Slots")]
    [Tooltip("Ruedas visuales. Se usan solo si no hay KartModelController (bots de entrenamiento).")]
    [FormerlySerializedAs("frontLeftMesh")]
    [SerializeField] private Transform frontLeftSlot;
    [FormerlySerializedAs("frontRightMesh")]
    [SerializeField] private Transform frontRightSlot;
    [FormerlySerializedAs("rearLeftMesh")]
    [SerializeField] private Transform rearLeftSlot;
    [FormerlySerializedAs("rearRightMesh")]
    [SerializeField] private Transform rearRightSlot;

    [Header("Model")]
    [Tooltip("Opcional: sin él, el bot anda igual y mueve las ruedas de Wheel Slots.")]
    [SerializeField] private KartModelController modelController;

    [Header("Configuration")]
    [SerializeField] private KartConfiguration configuration;

    private Kart kart;

    private float throttle;
    private float steering;
    private bool braking;

    private Rigidbody rb;

    private EngineController engineController;
    private SteeringController steeringController;
    private BrakeController brakeController;
    private WheelVisualController wheelVisualController;

    private KartPhysics kartPhysics;

    public Kart Kart => kart;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();

        if (rb == null)
        {
            Debug.LogError(
                "BotBehaviour necesita un Rigidbody en el mismo GameObject.",
                this
            );

            return;
        }

        if (configuration == null)
        {
            Debug.LogError(
                "BotBehaviour no tiene una KartConfiguration asignada.",
                this
            );

            return;
        }

        if (!ValidateWheelColliders())
            return;

        kart = new Kart(
            new RuntimeKartConfiguration(configuration)
        );

        kartPhysics = new KartPhysics(
            rb,
            frontLeftWheelCollider,
            frontRightWheelCollider,
            rearLeftWheelCollider,
            rearRightWheelCollider
        );

        engineController = new EngineController(kartPhysics);
        steeringController = new SteeringController(kartPhysics);
        brakeController = new BrakeController(kartPhysics);

        if (modelController != null)
        {
            wheelVisualController = new WheelVisualController(
                kartPhysics,
                modelController.FrontLeftWheelSlot,
                modelController.FrontRightWheelSlot,
                modelController.RearLeftWheelSlot,
                modelController.RearRightWheelSlot
            );
        }

        RefreshKart();
    }

    public void SetInputs(float throttle, float steering, bool brake)
    {
        this.throttle = Mathf.Clamp(throttle, -1f, 1f);
        this.steering = Mathf.Clamp(steering, -1f, 1f);
        this.braking = brake;
    }

    private void FixedUpdate()
    {
        if (kart == null || kartPhysics == null)
            return;

        float deltaTime = Time.fixedDeltaTime;

        kartPhysics.UpdateWheels(deltaTime);

        engineController.UpdateMotor(throttle, kart.Stats);

        steeringController.UpdateSteering(
            steering,
            rb.linearVelocity.magnitude,
            kart.Stats
        );

        brakeController.UpdateBrakes(braking, kart.Stats);

        if (wheelVisualController != null)
            wheelVisualController.UpdateVisuals();
        else
            UpdateWheelSlots();
    }

    public void RefreshKart()
    {
        if (kart == null)
            return;

        kart.Rebuild();

        if (kartPhysics != null)
        {
            PhysicsConfigurator.Configure(kartPhysics, kart.Stats);
        }

        UpdateVisualModel();
    }

    /// <summary>Le pone al bot piezas elegidas al azar (BotRandomizer).</summary>
    public void SetRandomConfiguration(EngineData engine, ChassisData chassis, WheelData wheels, AeroKitData aeroKit)
    {
        if (kart == null)
        {
            Debug.LogError("El Kart todavía no fue inicializado.", this);
            return;
        }

        kart.Configuration.InstallEngine(engine);
        kart.Configuration.InstallChassis(chassis);
        kart.Configuration.InstallWheels(wheels);
        kart.Configuration.InstallAeroKit(aeroKit);

        RefreshKart();
    }

    private void UpdateVisualModel()
    {
        if (modelController == null)
            return;

        if (kart == null)
            return;

        modelController.Refresh(kart.Configuration);
    }

    /// <summary>Sin KartModelController: mueve las ruedas asignadas en Wheel Slots.</summary>
    private void UpdateWheelSlots()
    {
        UpdateWheelSlot(kartPhysics.FrontLeftWheel, frontLeftSlot);
        UpdateWheelSlot(kartPhysics.FrontRightWheel, frontRightSlot);
        UpdateWheelSlot(kartPhysics.RearLeftWheel, rearLeftSlot);
        UpdateWheelSlot(kartPhysics.RearRightWheel, rearRightSlot);
    }

    // Como antes de la física nueva: la rueda toma la pose de su WheelCollider tal cual.
    private static void UpdateWheelSlot(WheelPhysics wheel, Transform slot)
    {
        if (wheel == null || slot == null)
            return;

        wheel.GetVisualPose(
            out Vector3 position,
            out Quaternion rotation
        );

        slot.SetPositionAndRotation(position, rotation);
    }

    private bool ValidateWheelColliders()
    {
        bool valid = true;

        if (frontLeftWheelCollider == null)
        {
            Debug.LogError(
                "BotBehaviour no tiene WheelCollider-FL asignado.",
                this
            );

            valid = false;
        }

        if (frontRightWheelCollider == null)
        {
            Debug.LogError(
                "BotBehaviour no tiene WheelCollider-FR asignado.",
                this
            );

            valid = false;
        }

        if (rearLeftWheelCollider == null)
        {
            Debug.LogError(
                "BotBehaviour no tiene WheelCollider-RL asignado.",
                this
            );

            valid = false;
        }

        if (rearRightWheelCollider == null)
        {
            Debug.LogError(
                "BotBehaviour no tiene WheelCollider-RR asignado.",
                this
            );

            valid = false;
        }

        return valid;
    }
}
