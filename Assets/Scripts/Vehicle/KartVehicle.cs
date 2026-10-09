using System;
using UnityEngine;

/// <summary>
/// El kart de la física nueva completo: ruedas, ejes, motor, dirección y aire.
///
/// No es un MonoBehaviour: lo usan KartBehaviour (en el juego) y
/// KartPhysicsTestRig (en las pruebas), así los dos simulan exactamente lo mismo.
/// Quien lo usa fija los mandos (Throttle, Brake, SteerAngle) y llama a Step
/// una vez por FixedUpdate.
/// </summary>
public class KartVehicle
{
    public const int FrontLeft = 0;
    public const int FrontRight = 1;
    public const int RearLeft = 2;
    public const int RearRight = 3;

    private static readonly string[] WheelNames = { "FL", "FR", "RL", "RR" };

    private readonly Rigidbody body;

    private KartAxle frontLeftAxle;
    private KartAxle frontRightAxle;
    private KartAxle[] axles;
    private int tireSubsteps = 10;

    public Rigidbody Body => body;

    /// <summary>Ruedas en orden FL, FR, RL, RR.</summary>
    public KartWheel[] Wheels { get; }

    public KartAxle RearAxle { get; private set; }
    public KartEngine Engine { get; private set; }
    public KartSteering Steering { get; private set; }
    public KartAerodynamics Aerodynamics { get; private set; }
    public ChassisSettings Chassis { get; private set; }
    public TireSettings Tire { get; private set; }

    /// <summary>True cuando ya recibió sus valores con Configure.</summary>
    public bool IsConfigured => Engine != null;

    /// <summary>Acelerador, de 0 a 1.</summary>
    public float Throttle { get; set; }

    /// <summary>Freno, de 0 a 1.</summary>
    public float Brake { get; set; }

    /// <summary>Ángulo de dirección en grados (rueda imaginaria en el centro del eje delantero). Positivo = derecha.</summary>
    public float SteerAngle { get; set; }

    /// <summary>Velocidad en la dirección en que apunta el kart (m/s). Negativa = marcha atrás.</summary>
    public float ForwardSpeed => Vector3.Dot(body.linearVelocity, body.rotation * Vector3.forward);

    public KartVehicle(Rigidbody body, SphereCollider[] wheelColliders)
    {
        this.body = body ?? throw new ArgumentNullException(nameof(body));

        if (wheelColliders == null || wheelColliders.Length != WheelNames.Length)
            throw new ArgumentException("El kart necesita 4 colliders de rueda: FL, FR, RL, RR.", nameof(wheelColliders));

        Wheels = new KartWheel[WheelNames.Length];

        for (int i = 0; i < Wheels.Length; i++)
        {
            Wheels[i] = new KartWheel(WheelNames[i], wheelColliders[i]);
        }
    }

    /// <summary>
    /// Aplica los valores del kart (normalmente, los de KartStats). Se puede
    /// volver a llamar al cambiar piezas: el kart sigue andando a la misma velocidad.
    /// </summary>
    public void Configure(
        ChassisSettings chassis,
        TireSettings tire,
        float wheelInertia,
        EngineSettings engineSettings,
        AeroSettings aero,
        int substeps)
    {
        Chassis = chassis ?? throw new ArgumentNullException(nameof(chassis));
        Tire = tire ?? throw new ArgumentNullException(nameof(tire));

        if (engineSettings == null)
            throw new ArgumentNullException(nameof(engineSettings));

        if (aero == null)
            throw new ArgumentNullException(nameof(aero));

        tireSubsteps = Mathf.Max(1, substeps);

        ChassisBodyConfigurator.Configure(
            body,
            chassis.mass,
            chassis.centerOfMass,
            chassis.inertiaBoxSize,
            chassis.angularDamping
        );

        foreach (KartWheel wheel in Wheels)
        {
            wheel.ApplySettings(tire);
        }

        // Delanteras: cada una gira suelta. Traseras: eje rígido compartido,
        // con las dos ruedas montadas.
        float inertia = Mathf.Max(0.001f, wheelInertia);
        float rearInertia = chassis.rearAxleInertia + 2f * inertia;

        frontLeftAxle = new KartAxle("Front Left", inertia, Wheels[FrontLeft]);
        frontRightAxle = new KartAxle("Front Right", inertia, Wheels[FrontRight]);
        RearAxle = new KartAxle("Rear", rearInertia, Wheels[RearLeft], Wheels[RearRight]);

        axles = new[] { frontLeftAxle, frontRightAxle, RearAxle };

        float rearAxleZ = (Wheels[RearLeft].LocalPosition.z + Wheels[RearRight].LocalPosition.z) * 0.5f;

        Steering = new KartSteering(chassis.steering, Wheels[FrontLeft], Wheels[FrontRight], rearAxleZ);
        Steering.ChassisTwistFactor = ComputeChassisTwistFactor();

        Engine = new KartEngine(engineSettings);
        Aerodynamics = new KartAerodynamics(aero);

        ResetMotion();
    }

