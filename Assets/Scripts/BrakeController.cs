using UnityEngine;

public class BrakeController
{
    private readonly WheelPhysics frontLeftWheel;
    private readonly WheelPhysics frontRightWheel;
    private readonly WheelPhysics rearLeftWheel;
    private readonly WheelPhysics rearRightWheel;

    public BrakeController(KartPhysics physics)
    {
        frontLeftWheel =
            physics.FrontLeftWheel;

        frontRightWheel =
            physics.FrontRightWheel;

        rearLeftWheel =
            physics.RearLeftWheel;

        rearRightWheel =
            physics.RearRightWheel;
    }

    public void UpdateBrakes(
        bool braking,
        KartStats stats)
    {
        if (stats == null)
            return;

        float brakeTorque =
            braking
                ? stats.brakeTorque
                : 0f;

        frontLeftWheel.SetBrakeTorque(
            brakeTorque
        );

        frontRightWheel.SetBrakeTorque(
            brakeTorque
        );

        rearLeftWheel.SetBrakeTorque(
            brakeTorque
        );

        rearRightWheel.SetBrakeTorque(
            brakeTorque
        );
    }
}