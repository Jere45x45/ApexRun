using UnityEngine;

public class BotBehaviour : MonoBehaviour
{
    [Header("Wheel Colliders")]
    [SerializeField] private WheelCollider frontLeftWheelCollider;
    [SerializeField] private WheelCollider frontRightWheelCollider;
    [SerializeField] private WheelCollider rearLeftWheelCollider;
    [SerializeField] private WheelCollider rearRightWheelCollider;

    [Header("Wheel Slots")]
    [SerializeField] private Transform frontLeftSlot;
    [SerializeField] private Transform frontRightSlot;
    [SerializeField] private Transform rearLeftSlot;
    [SerializeField] private Transform rearRightSlot;

    [Header("Model")]
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

        if (modelController == null)
        {
            Debug.LogError(
                "BotBehaviour no tiene un KartModelController asignado.",
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

        wheelVisualController = new WheelVisualController(
            kartPhysics,
            modelController.FrontLeftWheelSlot,
            modelController.FrontRightWheelSlot,
            modelController.RearLeftWheelSlot,
            modelController.RearRightWheelSlot
        );

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

        wheelVisualController.UpdateVisuals();
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

    private void UpdateVisualModel()
    {
        if (modelController == null)
            return;

        if (kart == null)
            return;

        modelController.Refresh(kart.Configuration);
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