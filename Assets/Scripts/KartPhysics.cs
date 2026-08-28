using UnityEngine;

public class KartPhysics
{
    public Rigidbody Rigidbody { get; }

    public WheelCollider FrontLeftWheel { get; }
    public WheelCollider FrontRightWheel { get; }
    public WheelCollider RearLeftWheel { get; }
    public WheelCollider RearRightWheel { get; }

    public Transform FrontLeftSlot { get; }
    public Transform FrontRightSlot { get; }
    public Transform RearLeftSlot { get; }
    public Transform RearRightSlot { get; }

    public KartPhysics(
        Rigidbody rigidbody,
        WheelCollider frontLeft,
        WheelCollider frontRight,
        WheelCollider rearLeft,
        WheelCollider rearRight,
        Transform frontLeftSlot,
        Transform frontRightSlot,
        Transform rearLeftSlot,
        Transform rearRightSlot)
    {
        Rigidbody = rigidbody;

        FrontLeftWheel = frontLeft;
        FrontRightWheel = frontRight;
        RearLeftWheel = rearLeft;
        RearRightWheel = rearRight;

        FrontLeftSlot = frontLeftSlot;
        FrontRightSlot = frontRightSlot;
        RearLeftSlot = rearLeftSlot;
        RearRightSlot = rearRightSlot;
    }
}