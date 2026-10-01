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
}