using System;
using UnityEngine;

public class KartPhysics
{
    public Rigidbody Rigidbody { get; }

    public WheelPhysics FrontLeftWheel { get; }
    public WheelPhysics FrontRightWheel { get; }
    public WheelPhysics RearLeftWheel { get; }
    public WheelPhysics RearRightWheel { get; }

    public KartPhysics(
        Rigidbody rigidbody,
        Transform frontLeftPoint,
        Transform frontRightPoint,
        Transform rearLeftPoint,
        Transform rearRightPoint)
    {
        if (rigidbody == null)
            throw new ArgumentNullException(nameof(rigidbody));

        if (frontLeftPoint == null)
            throw new ArgumentNullException(nameof(frontLeftPoint));

        if (frontRightPoint == null)
            throw new ArgumentNullException(nameof(frontRightPoint));

        if (rearLeftPoint == null)
            throw new ArgumentNullException(nameof(rearLeftPoint));

        if (rearRightPoint == null)
            throw new ArgumentNullException(nameof(rearRightPoint));

        Rigidbody = rigidbody;

        FrontLeftWheel =
            new WheelPhysics(
                rigidbody,
                frontLeftPoint
            );

        FrontRightWheel =
            new WheelPhysics(
                rigidbody,
                frontRightPoint
            );

        RearLeftWheel =
            new WheelPhysics(
                rigidbody,
                rearLeftPoint
            );

        RearRightWheel =
            new WheelPhysics(
                rigidbody,
                rearRightPoint
            );
    }

    public void Configure(KartStats stats)
    {
        if (stats == null)
            throw new ArgumentNullException(
                nameof(stats)
            );

        FrontLeftWheel.Configure(
            stats.wheelRadius,
            stats.suspensionDistance,
            stats.springRate,
            stats.damperRate
        );

        FrontRightWheel.Configure(
            stats.wheelRadius,
            stats.suspensionDistance,
            stats.springRate,
            stats.damperRate
        );

        RearLeftWheel.Configure(
            stats.wheelRadius,
            stats.suspensionDistance,
            stats.springRate,
            stats.damperRate
        );

        RearRightWheel.Configure(
            stats.wheelRadius,
            stats.suspensionDistance,
            stats.springRate,
            stats.damperRate
        );
    }

    public void UpdateWheels(float deltaTime)
    {
        FrontLeftWheel.Update(deltaTime);
        FrontRightWheel.Update(deltaTime);
        RearLeftWheel.Update(deltaTime);
        RearRightWheel.Update(deltaTime);
    }

    public void ApplySuspension(float deltaTime)
    {
        FrontLeftWheel.ApplySuspensionForce(deltaTime);
        FrontRightWheel.ApplySuspensionForce(deltaTime);
        RearLeftWheel.ApplySuspensionForce(deltaTime);
        RearRightWheel.ApplySuspensionForce(deltaTime);
    }
}