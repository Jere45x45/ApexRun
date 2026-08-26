using UnityEngine;

public class AeroController
{
    private readonly Rigidbody rigidbody;

    public AeroController(KartPhysics physics)
    {
        rigidbody = physics.Rigidbody;
    }

    public void UpdateAerodynamics(KartStats stats)
    {
        if (stats == null)
            return;

        float speed = rigidbody.linearVelocity.magnitude;

        if (speed <= 0f)
            return;

        float speedFactor = speed * speed;

        Vector3 downforce =
            -rigidbody.transform.up *
            stats.downforce *
            speedFactor;

        rigidbody.AddForce(
            downforce,
            ForceMode.Force
        );

        Vector3 aerodynamicDrag =
            -rigidbody.linearVelocity.normalized *
            stats.aerodynamicDrag *
            speedFactor;

        rigidbody.AddForce(
            aerodynamicDrag,
            ForceMode.Force
        );
    }
}