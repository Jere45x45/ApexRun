using UnityEngine;

public class BotBehaviour : MonoBehaviour
{
    [Header("Wheel Points")]
    [SerializeField] private Transform frontLeftWheelPoint;
    [SerializeField] private Transform frontRightWheelPoint;
    [SerializeField] private Transform rearLeftWheelPoint;
    [SerializeField] private Transform rearRightWheelPoint;

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

        if (!ValidateWheelPoints())
            return;

        kart = new Kart(
            new RuntimeKartConfiguration(
                configuration
            )
        );

        kartPhysics = new KartPhysics(
            rb,
            frontLeftWheelPoint,
            frontRightWheelPoint,
            rearLeftWheelPoint,
            rearRightWheelPoint
        );

        engineController =
            new EngineController(
                kartPhysics
            );

        steeringController =
            new SteeringController(
                kartPhysics
            );

        brakeController =
            new BrakeController(
                kartPhysics
            );

        wheelVisualController =
            new WheelVisualController(
                kartPhysics,
                modelController.FrontLeftWheelSlot,
                modelController.FrontRightWheelSlot,
                modelController.RearLeftWheelSlot,
                modelController.RearRightWheelSlot
            );

        RefreshKart();
    }

    public void SetInputs(
        float throttle,
        float steering,
        bool brake)
    {
        this.throttle =
            Mathf.Clamp(throttle, -1f, 1f);

        this.steering =
            Mathf.Clamp(steering, -1f, 1f);

        this.braking = brake;
    }

    private void FixedUpdate()
    {
        if (kart == null ||
            kartPhysics == null)
        {
            return;
        }

        float deltaTime =
            Time.fixedDeltaTime;

        kartPhysics.UpdateWheels(
            deltaTime
        );

        kartPhysics.ApplySuspension(
            deltaTime
        );

        engineController.UpdateMotor(
            throttle,
            kart.Stats
        );

        steeringController.UpdateSteering(
            steering,
            rb.linearVelocity.magnitude,
            kart.Stats
        );

        brakeController.UpdateBrakes(
            braking,
            kart.Stats
        );

        wheelVisualController.UpdateVisuals();
    }

    public void RefreshKart()
    {
        if (kart == null)
            return;

        kart.Rebuild();

        if (kartPhysics != null)
        {
            PhysicsConfigurator.Configure(
                kartPhysics,
                kart.Stats
            );
        }

        UpdateVisualModel();
    }

    private void UpdateVisualModel()
    {
        if (modelController == null)
            return;

        if (kart == null)
            return;

        modelController.Refresh(
            kart.Configuration
        );
    }

    private bool ValidateWheelPoints()
    {
        bool valid = true;

        if (frontLeftWheelPoint == null)
        {
            Debug.LogError(
                "BotBehaviour no tiene WheelPoint-FL asignado.",
                this
            );

            valid = false;
        }

        if (frontRightWheelPoint == null)
        {
            Debug.LogError(
                "BotBehaviour no tiene WheelPoint-FR asignado.",
                this
            );

            valid = false;
        }

        if (rearLeftWheelPoint == null)
        {
            Debug.LogError(
                "BotBehaviour no tiene WheelPoint-RL asignado.",
                this
            );

            valid = false;
        }

        if (rearRightWheelPoint == null)
        {
            Debug.LogError(
                "BotBehaviour no tiene WheelPoint-RR asignado.",
                this
            );

            valid = false;
        }

        return valid;
    }
}