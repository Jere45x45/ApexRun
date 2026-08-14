using UnityEngine;

public static class PhysicsConfigurator
{
    public static void Configure(KartPhysics physics, KartStats stats)
    {
        physics.Rigidbody.mass = stats.mass;

        physics.Rigidbody.linearDamping = stats.drag;
        physics.Rigidbody.angularDamping = stats.angularDrag;

        physics.Rigidbody.centerOfMass = stats.centerOfMass;

        physics.Rigidbody.maxLinearVelocity = stats.maxSpeed;

        ConfigureWheel(physics.FrontLeftWheel, stats);
        ConfigureWheel(physics.FrontRightWheel, stats);
        ConfigureWheel(physics.RearLeftWheel, stats);
        ConfigureWheel(physics.RearRightWheel, stats);
    }

    private static void ConfigureWheel(WheelCollider wheel, KartStats stats)
    {
        wheel.radius = stats.wheelRadius;
        wheel.suspensionDistance = stats.suspensionDistance;
    }
}