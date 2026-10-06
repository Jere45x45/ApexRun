using UnityEngine;
using UnityEngine.Serialization;

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

    // Velocidades del eje delantero (m/s) entre las que entra el límite de agarre
    // del volante. Casi quieto, la dirección de avance no está definida y manda
    // el tope del volante.
    private const float GripLimitStartSpeed = 1f;
    private const float GripLimitFullSpeed = 4f;

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

    [Header("Volante")]
    [Tooltip("Velocidad máxima a la que giran las ruedas delanteras con el kart lento (°/s). " +
             "Con teclado, A/D es todo o nada: esto hace de las manos del piloto.")]
    [FormerlySerializedAs("steeringSpeed")]
    [SerializeField, Min(1f)] private float steeringRateLowSpeed = 120f;

    [Tooltip("Velocidad máxima a la que giran las ruedas delanteras con el kart rápido (°/s). " +
             "A más velocidad, un piloto mueve el volante menos y más despacio.")]
    [SerializeField, Min(1f)] private float steeringRateHighSpeed = 35f;

    [Tooltip("Velocidad del kart (km/h) desde la que el volante gira a la velocidad de alta.")]
    [SerializeField, Min(1f)] private float steeringRateHighSpeedKmh = 90f;

    [Tooltip("Cuánto más rápido vuelve el volante al centro que lo que dobla. " +
             "El avance de la dirección (caster) endereza solas las ruedas al soltar.")]
    [SerializeField, Range(1f, 3f)] private float steeringReturnMultiplier = 1.5f;

    [Tooltip("Suavizado del volante (s): el volante acelera y frena en vez de arrancar " +
             "y pararse de golpe. 0 = sin suavizado.")]
    [SerializeField, Range(0f, 0.3f)] private float steeringSmoothTime = 0.06f;

    [Header("Ayudas de manejo")]
    [Tooltip("No deja girar las ruedas delanteras más allá del ángulo de máximo agarre, " +
             "medido respecto de hacia dónde va de verdad el eje delantero. Girar más no " +
             "dobla más: solo arrastra las ruedas. Como se mide contra la dirección real, " +
             "también deja contravolantear cuando la cola se va.")]
    [SerializeField] private bool limitSteeringToGrip = true;

    [Tooltip("Margen sobre el ángulo de deslizamiento de máximo agarre. 1 = justo en el pico.")]
    [SerializeField, Range(0.5f, 2f)] private float gripSteeringMargin = 1f;

    [Header("Sin mandos")]
    [Tooltip("Freno (0 a 1) mientras el kart no tiene mandos habilitados y todavía se mueve, " +
             "por ejemplo después de cruzar la meta. 0 = rueda suelto.")]
    [SerializeField, Range(0f, 1f)] private float noInputBrake = 0.25f;

    private Kart kart;

    private float throttle;
    private float steering;
    private bool braking;

    private bool inputEnabled;

    private Rigidbody rb;

    private KartVehicle vehicle;
    private KartWheelVisual[] wheelVisuals;
    private float currentSteerAngle;
    private float steerAngleVelocity;

    // Kart de otro jugador (multiplayer): la física corre en su computadora y
    // acá solo se muestra. Estos valores llegan por red (ver NetworkKart).
    private bool isSimulated = true;
    private float remoteRpm;
    private float remoteThrottle;
    private float remoteSteerAngle;
    private float remoteForwardSpeed;
    private Vector3 lastRemotePosition;
    private bool hasRemotePosition;
    private float[] wheelRadii = new float[0];

    public Kart Kart => kart;

    /// <summary>La física del kart. Null si el kart solo muestra el modelo.</summary>
    public KartVehicle Vehicle => vehicle;

    public bool InputEnabled => inputEnabled;

    /// <summary>
    /// True si la física de este kart corre en esta computadora (un jugador, o
    /// el kart propio en multiplayer). False en los karts de los demás jugadores.
    /// </summary>
    public bool IsSimulated => isSimulated;

    /// <summary>True si hay datos del motor para mostrar o hacer sonar.</summary>
    public bool HasEngineState => isSimulated ? vehicle != null && vehicle.IsConfigured : kart != null;

    public float EngineRpm => isSimulated
        ? (vehicle != null && vehicle.IsConfigured ? vehicle.Engine.Rpm : 0f)
        : remoteRpm;

    public float EngineThrottle => isSimulated
        ? (vehicle != null && vehicle.IsConfigured ? vehicle.Engine.Throttle : 0f)
        : remoteThrottle;

    /// <summary>Ángulo de las ruedas delanteras (°).</summary>
    public float SteerAngle => isSimulated ? currentSteerAngle : remoteSteerAngle;

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

        // Los mandos arrancan apagados (inputEnabled vale false) y los habilita
        // la carrera. No se apagan acá: en red, la carrera puede haberlos
        // habilitado antes de que corra este Start.
        RefreshKart();
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

    /// <summary>
    /// Local (true): este kart corre su física acá. Remoto (false): solo se
    /// muestra; la posición la pone la red y el resto llega con SetRemoteState.
    /// </summary>
    public void SetSimulated(bool simulated)
    {
        isSimulated = simulated;
        hasRemotePosition = false;

        if (!simulated)
        {
            SetInputEnabled(false);
        }
    }

    /// <summary>Estado de un kart remoto: rpm y acelerador (para el sonido) y ángulo de las ruedas.</summary>
    public void SetRemoteState(float rpm, float throttle, float steerAngle)
    {
        remoteRpm = rpm;
        remoteThrottle = throttle;
        remoteSteerAngle = steerAngle;
    }

    private void FixedUpdate()
    {
        if (kart == null || vehicle == null || !isSimulated)
            return;

        float deltaTime = Time.fixedDeltaTime;

        ApplyInputs(deltaTime);

        vehicle.Step(deltaTime);
    }

    private void LateUpdate()
    {
        if (vehicle == null || modelController == null)
            return;

        if (isSimulated)
        {
            // Ruedas visuales: con la pose interpolada del Transform, una vez por frame.
            for (int i = 0; i < wheelVisuals.Length; i++)
            {
                wheelVisuals[i].UpdatePose(transform, GetWheelModel(i), Time.deltaTime);
            }
        }
        else
        {
            UpdateRemoteWheels(Time.deltaTime);
        }

        // Volante: gira con las ruedas delanteras.
        modelController.SetSteeringAngle(SteerAngle);
    }

    /// <summary>
    /// Pasa los mandos a la física:
    /// - Acelerador positivo = motor. Negativo (tecla S) = freno: un kart no tiene marcha atrás.
    /// - Volante: ver UpdateSteering.
    /// - Sin mandos habilitados (cuenta regresiva, carrera terminada) el kart
    ///   frena suave (noInputBrake) y, ya casi quieto, queda frenado.
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
            vehicle.Brake = speed < StandstillSpeed ? 1f : noInputBrake;
        }

        UpdateSteering(speed, deltaTime);
    }

    /// <summary>
    /// Volante, como lo resuelven los simuladores para teclado y joystick:
    /// 1. El mando (−1 a 1) es una fracción del ángulo útil hacia ese lado
    ///    (GetSteerLimits): a fondo, la rueda queda en el máximo agarre.
    /// 2. Las ruedas van hacia ese ángulo con una velocidad máxima que baja con
    ///    la velocidad del kart y sube al volver al centro.
    /// 3. Un suavizado críticamente amortiguado (SmoothDamp) hace que el volante
    ///    acelere y frene, sin arranques ni paradas de golpe.
    /// </summary>
    private void UpdateSteering(float speed, float deltaTime)
    {
        GetSteerLimits(out float lowerLimit, out float upperLimit);

        float targetAngle = steering >= 0f
            ? steering * upperLimit
            : -steering * lowerLimit;

        bool returning = Mathf.Abs(targetAngle) < Mathf.Abs(currentSteerAngle);

        float maxRate = GetSteeringRate(speed);

        if (returning)
        {
            maxRate *= steeringReturnMultiplier;
        }

        currentSteerAngle = Mathf.SmoothDamp(
            currentSteerAngle,
            targetAngle,
            ref steerAngleVelocity,
            steeringSmoothTime,
            maxRate,
            deltaTime
        );

        vehicle.SteerAngle = currentSteerAngle;
    }

    /// <summary>
    /// Velocidad máxima del volante (°/s) para la velocidad del kart: pasa de la
    /// de baja a la de alta en línea recta hasta steeringRateHighSpeedKmh.
    /// </summary>
    private float GetSteeringRate(float speed)
    {
        float t = speed * 3.6f / steeringRateHighSpeedKmh;

        return Mathf.Lerp(steeringRateLowSpeed, steeringRateHighSpeed, t);
    }

    /// <summary>
    /// Ángulos de volante útiles hacia cada lado (lower ≤ 0 ≤ upper).
    /// Sin la ayuda, son el tope del volante.
    /// Con la ayuda, se mide hacia dónde va de verdad el eje delantero respecto
    /// del chasis, y se deja girar la rueda hasta el ángulo de deslizamiento de
    /// máximo agarre a cada lado de esa dirección. Así:
    /// - Al entrar en la curva, la rueda no pasa del máximo agarre; a medida que
    ///   el kart rota, el eje va más hacia adentro y el límite se abre solo.
    /// - Si la cola se va, el eje delantero va hacia el lado contrario y el
    ///   límite deja contravolantear todo lo necesario.
    /// </summary>
    private void GetSteerLimits(out float lower, out float upper)
    {
        float lockAngle = vehicle.Chassis.steering.maxSteerAngle;

        lower = -lockAngle;
        upper = lockAngle;

        if (!limitSteeringToGrip)
            return;

        Vector3 frontVelocity = GetFrontAxleLocalVelocity();

        float weight = Mathf.InverseLerp(GripLimitStartSpeed, GripLimitFullSpeed, frontVelocity.z);

        if (weight <= 0f)
            return;

        float travelAngle = Mathf.Atan2(frontVelocity.x, frontVelocity.z) * Mathf.Rad2Deg;
        float slipAngle = vehicle.Tire.LateralPeakSlipAngle * Mathf.Rad2Deg * gripSteeringMargin;

        float gripUpper = Mathf.Clamp(travelAngle + slipAngle, 0f, lockAngle);
        float gripLower = Mathf.Clamp(travelAngle - slipAngle, -lockAngle, 0f);

        upper = Mathf.Lerp(lockAngle, gripUpper, weight);
        lower = Mathf.Lerp(-lockAngle, gripLower, weight);
    }

    /// <summary>
    /// Velocidad del punto medio del eje delantero, en coordenadas del kart
    /// (x = hacia la derecha, z = hacia adelante).
    /// </summary>
    private Vector3 GetFrontAxleLocalVelocity()
    {
        KartWheel left = vehicle.Wheels[KartVehicle.FrontLeft];
        KartWheel right = vehicle.Wheels[KartVehicle.FrontRight];

        Vector3 localPoint = 0.5f * (left.LocalPosition + right.LocalPosition);
        Vector3 worldPoint = rb.position + rb.rotation * localPoint;

        return Quaternion.Inverse(rb.rotation) * rb.GetPointVelocity(worldPoint);
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
        steerAngleVelocity = 0f;
    }

    /// <summary>
    /// Ruedas de un kart remoto: acá no corre su física, así que giran según
    /// cuánto avanzó el kart desde el frame anterior, y las delanteras doblan
    /// con el ángulo que manda el dueño.
    /// </summary>
    private void UpdateRemoteWheels(float deltaTime)
    {
        Vector3 position = transform.position;

        if (deltaTime > 0f && hasRemotePosition)
        {
            Vector3 travel = position - lastRemotePosition;
            remoteForwardSpeed = Vector3.Dot(travel, transform.forward) / deltaTime;
        }

        lastRemotePosition = position;
        hasRemotePosition = true;

        for (int i = 0; i < wheelVisuals.Length; i++)
        {
            bool front = i == KartVehicle.FrontLeft || i == KartVehicle.FrontRight;
            float radius = Mathf.Max(0.05f, wheelRadii[i]);

            wheelVisuals[i].UpdatePose(
                transform,
                GetWheelModel(i),
                deltaTime,
                remoteForwardSpeed / radius,
                front ? remoteSteerAngle : 0f
            );
        }
    }

    private void CreateVehicle()
    {
        vehicle = new KartVehicle(rb, wheelColliders);
        wheelVisuals = new KartWheelVisual[vehicle.Wheels.Length];
        wheelRadii = new float[vehicle.Wheels.Length];

        for (int i = 0; i < vehicle.Wheels.Length; i++)
        {
            KartWheel wheel = vehicle.Wheels[i];
            wheelVisuals[i] = new KartWheelVisual(wheel, wheel.LocalPosition.x < 0f);

            Vector3 scale = wheelColliders[i].transform.lossyScale;
            wheelRadii[i] = wheelColliders[i].radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
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
