using UnityEngine;

public class WheelVisualController
{
    private readonly WheelCollider frontLeftWheel;
    private readonly WheelCollider frontRightWheel;
    private readonly WheelCollider rearLeftWheel;
    private readonly WheelCollider rearRightWheel;

    private readonly Transform frontLeftSlot;
    private readonly Transform frontRightSlot;
    private readonly Transform rearLeftSlot;
    private readonly Transform rearRightSlot;

    public WheelVisualController(KartPhysics physics)
    {
        frontLeftWheel = physics.FrontLeftWheel;
        frontRightWheel = physics.FrontRightWheel;
        rearLeftWheel = physics.RearLeftWheel;
        rearRightWheel = physics.RearRightWheel;

        frontLeftSlot = physics.FrontLeftSlot;
        frontRightSlot = physics.FrontRightSlot;
        rearLeftSlot = physics.RearLeftSlot;
        rearRightSlot = physics.RearRightSlot;
    }

    public void UpdateVisuals()
    {
        UpdateWheel(frontLeftWheel, frontLeftSlot);
        UpdateWheel(frontRightWheel, frontRightSlot);
        UpdateWheel(rearLeftWheel, rearLeftSlot);
        UpdateWheel(rearRightWheel, rearRightSlot);
    }

    private void UpdateWheel(
        WheelCollider wheel,
        Transform slot)
    {
        if (wheel == null || slot == null)
            return;

        if (slot.childCount == 0)
            return;

        Transform wheelVisual = slot.GetChild(0);

        wheel.GetWorldPose(
            out Vector3 position,
            out Quaternion rotation
        );

        wheelVisual.position = position;
        wheelVisual.rotation = rotation;
    }
}