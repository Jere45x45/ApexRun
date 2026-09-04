using UnityEngine;

public class KartBehaviour : MonoBehaviour
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
    [SerializeField] private KartConfigurationController configurationController;

    private Kart kart;

    private float throttle;
    private float steering;
    private bool braking;

    private Rigidbody rb;

    private EngineController engineController;
    private SteeringController steeringController;
    private BrakeController brakeController;
    private WheelVisualController wheelVisualController;
    private AeroController aeroController;
    private KartFrictionController frictionController;

    private KartPhysics kartPhysics;

    public Kart Kart => kart;

    private void OnEnable()
    {
        if (configurationController != null)
        {
            configurationController.ConfigurationChanged +=
                RefreshKart;
        }
    }

    private void OnDisable()
    {
        if (configurationController != null)
        {
            configurationController.ConfigurationChanged -=
                RefreshKart;
        }
    }

    private void Start()
    {
        rb = GetComponent<Rigidbody>();

        if (rb == null)
        {
            Debug.LogError(
                "KartBehaviour necesita un Rigidbody en el mismo GameObject.",
                this
            );

            return;
        }

        if (configurationController == null)
        {
            Debug.LogError(
                "KartBehaviour no tiene un KartConfigurationController asignado.",
                this
            );

            return;
        }

        if (configurationController.Configuration == null)
        {
            Debug.LogError(
                "El KartConfigurationController no tiene una configuración runtime válida.",
                this
            );

            return;
        }

        if (modelController == null)
        {
            Debug.LogError(
                "KartBehaviour no tiene un KartModelController asignado.",
                this
            );

            return;
        }

        if (!ValidateWheelPoints())
            return;

        kart = new Kart(
            configurationController.Configuration
        );

        kartPhysics = new KartPhysics(
            rb,
            frontLeftWheelPoint,
            frontRightWheelPoint,
            rearLeftWheelPoint,
            rearRightWheelPoint
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

        aeroController =
            new AeroController(kartPhysics);

        frictionController =
            new KartFrictionController(kartPhysics);

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

        frictionController.UpdateFriction();

        aeroController.UpdateAerodynamics(
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

    public void Refresh(Kart newKart)
    {
        if (newKart == null)
        {
            Debug.LogWarning(
                "Se intentó asignar un Kart nulo.",
                this
            );

            return;
        }

        kart = newKart;

        RefreshKart();
    }

    private void UpdateVisualModel()
    {
        if (modelController == null)
        {
            Debug.LogWarning(
                "KartBehaviour no tiene un KartModelController asignado.",
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

    private bool ValidateWheelPoints()
    {
        bool valid = true;

        if (frontLeftWheelPoint == null)
        {
            Debug.LogError(
                "No hay WheelPoint-FL asignado.",
                this
            );

            valid = false;
        }

        if (frontRightWheelPoint == null)
        {
            Debug.LogError(
                "No hay WheelPoint-FR asignado.",
                this
            );

            valid = false;
        }

        if (rearLeftWheelPoint == null)
        {
            Debug.LogError(
                "No hay WheelPoint-RL asignado.",
                this
            );

            valid = false;
        }

        if (rearRightWheelPoint == null)
        {
            Debug.LogError(
                "No hay WheelPoint-RR asignado.",
                this
            );

            valid = false;
        }

        return valid;
    }
}