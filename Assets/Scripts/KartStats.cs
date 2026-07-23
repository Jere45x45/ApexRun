using UnityEngine;

[System.Serializable]
public class KartStats
{
    public float motorTorque;

    public float maxSteeringAngle;
    public float minSteeringAngle;
    public float steeringReductionSpeed;

    public float brakeTorque;

    public float mass;
    public Vector3 centerOfMass;
    public float drag;
    public float angularDrag;

    public float wheelRadius;
    public float suspensionDistance;
}