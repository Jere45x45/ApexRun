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

    [Header("Aerodinámica")]
    public float downforce;
    public float aerodynamicDrag;
}