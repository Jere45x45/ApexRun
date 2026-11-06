using System;
using UnityEngine;

public class KartPhysics
{
    public Rigidbody Rigidbody { get; }

    public WheelPhysics FrontLeftWheel { get; }
    public WheelPhysics FrontRightWheel { get; }
    public WheelPhysics RearLeftWheel { get; }
    public WheelPhysics RearRightWheel { get; }

    public WheelPhysics[] Wheels { get; }

    public KartPhysics(
        Rigidbody rigidbody,
        WheelCollider frontLeftCollider,
        WheelCollider frontRightCollider,
        WheelCollider rearLeftCollider,
        WheelCollider rearRightCollider)
    {
        if (rigidbody == null)
            throw new ArgumentNullException(nameof(rigidbody));

        if (frontLeftCollider == null)
            throw new ArgumentNullException(nameof(frontLeftCollider));

        if (frontRightCollider == null)
            throw new ArgumentNullException(nameof(frontRightCollider));

        if (rearLeftCollider == null)
            throw new ArgumentNullException(nameof(rearLeftCollider));

        if (rearRightCollider == null)
            throw new ArgumentNullException(nameof(rearRightCollider));

        Rigidbody = rigidbody;

        FrontLeftWheel = new WheelPhysics(rigidbody, frontLeftCollider);
        FrontRightWheel = new WheelPhysics(rigidbody, frontRightCollider);
        RearLeftWheel = new WheelPhysics(rigidbody, rearLeftCollider);
        RearRightWheel = new WheelPhysics(rigidbody, rearRightCollider);

        Wheels = new[]
        {
            FrontLeftWheel,
            FrontRightWheel,
            RearLeftWheel,
            RearRightWheel
        };
    }

    public void Configure(KartStats stats)
    {
        if (stats == null)
            throw new ArgumentNullException(nameof(stats));

        FrontLeftWheel.Configure(
            stats.wheelRadius,
            stats.suspensionDistance,
            stats.springRate,
            stats.damperRate,
            stats.suspensionTargetPosition,
            stats.frontForwardFriction,
            stats.frontSidewaysFriction
        );

        FrontRightWheel.Configure(
            stats.wheelRadius,
            stats.suspensionDistance,
            stats.springRate,
            stats.damperRate,
            stats.suspensionTargetPosition,
            stats.frontForwardFriction,
            stats.frontSidewaysFriction
        );

        RearLeftWheel.Configure(
            stats.wheelRadius,
            stats.suspensionDistance,
            stats.springRate,
            stats.damperRate,
            stats.suspensionTargetPosition,
            stats.rearForwardFriction,
            stats.rearSidewaysFriction
        );

        RearRightWheel.Configure(
            stats.wheelRadius,
            stats.suspensionDistance,
            stats.springRate,
            stats.damperRate,
            stats.suspensionTargetPosition,
            stats.rearForwardFriction,
            stats.rearSidewaysFriction
        );
    }

    public void UpdateWheels(float deltaTime)
    {
        FrontLeftWheel.Update(deltaTime);
        FrontRightWheel.Update(deltaTime);
        RearLeftWheel.Update(deltaTime);
        RearRightWheel.Update(deltaTime);
    }
}