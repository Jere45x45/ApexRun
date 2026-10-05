using UnityEngine;

/// <summary>
/// Kart del juego. Recibe los mandos del jugador o de la IA (SetInputs), arma
/// el kart con sus piezas (KartConfigurationController → Kart → KartStats) y
/// lo mueve con la física nueva (KartVehicle).
///
/// Sin colliders de ruedas asignados, solo muestra el modelo: es lo que usa,
/// por ejemplo, el kart de exhibición del catálogo.
/// </summary>
public class KartBehaviour : MonoBehaviour
{
    // Por debajo de esta velocidad, sin mandos habilitados, el kart queda frenado.
    private const float StandstillSpeed = 1f;

    [Header("Ruedas")]
    [Tooltip("Un SphereCollider trigger por rueda, en el centro de cada rueda. Orden: FL, FR, RL, RR. " +
             "Si queda vacío, el kart solo muestra el modelo (sin física).")]
    [SerializeField] private SphereCollider[] wheelColliders = new SphereCollider[0];

    [Header("Model")]
    [SerializeField] private KartModelController modelController;

    [Header("Configuration")]
    [SerializeField] private KartConfigurationController configurationController;

    [Header("Simulación")]
    [Tooltip("Subpasos del neumático por cada paso de física. La goma es muy rígida " +
             "comparada con la inercia de una rueda y necesita pasos más cortos.")]
    [SerializeField, Range(1, 50)] private int tireSubsteps = 10;

    [Header("Ayudas de manejo")]
    [Tooltip("Qué tan rápido giran las ruedas hacia el ángulo pedido (°/s). " +
             "Con teclado, A/D es todo o nada: así el volante no salta de golpe.")]
    [SerializeField, Min(1f)] private float steeringSpeed = 120f;

    [Tooltip("Limita el volante al ángulo que da el máximo agarre a la velocidad actual. " +
             "Girar más que eso no dobla más: solo arrastra las ruedas.")]
    [SerializeField] private bool limitSteeringToGrip = true;

    [Tooltip("Margen sobre el ángulo de deslizamiento de máximo agarre. 1 = justo en el pico.")]
    [SerializeField, Range(0.5f, 2f)] private float gripSteeringMargin = 1f;

    private Kart kart;

    private float throttle;
    private float steering;
    private bool braking;

    private bool inputEnabled;

    private Rigidbody rb;

    private KartVehicle vehicle;
    private KartWheelVisual[] wheelVisuals;
    private float currentSteerAngle;

    public Kart Kart => kart;

    /// <summary>La física del kart. Null si el kart solo muestra el modelo.</summary>
    public KartVehicle Vehicle => vehicle;

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

        kart = new Kart(configurationController.Configuration);

        if (HasWheelColliders())
        {
            CreateVehicle();
        }

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
        if (kart == null || vehicle == null)
            return;

        float deltaTime = Time.fixedDeltaTime;

        ApplyInputs(deltaTime);

