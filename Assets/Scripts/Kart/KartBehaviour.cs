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

    [Tooltip("Diferencial a baja velocidad: casi quieto, las ruedas traseras giran cada una a su " +
             "velocidad en vez de arrastrarse (eje rígido), así el kart dobla cerrado al arrancar " +
             "o a paso de hombre. Se va apagando con la velocidad: en carrera el eje vuelve a ser rígido.")]
    [SerializeField] private bool lowSpeedDifferential = true;

    [Tooltip("Hasta esta velocidad (km/h) el diferencial actúa completo.")]
    [SerializeField, Min(0f)] private float differentialFullBelowKmh = 8f;

    [Tooltip("Desde esta velocidad (km/h) el eje trasero es rígido, como en un kart real.")]
    [SerializeField, Min(0f)] private float differentialOffAboveKmh = 25f;

    [Tooltip("Control de tracción a baja velocidad: si las traseras patinan, baja el acelerador " +
             "hasta que vuelven a agarrar. Así, arrancando con el volante girado, el kart avanza " +
             "doblando en vez de hacer un trompo. Actúa en el mismo rango que el diferencial.")]
    [SerializeField] private bool lowSpeedTractionControl = true;

    [Tooltip("Patinamiento permitido, en veces el de máximo agarre (1 = justo en el pico).")]
    [SerializeField, Range(0.8f, 3f)] private float tractionSlipMargin = 1.3f;

    [Tooltip("ABS: con el freno a fondo (teclado), baja la fuerza de frenado de cada eje cuando " +
             "sus ruedas están por bloquearse, así siguen girando y agarrando de costado. En curva " +
             "deja frenar menos, porque parte del agarre se usa para doblar. Adelante evita que el " +
             "kart siga de largo sin doblar; atrás, el trompo.")]
    [SerializeField] private bool antiLockBrakes = true;

    [Tooltip("Patinamiento de frenado permitido, en veces el de máximo agarre (1 = justo en el pico). " +
             "Menos = más estable en curva pero frena un poco más largo.")]
    [SerializeField, Range(0.3f, 2f)] private float absSlipMargin = 0.9f;

    [Tooltip("Por debajo de esta velocidad (km/h) el ABS no actúa y el freno puede bloquear, para quedar quieto.")]
    [SerializeField, Min(0f)] private float absMinSpeedKmh = 6f;

    [Header("Freno con teclado")]
    [Tooltip("Tiempo para llegar al freno a fondo al apretar la tecla (s). Como un pie que pisa " +
             "rápido pero no de golpe: el peso pasa adelante y las ruedas no se bloquean de entrada. " +
             "0 = de golpe.")]
    [SerializeField, Min(0f)] private float brakeApplyTime = 0.12f;

    [Tooltip("Tiempo para soltar el freno del todo al soltar la tecla (s).")]
    [SerializeField, Min(0f)] private float brakeReleaseTime = 0.05f;

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

    // Control de tracción: fracción del acelerador que se deja pasar (0 a 1).
    private float tractionLimit = 1f;

    // ABS: fracción del freno que se deja pasar en cada eje (0 a 1).
    private float brakeLimit = 1f;
    private float frontBrakeLimit = 1f;

    // Freno pedido, ya con la rampa del teclado (0 a 1).
    private float brakeInput;

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

        float lowSpeedAssist = GetLowSpeedAssist(speed);

        if (inputEnabled)
        {
            float target = Mathf.Max(braking ? 1f : 0f, Mathf.Clamp01(-throttle));
            float rampTime = target > brakeInput ? brakeApplyTime : brakeReleaseTime;

            brakeInput = rampTime > 0f
                ? Mathf.MoveTowards(brakeInput, target, deltaTime / rampTime)
                : target;

            vehicle.Throttle = Mathf.Clamp01(throttle) * GetTractionLimit(lowSpeedAssist, deltaTime);
            vehicle.Brake = brakeInput * GetBrakeLimit(speed, deltaTime);
            vehicle.FrontBrake = brakeInput * GetFrontBrakeLimit(speed, deltaTime);
        }
        else
        {
            brakeInput = 0f;
            vehicle.Throttle = 0f;
            vehicle.Brake = speed < StandstillSpeed ? 1f : noInputBrake;
            vehicle.FrontBrake = vehicle.Brake;
        }

        UpdateSteering(speed, deltaTime);

        vehicle.RearDifferential = lowSpeedDifferential ? lowSpeedAssist : 0f;
    }

    /// <summary>
    /// Cuánto actúan las ayudas de baja velocidad (diferencial y control de
    /// tracción): 1 hasta differentialFullBelowKmh, 0 desde differentialOffAboveKmh.
    /// </summary>
    private float GetLowSpeedAssist(float speed)
    {
        float kmh = speed * 3.6f;
        return 1f - Mathf.InverseLerp(differentialFullBelowKmh, Mathf.Max(differentialFullBelowKmh + 0.1f, differentialOffAboveKmh), kmh);
    }

    /// <summary>
    /// ABS (ver antiLockBrakes). Para cada trasera mide cuánto se acerca al
    /// bloqueo respecto de lo que puede frenar sin perder el agarre de costado:
    /// el neumático tiene un solo presupuesto de agarre (círculo de fricción),
    /// así que si ya está doblando, le queda menos para frenar. Promedia las
    /// dos ruedas por la carga (la que está en el aire no cuenta), baja el
    /// freno rápido si se pasa y lo devuelve de a poco cuando vuelve a rodar.
    /// </summary>
    private float GetBrakeLimit(float speed, float deltaTime)
    {
        return UpdateBrakeLimit(ref brakeLimit, KartVehicle.RearLeft, KartVehicle.RearRight, speed, deltaTime);
    }

    /// <summary>Lo mismo para las delanteras, que frenan cada una con su propio eje.</summary>
    private float GetFrontBrakeLimit(float speed, float deltaTime)
    {
        return UpdateBrakeLimit(ref frontBrakeLimit, KartVehicle.FrontLeft, KartVehicle.FrontRight, speed, deltaTime);
    }

    private float UpdateBrakeLimit(ref float limit, int leftIndex, int rightIndex, float speed, float deltaTime)
    {
        if (!antiLockBrakes || speed * 3.6f < absMinSpeedKmh)
        {
            limit = 1f;
            return 1f;
        }

        KartWheel left = vehicle.Wheels[leftIndex];
        KartWheel right = vehicle.Wheels[rightIndex];

        float loadSum = left.VerticalLoad + right.VerticalLoad;

        if (loadSum <= 1f)
            return limit;

        float usage =
            (BrakeSlipUsage(left) * left.VerticalLoad + BrakeSlipUsage(right) * right.VerticalLoad) / loadSum;

        if (usage > 1f)
            limit -= deltaTime * 10f * Mathf.Clamp01(usage - 1f + 0.25f);
        else
            limit += deltaTime * 3f;

        limit = Mathf.Clamp(limit, 0.05f, 1f);

        return limit;
    }

    /// <summary>
    /// Patinamiento de frenado de una rueda, dividido por el que se permite:
    /// absSlipMargin veces el del pico, achicado según cuánto del círculo de
    /// fricción ya usa de costado. Más de 1 = se está pasando.
    /// </summary>
    private float BrakeSlipUsage(KartWheel wheel)
    {
        TireSettings tire = vehicle.Tire;

        float braking = Mathf.Max(0f, -wheel.SlipRatio) / tire.LongitudinalPeakSlipRatio;
        float lateral = Mathf.Clamp01(Mathf.Abs(wheel.SlipAngle) * Mathf.Deg2Rad / tire.LateralPeakSlipAngle);
        float allowed = absSlipMargin * Mathf.Sqrt(Mathf.Max(0.04f, 1f - lateral * lateral));

        return braking / allowed;
    }

    /// <summary>
    /// Control de tracción (ver lowSpeedTractionControl). Mide el patinamiento
    /// de las traseras (promedio pesado por la carga, así no cuenta la rueda que
    /// está en el aire) y baja el acelerador rápido si se pasa del margen; lo
    /// devuelve de a poco cuando vuelven a agarrar.
    /// </summary>
    private float GetTractionLimit(float assist, float deltaTime)
    {
        if (!lowSpeedTractionControl || assist <= 0f)
        {
            tractionLimit = 1f;
            return 1f;
        }

        KartWheel left = vehicle.Wheels[KartVehicle.RearLeft];
        KartWheel right = vehicle.Wheels[KartVehicle.RearRight];

        float loadSum = left.VerticalLoad + right.VerticalLoad;
        float slip = loadSum > 1f
            ? (left.SlipRatio * left.VerticalLoad + right.SlipRatio * right.VerticalLoad) / loadSum
            : 0f;

        float allowed = vehicle.Tire.LongitudinalPeakSlipRatio * tractionSlipMargin;

        if (slip > allowed)
            tractionLimit -= deltaTime * 6f * Mathf.Clamp01((slip - allowed) / allowed + 0.25f);
        else
            tractionLimit += deltaTime * 1.5f;

        tractionLimit = Mathf.Clamp(tractionLimit, 0.15f, 1f);

        return Mathf.Lerp(1f, tractionLimit, assist);
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
