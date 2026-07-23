using UnityEngine;

public class EngineController
{
    private readonly WheelCollider rearLeftWheel;
    private readonly WheelCollider rearRightWheel;

    public EngineController(KartPhysics physics)
    {
        rearLeftWheel = physics.RearLeftWheel;
        rearRightWheel = physics.RearRightWheel;
    }

    public void UpdateMotor(float throttle, KartStats stats)
    {
        float torque = throttle * stats.motorTorque;

        rearLeftWheel.motorTorque = torque;
        rearRightWheel.motorTorque = torque;
    }
}