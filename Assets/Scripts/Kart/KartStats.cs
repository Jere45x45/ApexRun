using UnityEngine;

[System.Serializable]
public class KartStats
{
    [Header("Motor")]
    public float motorTorque;
    public float maxSpeed;

    [Header("Dirección")]
    public float maxSteeringAngle;
    public float minSteeringAngle;
    public float steeringReductionSpeed;

    [Header("Frenado")]
    public float brakeTorque;

    [Header("Chasis")]
    public float mass;
    public Vector3 centerOfMass;

    public float drag;
    public float angularDrag;

    [Header("Ruedas")]
    public float wheelRadius;
    public float suspensionDistance;
    public float springRate;
    public float damperRate;
    public float suspensionTargetPosition;

    [Header("Fricción - Delanteras")]
    public WheelFrictionCurve frontForwardFriction;
    public WheelFrictionCurve frontSidewaysFriction;

    [Header("Fricción - Traseras")]
    public WheelFrictionCurve rearForwardFriction;
    public WheelFrictionCurve rearSidewaysFriction;

    [Header("Aerodinámica")]
    public float downforce;
    public float aerodynamicDrag;

    // ---------- Física nueva (Fase 0) ----------
    // Conviven con los valores de arriba hasta que Race pase a la física nueva.

    [Header("Física nueva - Chasis")]
    public ChassisSettings chassis;

    [Header("Física nueva - Motor")]
    public EngineSettings engine;

    [Header("Física nueva - Ruedas")]
    public TireSettings tire;
    public float wheelInertia;

    [Header("Física nueva - Aerodinámica")]
    [Tooltip("Sin kit aerodinámico queda la resistencia del kart con el piloto.")]
    public AeroSettings aero = new AeroSettings();
}