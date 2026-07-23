using UnityEngine;

public class WheelVisualController
{
    private readonly WheelCollider frontLeftWheel;
    private readonly WheelCollider frontRightWheel;
    private readonly WheelCollider rearLeftWheel;
    private readonly WheelCollider rearRightWheel;

    private readonly Transform frontLeftMesh;
    private readonly Transform frontRightMesh;
    private readonly Transform rearLeftMesh;
    private readonly Transform rearRightMesh;

    public WheelVisualController(KartPhysics physics)
    {
        frontLeftWheel = physics.FrontLeftWheel;
        frontRightWheel = physics.FrontRightWheel;
        rearLeftWheel = physics.RearLeftWheel;
        rearRightWheel = physics.RearRightWheel;
        
        frontLeftMesh = physics.FrontLeftMesh;
        frontRightMesh = physics.FrontRightMesh;
        rearLeftMesh = physics.RearLeftMesh;
        rearRightMesh = physics.RearRightMesh;
    }

    public void UpdateVisuals()
    {
        UpdateWheel(frontLeftWheel, frontLeftMesh);
        UpdateWheel(frontRightWheel, frontRightMesh);
        UpdateWheel(rearLeftWheel, rearLeftMesh);
        UpdateWheel(rearRightWheel, rearRightMesh);
    }

    private void UpdateWheel(WheelCollider wheel, Transform mesh)
    {
        wheel.GetWorldPose(out Vector3 position, out Quaternion rotation);

        mesh.position = position;
        mesh.rotation = rotation;
    }
}