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
    [Min(0f)]
    public float suspensionDistance = 0.2f;

    [Min(0f)]
    public float springRate = 20000f;

    [Min(0f)]
    public float damperRate = 4000f;

    [Header("Rueda")]
    [Min(0.001f)]
    public float radius = 0.25f;

    public override PartType PartType => PartType.Wheels;

    public override void Apply(KartStats stats)
    {
        stats.maxSteeringAngle = maxSteeringAngle;
        stats.minSteeringAngle = minSteeringAngle;
        stats.steeringReductionSpeed =
            steeringReductionSpeed;

        stats.brakeTorque = brakeTorque;

        stats.wheelRadius = radius;
        stats.suspensionDistance =
            suspensionDistance;

        stats.springRate = springRate;
        stats.damperRate = damperRate;
    }

    public override void Install(
        RuntimeKartConfiguration configuration)
    {
        configuration.InstallWheels(this);
    }
}