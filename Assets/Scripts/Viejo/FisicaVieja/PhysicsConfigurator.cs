using UnityEngine;

public static class PhysicsConfigurator
{
    public static void Configure(
        KartPhysics physics,
        KartStats stats)
    {
        if (physics == null || stats == null)
            return;

        Rigidbody rb = physics.Rigidbody;

        if (rb == null)
            return;

        rb.mass = stats.mass;

        rb.linearDamping =
            stats.drag;

        rb.angularDamping =
            stats.angularDrag;

        rb.centerOfMass =
            stats.centerOfMass;

        rb.maxLinearVelocity =
            stats.maxSpeed;

        physics.Configure(stats);
    }
}