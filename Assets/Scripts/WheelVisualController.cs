using UnityEngine;

public class WheelVisualController
{
    private readonly WheelCollider frontLeftWheel;
    private readonly WheelCollider frontRightWheel;
    private readonly WheelCollider rearLeftWheel;
    private readonly WheelCollider rearRightWheel;

    private readonly ModelSlot frontLeftModelSlot;
    private readonly ModelSlot frontRightModelSlot;
    private readonly ModelSlot rearLeftModelSlot;
    private readonly ModelSlot rearRightModelSlot;

    private static readonly Quaternion LeftWheelRotationOffset =
        Quaternion.Euler(0f, 180f, 0f);

    public WheelVisualController(
        KartPhysics physics,
        ModelSlot frontLeftModelSlot,
        ModelSlot frontRightModelSlot,
        ModelSlot rearLeftModelSlot,
        ModelSlot rearRightModelSlot)
    {
        frontLeftWheel = physics.FrontLeftWheel;
        frontRightWheel = physics.FrontRightWheel;
        rearLeftWheel = physics.RearLeftWheel;
        rearRightWheel = physics.RearRightWheel;

        this.frontLeftModelSlot = frontLeftModelSlot;
        this.frontRightModelSlot = frontRightModelSlot;
        this.rearLeftModelSlot = rearLeftModelSlot;
        this.rearRightModelSlot = rearRightModelSlot;
    }

    public void UpdateVisuals()
    {
        UpdateWheel(
            frontLeftWheel,
            frontLeftModelSlot,
            LeftWheelRotationOffset
        );

        UpdateWheel(
            frontRightWheel,
            frontRightModelSlot,
            Quaternion.identity
        );

        UpdateWheel(
            rearLeftWheel,
            rearLeftModelSlot,
            LeftWheelRotationOffset
        );

        UpdateWheel(
            rearRightWheel,
            rearRightModelSlot,
            Quaternion.identity
        );
    }

    private void UpdateWheel(
        WheelCollider wheel,
        ModelSlot modelSlot,
        Quaternion rotationOffset)
    {
        if (wheel == null || modelSlot == null)
            return;

        GameObject visual =
            modelSlot.CurrentInstance;

        if (visual == null)
            return;

        wheel.GetWorldPose(
            out Vector3 position,
            out Quaternion rotation
        );

        visual.transform.SetPositionAndRotation(
            position,
            rotation * rotationOffset
        );
    }
}