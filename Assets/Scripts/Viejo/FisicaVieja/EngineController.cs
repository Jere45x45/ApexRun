using UnityEngine;

public class EngineController
{
    private readonly WheelPhysics rearLeftWheel;
    private readonly WheelPhysics rearRightWheel;

    public EngineController(KartPhysics physics)
    {
        rearLeftWheel =
            physics.RearLeftWheel;

        rearRightWheel =
            physics.RearRightWheel;
    }

    public void UpdateMotor(
        float throttle,
        KartStats stats)
    {
        if (stats == null)
            return;

        float torque =
            throttle * stats.motorTorque;

        rearLeftWheel.SetDriveTorque(
            torque
        );

        rearRightWheel.SetDriveTorque(
            torque
        );
    }
}