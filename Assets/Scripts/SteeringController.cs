using UnityEngine;

public class SteeringController
{
    private readonly WheelCollider frontLeftWheel;
    private readonly WheelCollider frontRightWheel;

    public SteeringController(KartPhysics physics)
    {
        frontLeftWheel = physics.FrontLeftWheel;
        frontRightWheel = physics.FrontRightWheel;
    }

    public void UpdateSteering(float steeringInput, float speed, KartStats stats)
    {
        float steeringAngle = Mathf.Lerp(
            stats.maxSteeringAngle,
            stats.minSteeringAngle,
            Mathf.Clamp01(speed / stats.steeringReductionSpeed)
        );

        frontLeftWheel.steerAngle = steeringInput * steeringAngle;
        frontRightWheel.steerAngle = steeringInput * steeringAngle;
    }
}