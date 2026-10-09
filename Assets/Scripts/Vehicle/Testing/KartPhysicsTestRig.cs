using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Banco de pruebas de la física nueva (solo PhysicsTestScene).
/// Simula el kart con KartVehicle, igual que KartBehaviour en el juego, y mide cada pasada.
/// Los valores salen de las piezas (KartConfiguration → KartBuilder → KartStats) o,
/// si no hay configuración asignada, de los campos de este componente.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class KartPhysicsTestRig : MonoBehaviour
{
    [Header("Piezas")]
    [Tooltip("Si está asignada, chasis, motor, ruedas y aerodinámica salen de sus piezas " +
             "y los valores de abajo se reemplazan al empezar.")]
    [SerializeField] private KartConfiguration configuration;

    [Header("Chasis")]
    [SerializeField] private ChassisSettings chassis = new ChassisSettings();

    [Header("Ruedas")]
    [Tooltip("Un SphereCollider trigger por rueda, ubicado en el centro de la rueda. Orden: FL, FR, RL, RR.")]
    [SerializeField] private SphereCollider[] wheelColliders = new SphereCollider[4];

    [SerializeField] private TireSettings tire = new TireSettings();

    [Tooltip("Inercia de giro de cada rueda con su llanta (kg·m²).")]
    [SerializeField, Min(0.001f)] private float wheelInertia = 0.04f;

    [Tooltip("Subpasos del neumático por cada paso de física. La goma es muy rígida " +
             "comparada con la inercia de una rueda y necesita pasos más cortos.")]
    [SerializeField, Range(1, 50)] private int tireSubsteps = 10;

    [Tooltip("Modelos de las ruedas, en el mismo orden que los colliders (FL, FR, RL, RR). Opcionales.")]
    [SerializeField] private Transform[] wheelVisuals = new Transform[4];

    [Header("Motor y transmisión")]
    [SerializeField] private EngineSettings engineSettings = new EngineSettings();

    [Header("Aerodinámica")]
    [SerializeField] private AeroSettings aero = new AeroSettings();

    [Header("Mandos de prueba")]
    [SerializeField, Range(0f, 1f)] private float throttle;
    [SerializeField, Range(0f, 1f)] private float brake;

    [Tooltip("Ángulo de dirección, en grados (el de una rueda imaginaria en el centro del eje delantero). " +
             "Positivo = derecha. Cada rueda recibe su ángulo según el Ackermann.")]
    [SerializeField, Range(-30f, 30f)] private float steerAngle;

    [Header("Prueba")]
    [SerializeField, Min(0f)] private float settleLinearSpeed = 0.02f;
    [SerializeField, Min(0f)] private float settleAngularSpeed = 0.02f;

    [Tooltip("Cada cuántos segundos se guarda una muestra de velocidad y rpm.")]
    [SerializeField, Min(0.01f)] private float telemetryInterval = 0.1f;

    private const float Speed50Kmh = 50f / 3.6f;
    private const float Speed100Kmh = 100f / 3.6f;

    private Rigidbody rb;
    private KartVehicle vehicle;
    private KartWheelVisual[] wheelVisualStates;

    private bool recording;
    private float runStartTime;
    private Vector3 runStartPosition;
    private float runStartYaw;
    private float stillSince = -1f;
    private float currentAirTime;
    private float previousForwardSpeed;
    private bool wasClutchLocked;
    private float nextTelemetryTime;
    private readonly List<Vector3> telemetry = new List<Vector3>();

    // Pedales programados: permiten cambiar acelerador/freno en un instante exacto de la prueba.
    private float scheduledTime = -1f;
    private float scheduledThrottle;
    private float scheduledBrake;

    public KartVehicle Vehicle => vehicle;
    public KartWheel[] Wheels => vehicle.Wheels;
    public KartEngine Engine => vehicle.Engine;
    public KartAerodynamics Aerodynamics => vehicle.Aerodynamics;
    public KartSteering Steering => vehicle.Steering;

    /// <summary>True si los valores salieron de las piezas de una KartConfiguration.</summary>
    public bool UsesConfiguration { get; private set; }

    public float MaxUpwardVelocity { get; private set; }
    public float MaxTiltAngle { get; private set; }
    public float MaxAngularSpeed { get; private set; }
    public float MaxWheelForce { get; private set; }
    public float MaxDeflection { get; private set; }
    public float LongestAirTime { get; private set; }
    public float MinSpeed { get; private set; }
    public float MaxSpeed { get; private set; }
    public float MaxLateralG { get; private set; }
    public float MaxAccelerationG { get; private set; }
    public float MaxDecelerationG { get; private set; }
    public float StopTime { get; private set; } = -1f;
    public float StopDistance { get; private set; } = -1f;
    public float SettleTime { get; private set; } = -1f;

    /// <summary>Máximo ángulo entre hacia dónde apunta el kart y hacia dónde va (grados). Grande = trompo.</summary>
    public float MaxBodySlipAngle { get; private set; }

    public float MaxYawRate { get; private set; }

    public float MaxRpm { get; private set; }
    public float MinRpm { get; private set; }

    /// <summary>Tiempo para llegar a 50 km/h desde el inicio de la pasada (s). -1 si no llegó.</summary>
    public float TimeTo50Kmh { get; private set; } = -1f;

    /// <summary>Tiempo para llegar a 100 km/h desde el inicio de la pasada (s). -1 si no llegó.</summary>
    public float TimeTo100Kmh { get; private set; } = -1f;

    /// <summary>Primer instante en que el embrague trabó durante la pasada (s). -1 si no trabó.</summary>
    public float ClutchLockTime { get; private set; } = -1f;

    /// <summary>Velocidad del kart cuando el embrague trabó por primera vez (m/s).</summary>
    public float ClutchLockSpeed { get; private set; } = -1f;

    /// <summary>Último instante en que el embrague se soltó durante la pasada (s). -1 si no se soltó.</summary>
    public float ClutchReleaseTime { get; private set; } = -1f;

    /// <summary>Velocidad del kart cuando el embrague se soltó (m/s).</summary>
    public float ClutchReleaseSpeed { get; private set; } = -1f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        if (configuration != null)
        {
            ApplyConfiguration(configuration);
        }

        vehicle = new KartVehicle(rb, wheelColliders);
        vehicle.Configure(chassis, tire, wheelInertia, engineSettings, aero, tireSubsteps);

        wheelVisualStates = new KartWheelVisual[vehicle.Wheels.Length];

        for (int i = 0; i < vehicle.Wheels.Length; i++)
        {
            KartWheel wheel = vehicle.Wheels[i];
            wheelVisualStates[i] = new KartWheelVisual(wheel, wheel.LocalPosition.x < 0f);
        }
    }

    /// <summary>
    /// Arma el kart con sus piezas, igual que el juego (KartBuilder → KartStats),
    /// y usa esos valores en lugar de los del componente.
    /// </summary>
    private void ApplyConfiguration(KartConfiguration kartConfiguration)
    {
        Kart kart = new Kart(new RuntimeKartConfiguration(kartConfiguration));
        KartStats stats = kart.Stats;

        chassis = stats.chassis;
        engineSettings = stats.engine;
        tire = stats.tire;
        wheelInertia = stats.wheelInertia;
        aero = stats.aero;

        UsesConfiguration = true;
    }

    /// <summary>Fija el ángulo de dirección (grados). Cada delantera recibe el suyo según el Ackermann.</summary>
    public void SetSteerAngle(float angle)
    {
        steerAngle = Mathf.Clamp(angle, -30f, 30f);
    }

    /// <summary>Acelerador y freno de prueba, de 0 a 1.</summary>
    public void SetPedals(float throttleInput, float brakeInput)
    {
        throttle = Mathf.Clamp01(throttleInput);
        brake = Mathf.Clamp01(brakeInput);
    }

    /// <summary>Cambia el torque máximo del freno trasero (N·m).</summary>
    public void SetRearBrakeTorque(float torque)
    {
        chassis.rearBrakeTorque = Mathf.Max(0f, torque);
    }

    /// <summary>Cambia los pedales dentro de 'delay' segundos (para pruebas con tiempos exactos).</summary>
    public void SchedulePedals(float delay, float throttleInput, float brakeInput)
    {
        scheduledTime = Time.time + Mathf.Max(0f, delay);
        scheduledThrottle = Mathf.Clamp01(throttleInput);
        scheduledBrake = Mathf.Clamp01(brakeInput);
    }

    private void FixedUpdate()
    {
        float deltaTime = Time.fixedDeltaTime;

        if (scheduledTime >= 0f && Time.time >= scheduledTime)
        {
            throttle = scheduledThrottle;
            brake = scheduledBrake;
            scheduledTime = -1f;
        }

        vehicle.Throttle = throttle;
        vehicle.Brake = brake;
        vehicle.SteerAngle = steerAngle;

        vehicle.Step(deltaTime);

        if (recording)
        {
            Record(deltaTime);
        }
    }

    private void LateUpdate()
    {
        // Ruedas visuales: con la pose interpolada del Transform, una vez por frame.
        for (int i = 0; i < wheelVisualStates.Length; i++)
        {
            Transform visual = i < wheelVisuals.Length ? wheelVisuals[i] : null;
            wheelVisualStates[i].UpdatePose(transform, visual, Time.deltaTime);
        }
    }

    /// <summary>Coloca el kart y le da una velocidad inicial. Empieza a medir.</summary>
    public void StartRun(Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        transform.SetPositionAndRotation(position, rotation);
        rb.position = position;
        rb.rotation = rotation;
        rb.linearVelocity = velocity;
        rb.angularVelocity = Vector3.zero;

        // Ruedas girando a la velocidad del kart, goma sin deformar y motor acorde.
        vehicle.ResetMotion();

        runStartTime = Time.time;
        runStartPosition = position;
        runStartYaw = rotation.eulerAngles.y;
        previousForwardSpeed = Vector3.Dot(velocity, rotation * Vector3.forward);

        stillSince = -1f;
        currentAirTime = 0f;

        MaxUpwardVelocity = 0f;
        MaxTiltAngle = 0f;
        MaxAngularSpeed = 0f;
        MaxWheelForce = 0f;
        MaxDeflection = 0f;
        LongestAirTime = 0f;
        MinSpeed = velocity.magnitude;
        MaxSpeed = velocity.magnitude;
        MaxLateralG = 0f;
        MaxAccelerationG = 0f;
        MaxDecelerationG = 0f;
        StopTime = -1f;
        StopDistance = -1f;
        SettleTime = -1f;
        MaxBodySlipAngle = 0f;
        MaxYawRate = 0f;
        scheduledTime = -1f;

        MaxRpm = vehicle.Engine.Rpm;
        MinRpm = vehicle.Engine.Rpm;
        TimeTo50Kmh = -1f;
        TimeTo100Kmh = -1f;
        ClutchLockTime = -1f;
        ClutchLockSpeed = -1f;
        ClutchReleaseTime = -1f;
        ClutchReleaseSpeed = -1f;
        wasClutchLocked = vehicle.Engine.IsClutchLocked;

        telemetry.Clear();
        nextTelemetryTime = 0f;

        recording = true;
    }

    private float HorizontalSpeed()
    {
        Vector3 velocity = rb.linearVelocity;
        return new Vector3(velocity.x, 0f, velocity.z).magnitude;
    }

    /// <summary>Aceleración lateral en g, para un movimiento circular: v · ω / g.</summary>
    private float LateralG()
    {
        return HorizontalSpeed() * Mathf.Abs(rb.angularVelocity.y) / Physics.gravity.magnitude;
    }

    private void Record(float deltaTime)
    {
        Vector3 velocity = rb.linearVelocity;
        float speed = HorizontalSpeed();
        float runTime = Time.time - runStartTime;

        float forwardSpeed = Vector3.Dot(velocity, transform.forward);
        float accelerationG = (forwardSpeed - previousForwardSpeed) / deltaTime / Physics.gravity.magnitude;
        previousForwardSpeed = forwardSpeed;

        MaxAccelerationG = Mathf.Max(MaxAccelerationG, accelerationG);
        MaxDecelerationG = Mathf.Max(MaxDecelerationG, -accelerationG);

        MaxUpwardVelocity = Mathf.Max(MaxUpwardVelocity, velocity.y);
        MaxTiltAngle = Mathf.Max(MaxTiltAngle, Vector3.Angle(transform.up, Vector3.up));
        MaxAngularSpeed = Mathf.Max(MaxAngularSpeed, rb.angularVelocity.magnitude);
        MinSpeed = Mathf.Min(MinSpeed, speed);
        MaxSpeed = Mathf.Max(MaxSpeed, speed);
        MaxLateralG = Mathf.Max(MaxLateralG, LateralG());
        MaxYawRate = Mathf.Max(MaxYawRate, Mathf.Abs(rb.angularVelocity.y) * Mathf.Rad2Deg);

        RecordEngine(speed, runTime);

        if (speed > 1f)
        {
            Vector3 flatVelocity = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            MaxBodySlipAngle = Mathf.Max(MaxBodySlipAngle, Vector3.Angle(flatForward, flatVelocity));
        }

        if (StopTime < 0f && brake > 0f && speed < 0.05f)
        {
            StopTime = runTime;
            Vector3 travel = transform.position - runStartPosition;
            travel.y = 0f;
            StopDistance = travel.magnitude;
        }

        bool anyGrounded = false;

        foreach (KartWheel wheel in vehicle.Wheels)
        {
            MaxWheelForce = Mathf.Max(MaxWheelForce, wheel.NormalForce);
            MaxDeflection = Mathf.Max(MaxDeflection, wheel.Deflection);
            anyGrounded |= wheel.IsGrounded;
        }

        if (anyGrounded)
        {
            currentAirTime = 0f;
        }
        else
        {
            currentAirTime += deltaTime;
            LongestAirTime = Mathf.Max(LongestAirTime, currentAirTime);
        }

        bool isStill =
            velocity.magnitude < settleLinearSpeed &&
            rb.angularVelocity.magnitude < settleAngularSpeed;

        if (!isStill)
        {
            stillSince = -1f;
            return;
        }

        if (stillSince < 0f)
        {
            stillSince = Time.time;
        }
        else if (SettleTime < 0f && Time.time - stillSince >= 0.5f)
        {
            SettleTime = stillSince - runStartTime;
        }
    }

    private void RecordEngine(float speed, float runTime)
    {
        float rpm = vehicle.Engine.Rpm;
        MaxRpm = Mathf.Max(MaxRpm, rpm);
        MinRpm = Mathf.Min(MinRpm, rpm);

        if (TimeTo50Kmh < 0f && speed >= Speed50Kmh)
        {
            TimeTo50Kmh = runTime;
        }

        if (TimeTo100Kmh < 0f && speed >= Speed100Kmh)
        {
            TimeTo100Kmh = runTime;
        }

        bool isLocked = vehicle.Engine.IsClutchLocked;

        if (isLocked && !wasClutchLocked && ClutchLockTime < 0f)
        {
            ClutchLockTime = runTime;
            ClutchLockSpeed = speed;
        }

        if (!isLocked && wasClutchLocked)
        {
            ClutchReleaseTime = runTime;
            ClutchReleaseSpeed = speed;
        }

        wasClutchLocked = isLocked;

        if (runTime >= nextTelemetryTime)
        {
            telemetry.Add(new Vector3(runTime, speed, rpm));
            nextTelemetryTime += telemetryInterval;
        }
    }

    /// <summary>Muestras de la pasada: tiempo (s); velocidad (km/h); rpm. Una por línea.</summary>
    public string GetTelemetry()
    {
        StringBuilder sb = new StringBuilder();

        foreach (Vector3 sample in telemetry)
        {
            sb.AppendLine($"{sample.x:F2};{sample.y * 3.6f:F1};{sample.z:F0}");
        }

        return sb.ToString();
    }

    public string GetReport()
    {
        Vector3 travel = transform.position - runStartPosition;
        travel.y = 0f;

        float yawChange = Mathf.DeltaAngle(runStartYaw, transform.eulerAngles.y);
        float speed = HorizontalSpeed();
        float yawRate = rb.angularVelocity.y;
        float radius = Mathf.Abs(yawRate) > 0.001f ? speed / Mathf.Abs(yawRate) : float.PositiveInfinity;

        KartWheel[] wheels = vehicle.Wheels;
        KartEngine engine = vehicle.Engine;

        StringBuilder sb = new StringBuilder();
        sb.Append($"config={(UsesConfiguration ? configuration.name : "componente")} ");
        sb.Append($"t={Time.time - runStartTime:F2}s ");
        sb.Append($"thr={throttle:F2} brk={brake:F2} steer={steerAngle:F1}° ");
        sb.Append($"speed={speed:F2} m/s ({speed * 3.6f:F1} km/h, max {MaxSpeed:F2}) ");
        sb.Append($"maxAccel={MaxAccelerationG:F2}g maxDecel={MaxDecelerationG:F2}g ");
        sb.Append($"stop={StopTime:F2}s/{StopDistance:F2}m ");
        sb.Append($"yawRate={yawRate * Mathf.Rad2Deg:F1}°/s radius={radius:F2} m ");
        sb.Append($"latG={LateralG():F2} maxLatG={MaxLateralG:F2} ");
        sb.Append($"maxBodySlip={MaxBodySlipAngle:F1}° maxYawRate={MaxYawRate:F0}°/s ");
        sb.Append($"maxTilt={MaxTiltAngle:F1}° maxAngVel={MaxAngularSpeed:F3} ");
        sb.Append($"settle={SettleTime:F2}s travel={travel.magnitude:F3} m yawChange={yawChange:F1}° ");
        sb.Append($"vel={rb.linearVelocity:F3} pos={transform.position:F2} ");
        sb.Append($"| engine rpm={engine.Rpm:F0} (min {MinRpm:F0}, max {MaxRpm:F0}) ");
        sb.Append($"clutchLocked={engine.IsClutchLocked} Te={engine.EngineTorque:F2} Tc={engine.ClutchTorque:F2} ");
        sb.Append($"limiter={engine.IsLimiterActive} ");
        sb.Append($"lock={ClutchLockTime:F2}s@{ClutchLockSpeed:F2} release={ClutchReleaseTime:F2}s@{ClutchReleaseSpeed:F2} ");
        sb.Append($"t50={TimeTo50Kmh:F2}s t100={TimeTo100Kmh:F2}s ");
        float frontLoad = wheels[KartVehicle.FrontLeft].VerticalLoad + wheels[KartVehicle.FrontRight].VerticalLoad;
        float rearLoad = wheels[KartVehicle.RearLeft].VerticalLoad + wheels[KartVehicle.RearRight].VerticalLoad;
        sb.Append($"| aero drag={vehicle.Aerodynamics.DragForce:F0} N downforce={vehicle.Aerodynamics.Downforce:F0} N ");
        sb.Append($"loads front={frontLoad:F0} N rear={rearLoad:F0} N ");
        sb.Append($"| steer FL={vehicle.Steering.LeftAngle:F1}° FR={vehicle.Steering.RightAngle:F1}° ");
        sb.Append($"dyFL={wheels[KartVehicle.FrontLeft].GeometryOffset.y * 1000f:F1}mm dyFR={wheels[KartVehicle.FrontRight].GeometryOffset.y * 1000f:F1}mm twist={vehicle.Steering.ChassisTwistFactor:F3} ");
        sb.Append($"| rearAxle={vehicle.RearAxle.AngularVelocity * vehicle.Tire.radius:F2} m/s locked={vehicle.RearAxle.IsLocked} | wheels ");

        foreach (KartWheel wheel in wheels)
        {
            sb.Append(
                $"{wheel.Name}: Fz={wheel.VerticalLoad:F0} κ={wheel.SlipRatio:F3} " +
                $"Fx={wheel.LongitudinalForce:F0} α={wheel.SlipAngle:F2}° Fy={wheel.LateralForce:F0} grip={wheel.GripUsage:F2}; "
            );
        }

        return sb.ToString();
    }
}
