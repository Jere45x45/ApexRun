using UnityEngine;

[CreateAssetMenu(fileName = "New Wheels", menuName = "Kart/Wheels")]

public class WheelData : KartPart
{
    [Header("Dirección")]
    public float maxSteeringAngle = 30f;

    public float minSteeringAngle = 10f;

    public float steeringReductionSpeed = 20f;

    [Header("Frenado")]
    public float brakeTorque = 3000f;

    [Header("Suspensión")]
    public float suspensionDistance = 0.2f;

    [Header("Rueda")]
    public float radius = 0.25f;

    public override void Apply(KartStats stats)
    {
        stats.maxSteeringAngle = maxSteeringAngle;
        stats.minSteeringAngle = minSteeringAngle;
        stats.steeringReductionSpeed = steeringReductionSpeed;

        stats.brakeTorque = brakeTorque;

        stats.wheelRadius = radius;
        stats.suspensionDistance = suspensionDistance;
    }
}