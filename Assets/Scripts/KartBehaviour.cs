using UnityEngine;

public class KartBehaviour : MonoBehaviour
{
    [Header("Wheel Colliders")]
    [SerializeField] private WheelCollider frontLeftWheel;
    [SerializeField] private WheelCollider frontRightWheel;
    [SerializeField] private WheelCollider rearLeftWheel;
    [SerializeField] private WheelCollider rearRightWheel;

    [Header("Wheel Meshes (Empty Parents)")]
    [SerializeField] private Transform frontLeftMesh;
    [SerializeField] private Transform frontRightMesh;
    [SerializeField] private Transform rearLeftMesh;
    [SerializeField] private Transform rearRightMesh;

    [SerializeField] private KartModelController modelController;

    [Header("Vehicle Settings")]
    private Kart kart;
    [SerializeField] private KartConfigurationController configurationController;
    
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
    
        kart = new Kart(
            configurationController.Configuration
        );
    
        kartPhysics = new KartPhysics(
            rb,
            frontLeftWheel,
            frontRightWheel,
            rearLeftWheel,
            rearRightWheel,
            frontLeftMesh,
            frontRightMesh,
            rearLeftMesh,
            rearRightMesh
        );
    
        engineController = new EngineController(kartPhysics);
        steeringController = new SteeringController(kartPhysics);
        brakeController = new BrakeController(kartPhysics);
        wheelVisualController = new WheelVisualController(kartPhysics);
    
        RefreshKart();
    }

    public void SetInputs(float throttle, float steering, bool brake)
    {
    this.throttle = throttle;
    this.steering = steering;
    this.braking = brake;
    }

    private void FixedUpdate()
    {
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
        kart.Rebuild();

        PhysicsConfigurator.Configure(
            kartPhysics,
            kart.Stats
        );

        UpdateVisualModel();
    }

    public void Refresh(Kart kart)
    {
        this.kart = kart;

        RefreshKart();
    }

    private void UpdateVisualModel()
    {
        modelController.Refresh(kart.Configuration);
    }
}