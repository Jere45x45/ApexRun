using UnityEngine;

public class KartBehaviour : MonoBehaviour
{
    [Header("Wheel Colliders")]
    [SerializeField] private WheelCollider frontLeftWheelCollider;
    [SerializeField] private WheelCollider frontRightWheelCollider;
    [SerializeField] private WheelCollider rearLeftWheelCollider;
    [SerializeField] private WheelCollider rearRightWheelCollider;

    [Header("Model")]
    [SerializeField] private KartModelController modelController;

    [Header("Configuration")]
    [SerializeField] private KartConfigurationController configurationController;

    private Kart kart;

    private float throttle;
    private float steering;
    private bool braking;

    private bool inputEnabled;

    private Rigidbody rb;

    private EngineController engineController;
    private SteeringController steeringController;
    private BrakeController brakeController;
    private WheelVisualController wheelVisualController;
    private AeroController aeroController;
    private KartFrictionController frictionController;

    private KartPhysics kartPhysics;

    public Kart Kart => kart;

    public KartPhysics Physics => kartPhysics;

    public bool InputEnabled => inputEnabled;

    private void OnEnable()
    {
        if (configurationController != null)
        {
            configurationController.ConfigurationChanged += RefreshKart;
        }
    }

    private void OnDisable()
    {
        if (configurationController != null)
        {
            configurationController.ConfigurationChanged -= RefreshKart;
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

        if (!ValidateWheelColliders())
            return;

        kart = new Kart(configurationController.Configuration);

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

        aeroController = new AeroController(kartPhysics);
        frictionController = new KartFrictionController(kartPhysics);

        RefreshKart();

        SetInputEnabled(false);
    }

    public void SetInputs(float throttle, float steering, bool brake)
    {
        if (!inputEnabled)
        {
            ClearInputs();
            return;
        }

        this.throttle = Mathf.Clamp(throttle, -1f, 1f);
        this.steering = Mathf.Clamp(steering, -1f, 1f);
        this.braking = brake;
    }

    public void SetInputEnabled(bool enabled)
    {
        inputEnabled = enabled;

        if (!enabled)
        {
            ClearInputs();
        }
    }

    private void ClearInputs()
    {
        throttle = 0f;
        steering = 0f;
        braking = false;
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

        frictionController.UpdateFriction();

        aeroController.UpdateAerodynamics(kart.Stats);

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

    public void Refresh(Kart newKart)
    {
        if (newKart == null)
        {
            Debug.LogWarning("Se intentó asignar un Kart nulo.", this);
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

        modelController.Refresh(kart.Configuration);
    }

    private bool ValidateWheelColliders()
    {
        bool valid = true;

        if (frontLeftWheelCollider == null)
        {
            Debug.LogError("No hay WheelCollider-FL asignado.", this);
            valid = false;
        }

        if (frontRightWheelCollider == null)
        {
            Debug.LogError("No hay WheelCollider-FR asignado.", this);
            valid = false;
        }

        if (rearLeftWheelCollider == null)
        {
            Debug.LogError("No hay WheelCollider-RL asignado.", this);
            valid = false;
        }

        if (rearRightWheelCollider == null)
        {
            Debug.LogError("No hay WheelCollider-RR asignado.", this);
            valid = false;
        }

        return valid;
    }
}