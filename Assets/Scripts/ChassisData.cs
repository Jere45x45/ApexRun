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

    [Header("Física nueva (Fase 0)")]
    [Tooltip("Masa, inercia, bastidor, eje trasero, frenos y dirección de la física nueva. " +
             "Cuando Race pase a la física nueva, reemplaza a mass, centerOfMass, drag y angularDrag.")]
    public ChassisSettings chassisSettings = new ChassisSettings();

    public override PartType PartType => PartType.Chassis;

    public override void Apply(KartStats stats)
    {
        stats.mass = mass;
        stats.centerOfMass = centerOfMass;
        stats.drag = drag;
        stats.angularDrag = angularDrag;

        // Copia: la física no tiene que poder modificar el asset.
        stats.chassis = JsonUtility.FromJson<ChassisSettings>(JsonUtility.ToJson(chassisSettings));
    }

    public override void Install(RuntimeKartConfiguration configuration)
    {
        configuration.InstallChassis(this);
    }
}