    /// <summary>
    /// Pone ruedas, motor y goma de acuerdo con la velocidad actual del Rigidbody.
    /// Llamarlo después de mover el kart a mano (respawn, reubicación).
    /// </summary>
    public void ResetMotion()
    {
        if (!IsConfigured)
            return;

        // Las ruedas giran a la velocidad del kart, sin patinar.
        float rollingSpeed = ForwardSpeed / Tire.radius;

        foreach (KartAxle axle in axles)
        {
            axle.SetAngularVelocity(rollingSpeed);
        }

        foreach (KartWheel wheel in Wheels)
        {
            wheel.ResetTireState();
        }

        // El motor arranca en ralentí, o acompañando al eje si el kart ya viene rápido.
        Engine.ResetState(RearAxle.AngularVelocity);
    }

    /// <summary>Avanza la física un paso. Va en FixedUpdate.</summary>
    public void Step(float deltaTime)
    {
        if (!IsConfigured)
            return;

        // Dirección antes de los contactos: la geometría mueve el centro de cada delantera.
        Steering.Apply(SteerAngle);

        float brake = Mathf.Clamp01(Brake);

        Engine.Throttle = Mathf.Clamp01(Throttle);
        RearAxle.BrakeTorque = brake * Chassis.rearBrakeTorque;
        frontLeftAxle.BrakeTorque = brake * Chassis.frontBrakeTorque;
        frontRightAxle.BrakeTorque = brake * Chassis.frontBrakeTorque;

        // 1. Contactos: cargas verticales de las cuatro ruedas.
        foreach (KartWheel wheel in Wheels)
        {
            wheel.UpdateContact(body, Tire);
        }

        // 2. Motor, neumáticos y ejes, en subpasos cortos. El motor va en los
        //    subpasos porque, con el embrague trabado, gira junto con el eje.
        foreach (KartWheel wheel in Wheels)
        {
            wheel.BeginTireStep(body);
        }

        float substepTime = deltaTime / tireSubsteps;

        for (int i = 0; i < tireSubsteps; i++)
        {
            Engine.BeginSubstep(RearAxle);

            foreach (KartAxle axle in axles)
            {
                axle.Step(substepTime, Tire);
            }

            Engine.EndSubstep(RearAxle, substepTime);
        }

        // 3. Fuerzas promedio de los neumáticos sobre el chasis.
        foreach (KartWheel wheel in Wheels)
        {
            wheel.ApplyTireForces(body, deltaTime);
        }

        // 4. Aire: resistencia y carga aerodinámica.
        Aerodynamics.Apply(body);
    }

    /// <summary>
    /// Qué parte de un movimiento cruzado de las delanteras llega a las ruedas.
    /// Los neumáticos de cada eje resisten que el eje se incline respecto del
    /// piso; el bastidor resiste torcerse. Están en serie: el más blando se
    /// lleva la mayor parte del movimiento.
    /// </summary>
    private float ComputeChassisTwistFactor()
    {
        float frontHalfTrack = (Wheels[FrontRight].LocalPosition.x - Wheels[FrontLeft].LocalPosition.x) * 0.5f;
        float rearHalfTrack = (Wheels[RearRight].LocalPosition.x - Wheels[RearLeft].LocalPosition.x) * 0.5f;

        // Rigidez al rolido de cada eje dada por sus dos neumáticos (N·m/rad).
        float frontRollStiffness = 2f * Tire.verticalStiffness * frontHalfTrack * frontHalfTrack;
        float rearRollStiffness = 2f * Tire.verticalStiffness * rearHalfTrack * rearHalfTrack;
        float tireTwistStiffness = 1f / (1f / frontRollStiffness + 1f / rearRollStiffness);

        float frameTwistStiffness = Chassis.torsionalStiffness * Mathf.Rad2Deg;

        return frameTwistStiffness / (frameTwistStiffness + tireTwistStiffness);
    }
}
