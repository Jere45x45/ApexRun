using UnityEngine;

public class BotBehaviour : MonoBehaviour
{
    [Header("Wheel Colliders")]
    [SerializeField] private WheelCollider frontLeftWheel;
    [SerializeField] private WheelCollider frontRightWheel;
    [SerializeField] private WheelCollider rearLeftWheel;
    [SerializeField] private WheelCollider rearRightWheel;

    [Header("Wheel Slots")]
    [SerializeField] private Transform frontLeftSlot;
    [SerializeField] private Transform frontRightSlot;
    [SerializeField] private Transform rearLeftSlot;
    [SerializeField] private Transform rearRightSlot;

    [Header("Model")]
    [SerializeField] private KartModelController modelController;

    [Header("Vehicle Settings")]
    [SerializeField] private KartConfiguration configuration;

    private Kart kart;

    private float throttle;
    private float steering;

    private Rigidbody rb;

    private bool braking;

    private EngineController engineController;
    private SteeringController steeringController;
    private BrakeController brakeController;
    private WheelVisualController wheelVisualController;

    private KartPhysics kartPhysics;

    public Kart Kart => kart;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();

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

        kart = new Kart(
            new RuntimeKartConfiguration(configuration)
        );

        kartPhysics = new KartPhysics(
            rb,
            frontLeftWheel,
            frontRightWheel,
            rearLeftWheel,
            rearRightWheel,
            frontLeftSlot,
            frontRightSlot,
            rearLeftSlot,
            rearRightSlot
        );

        engineController =
            new EngineController(kartPhysics);

        steeringController =
            new SteeringController(kartPhysics);

        brakeController =
            new BrakeController(kartPhysics);

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
        this.throttle = throttle;
        this.steering = steering;
        this.braking = brake;
    }

    private void FixedUpdate()
    {
        if (kart == null)
            return;

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

    public void Refresh(Kart kart)
    {
        if (kart == null)
        {
            Debug.LogWarning(
                "Se intentó asignar un Kart nulo.",
                this
            );

            return;
        }

        this.kart = kart;

        RefreshKart();
    }

    private void UpdateVisualModel()
    {
        if (modelController == null)
        {
            Debug.LogWarning(
                "BotBehaviour no tiene un KartModelController asignado.",
                this
            );

            return;
        }

        if (kart == null)
            return;

        modelController.Refresh(
            kart.Configuration
        );
    }
}