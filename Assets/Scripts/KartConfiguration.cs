using UnityEngine;

[CreateAssetMenu(
    fileName = "New Kart Configuration",
    menuName = "Kart/Kart Configuration"
)]
public class KartConfiguration : ScriptableObject
{
    [Header("Piezas")]
    public EngineData engine;

    public ChassisData chassis;

    public WheelData wheels;

    public AeroKitData aeroKit;

    private void OnValidate()
    {
        if (engine == null)
        {
            Debug.LogWarning(
                $"{name}: No hay un Engine asignado.",
                this
            );
        }

        if (chassis == null)
        {
            Debug.LogWarning(
                $"{name}: No hay un Chassis asignado.",
                this
            );
        }

        if (wheels == null)
        {
            Debug.LogWarning(
                $"{name}: No hay Wheels asignadas.",
                this
            );
        }

        if (aeroKit == null)
        {
            Debug.LogWarning(
                $"{name}: No hay un Aero Kit asignado.",
                this
            );
        }
    }
}