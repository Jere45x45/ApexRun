using UnityEngine;

[CreateAssetMenu(fileName = "New Chassis", menuName = "Kart/Chassis")]

public class ChassisData : KartPart
{
    [Header("Peso")]
    public float mass = 150f;

    public Vector3 centerOfMass = new Vector3(0f, -0.5f, 0f);

    [Header("Aerodinámica")]
    public float drag = 0.05f;

    public float angularDrag = 0.5f;

    public override void Apply(KartStats stats)
    {
        stats.mass = mass;
        stats.centerOfMass = centerOfMass;
        stats.drag = drag;
        stats.angularDrag = angularDrag;
    }
}