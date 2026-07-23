using UnityEngine;

public class KartPhysics
{
    public Rigidbody Rigidbody { get; }

    public WheelCollider FrontLeftWheel { get; }
    public WheelCollider FrontRightWheel { get; }
    public WheelCollider RearLeftWheel { get; }
    public WheelCollider RearRightWheel { get; }

    public Transform FrontLeftMesh { get; }
    public Transform FrontRightMesh { get; }
    public Transform RearLeftMesh { get; }
    public Transform RearRightMesh { get; }

    public KartPhysics(
        Rigidbody rigidbody,
        WheelCollider frontLeft,
        WheelCollider frontRight,
        WheelCollider rearLeft,
        WheelCollider rearRight,
        Transform frontLeftMesh,
        Transform frontRightMesh,
        Transform rearLeftMesh,
        Transform rearRightMesh)
    {
        Rigidbody = rigidbody;

        FrontLeftWheel = frontLeft;
        FrontRightWheel = frontRight;
        RearLeftWheel = rearLeft;
        RearRightWheel = rearRight;

        FrontLeftMesh = frontLeftMesh;
        FrontRightMesh = frontRightMesh;
        RearLeftMesh = rearLeftMesh;
        RearRightMesh = rearRightMesh;
    }
}