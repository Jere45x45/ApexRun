using UnityEngine;

public class BrakeController
{
    private readonly WheelCollider frontLeftWheel;
    private readonly WheelCollider frontRightWheel;
    private readonly WheelCollider rearLeftWheel;
    private readonly WheelCollider rearRightWheel;
    
    public BrakeController(KartPhysics physics)
    {
        frontLeftWheel = physics.FrontLeftWheel;
        frontRightWheel = physics.FrontRightWheel;
        rearLeftWheel = physics.RearLeftWheel;
        rearRightWheel = physics.RearRightWheel;
    }

    public void UpdateBrakes(bool braking, KartStats stats)
    {
        float brakeTorque = braking ? stats.brakeTorque : 0f;

        frontLeftWheel.brakeTorque = brakeTorque;
        frontRightWheel.brakeTorque = brakeTorque;
        rearLeftWheel.brakeTorque = brakeTorque;
        rearRightWheel.brakeTorque = brakeTorque;
    }
}