        vehicle.Step(deltaTime);
    }

    private void LateUpdate()
    {
        if (vehicle == null || modelController == null)
            return;

        // Ruedas visuales: con la pose interpolada del Transform, una vez por frame.
        for (int i = 0; i < wheelVisuals.Length; i++)
        {
            wheelVisuals[i].UpdatePose(transform, GetWheelModel(i), Time.deltaTime);
        }
    }

    /// <summary>
    /// Pasa los mandos a la física:
    /// - Acelerador positivo = motor. Negativo (tecla S) = freno: un kart no tiene marcha atrás.
    /// - Volante: gira de a poco y, con la ayuda activada, no más allá del máximo agarre.
    /// - Sin mandos habilitados (cuenta regresiva, carrera terminada) el kart
    ///   sigue rodando suelto y, ya casi quieto, queda frenado.
    /// </summary>
    private void ApplyInputs(float deltaTime)
    {
        float speed = Mathf.Abs(vehicle.ForwardSpeed);

        if (inputEnabled)
        {
            float brake = braking ? 1f : 0f;

            vehicle.Throttle = Mathf.Clamp01(throttle);
            vehicle.Brake = Mathf.Max(brake, Mathf.Clamp01(-throttle));
        }
        else
        {
            vehicle.Throttle = 0f;
            vehicle.Brake = speed < StandstillSpeed ? 1f : 0f;
        }

        float targetAngle = steering * GetMaxSteerAngle(speed);

        currentSteerAngle = Mathf.MoveTowards(
            currentSteerAngle,
            targetAngle,
            steeringSpeed * deltaTime
        );

        vehicle.SteerAngle = currentSteerAngle;
    }

    /// <summary>
    /// Ángulo máximo de volante para la velocidad actual. Con la ayuda activada es
    /// el ángulo geométrico de la curva más cerrada que permite el agarre
    /// (radio = v² / (μ·g)) más el ángulo de deslizamiento de máximo agarre del
    /// neumático. A baja velocidad manda el tope del volante.
    /// </summary>
    private float GetMaxSteerAngle(float speed)
    {
        float lockAngle = vehicle.Chassis.steering.maxSteerAngle;

        if (!limitSteeringToGrip)
            return lockAngle;

        float speedSquared = speed * speed;

        if (speedSquared < 0.01f)
            return lockAngle;

        TireSettings tire = vehicle.Tire;

        float geometricAngle = Mathf.Atan(
            vehicle.Steering.Wheelbase * tire.lateralFriction * Physics.gravity.magnitude / speedSquared
        ) * Mathf.Rad2Deg;

        float slipAngle = tire.LateralPeakSlipAngle * Mathf.Rad2Deg * gripSteeringMargin;

        return Mathf.Min(lockAngle, geometricAngle + slipAngle);
    }

    public void RefreshKart()
    {
        if (kart == null)
            return;

        kart.Rebuild();

        if (vehicle != null)
        {
            KartStats stats = kart.Stats;

            vehicle.Configure(
                stats.chassis,
                stats.tire,
                stats.wheelInertia,
                stats.engine,
                stats.aero,
                tireSubsteps
            );
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

    /// <summary>
    /// Pone la física de acuerdo con la velocidad actual del Rigidbody.
    /// Llamarlo después de mover el kart a mano (por ejemplo, en un respawn).
    /// </summary>
    public void ResetMotion()
    {
        if (vehicle == null)
            return;

        vehicle.ResetMotion();
        currentSteerAngle = 0f;
    }

    private void CreateVehicle()
    {
        vehicle = new KartVehicle(rb, wheelColliders);
        wheelVisuals = new KartWheelVisual[vehicle.Wheels.Length];

        for (int i = 0; i < vehicle.Wheels.Length; i++)
        {
            KartWheel wheel = vehicle.Wheels[i];
            wheelVisuals[i] = new KartWheelVisual(wheel, wheel.LocalPosition.x < 0f);
        }
    }

    private Transform GetWheelModel(int index)
    {
        ModelSlot slot = null;

        switch (index)
        {
            case KartVehicle.FrontLeft:
                slot = modelController.FrontLeftWheelSlot;
                break;

            case KartVehicle.FrontRight:
                slot = modelController.FrontRightWheelSlot;
                break;

            case KartVehicle.RearLeft:
                slot = modelController.RearLeftWheelSlot;
                break;

            case KartVehicle.RearRight:
                slot = modelController.RearRightWheelSlot;
                break;
        }

        if (slot == null || slot.CurrentInstance == null)
            return null;

        return slot.CurrentInstance.transform;
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

    /// <summary>
    /// True si hay cuatro colliders de rueda. Sin ninguno, el kart solo muestra
    /// el modelo; si faltan algunos, es un error de armado.
    /// </summary>
    private bool HasWheelColliders()
    {
        if (wheelColliders == null || wheelColliders.Length == 0)
            return false;

        bool valid = wheelColliders.Length == 4;

        for (int i = 0; i < wheelColliders.Length; i++)
        {
            if (wheelColliders[i] == null)
            {
                valid = false;
            }
        }

        if (!valid)
        {
            Debug.LogError(
                "KartBehaviour necesita 4 colliders de rueda (FL, FR, RL, RR) o ninguno.",
                this
            );
        }

        return valid;
    }
}
