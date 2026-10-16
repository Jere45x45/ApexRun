using UnityEngine;

/// <summary>
/// Volante del catálogo. Por ahora es una pieza solo visual: no cambia la
/// física del kart.
///
/// Los modelos de volante vienen ubicados en coordenadas del kart (como el
/// chasis, el motor y el kit aerodinámico): el modelo se instala en el origen
/// del kart y KartModelController lo gira alrededor de hubPosition, sobre el
/// eje de la columna, cuando el piloto dobla.
/// </summary>
[CreateAssetMenu(fileName = "New Steering Wheel", menuName = "Kart/Steering Wheel")]
public class SteeringWheelData : KartPart
{
    [Header("Montaje")]
    [Tooltip("Centro del volante en coordenadas del kart (m): el punto alrededor del cual gira.")]
    public Vector3 hubPosition = new Vector3(0f, 0.31f, -0.16f);

    [Tooltip("Inclinación del volante (grados): cuánto se va la parte de arriba hacia adelante " +
             "respecto de la vertical. La columna (el eje de giro) es perpendicular al volante.")]
    [Range(-90f, 90f)]
    public float tilt = 25f;

    public override PartType PartType => PartType.SteeringWheel;

    /// <summary>Eje de la columna en coordenadas del kart, apuntando hacia el piloto.</summary>
    public Vector3 ColumnAxis
    {
        get
        {
            float radians = tilt * Mathf.Deg2Rad;
            return new Vector3(0f, Mathf.Sin(radians), -Mathf.Cos(radians));
        }
    }

    public override void Apply(KartStats stats)
    {
        // Solo visual por ahora: el volante no cambia la física del kart.
    }

    public override void Install(RuntimeKartConfiguration configuration)
    {
        configuration.InstallSteeringWheel(this);
    }
}
