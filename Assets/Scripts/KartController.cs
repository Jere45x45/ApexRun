using UnityEngine;

public class KartController : MonoBehaviour
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

    [Header("Vehicle Settings")]
    private Kart kart;
    [SerializeField] private KartConfiguration configuration;
    
    private float throttle;
    private float steering;

    private Rigidbody rb;

    private bool braking;
    
    private EngineController engineController;
    private SteeringController steeringController;
    private BrakeController brakeController;
    private WheelVisualController wheelVisualController;

    private KartPhysics kartPhysics;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();

        kart = new Kart(configuration);

        KartPhysics kartPhysics = new KartPhysics(
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

        PhysicsConfigurator.Configure(kartPhysics, kart.Stats);
    }

    private void Update()
    {
        throttle = Input.GetAxis("Vertical");
        steering = Input.GetAxis("Horizontal");

        braking = Input.GetKey(KeyCode.Space);
    }

    private void FixedUpdate()
    {
        engineController.UpdateMotor(
            throttle,
            kart.Stats
        );

        steeringController.UpdateSteering(
            steering,
            rb.velocity.magnitude,
            kart.Stats
        );

        brakeController.UpdateBrakes(
            braking,
            kart.Stats
        );

        wheelVisualController.UpdateVisuals();
    }
}