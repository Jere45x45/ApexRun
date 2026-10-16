using UnityEngine;

public class SteeringController
{
    private readonly WheelPhysics frontLeftWheel;
    private readonly WheelPhysics frontRightWheel;

    public SteeringController(KartPhysics physics)
    {
        frontLeftWheel =
            physics.FrontLeftWheel;

        frontRightWheel =
            physics.FrontRightWheel;
    }

    public void UpdateSteering(
        float steeringInput,
        float speed,
        KartStats stats)
    {
        if (stats == null)
            return;

        float steeringAngle =
            Mathf.Lerp(
                stats.maxSteeringAngle,
                stats.minSteeringAngle,
                Mathf.Clamp01(
                    speed /
                    Mathf.Max(
                        stats.steeringReductionSpeed,
                        0.001f
                    )
                )
            );

        float targetAngle =
            Mathf.Clamp(
                steeringInput,
                -1f,
                1f
            ) * steeringAngle;

        frontLeftWheel.SetSteeringAngle(
            targetAngle
        );

        frontRightWheel.SetSteeringAngle(
            targetAngle
        );
    }
}