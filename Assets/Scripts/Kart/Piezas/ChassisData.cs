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

    [Header("Volante")]
    [Tooltip("Dónde va el centro del volante, en coordenadas del kart (m): justo después de la " +
             "punta de la columna de dirección del modelo. Todos los volantes se montan acá.")]
    public Vector3 steeringWheelHub = new Vector3(0f, 0.317f, -0.162f);

    [Tooltip("Inclinación de la columna de dirección del modelo (grados): cuánto sube hacia el " +
             "piloto respecto de la horizontal. El volante queda perpendicular a la columna.")]
    [Range(-90f, 90f)]
    public float steeringColumnTilt = 25f;

    /// <summary>Eje de la columna en coordenadas del kart, apuntando hacia el piloto.</summary>
    public Vector3 SteeringColumnAxis
    {
        get
        {
            float radians = steeringColumnTilt * Mathf.Deg2Rad;
            return new Vector3(0f, Mathf.Sin(radians), -Mathf.Cos(radians));
        }
    }

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