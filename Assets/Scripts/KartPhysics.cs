using UnityEngine;

public class KartPhysics
{
    public Rigidbody Rigidbody { get; }

    public CustomWheel FrontLeftWheel { get; }
    public CustomWheel FrontRightWheel { get; }
    public CustomWheel RearLeftWheel { get; }
    public CustomWheel RearRightWheel { get; }

    public Transform FrontLeftSlot { get; }
    public Transform FrontRightSlot { get; }
    public Transform RearLeftSlot { get; }
    public Transform RearRightSlot { get; }

    public KartPhysics(
        Rigidbody rigidbody,
        CustomWheel frontLeft,
        CustomWheel frontRight,
        CustomWheel rearLeft,
        CustomWheel rearRight,
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