using UnityEngine;

[CreateAssetMenu(
    fileName = "New Aero Kit",
    menuName = "Kart/Aero Kit"
)]
public class AeroKitData : KartPart
{
    public override PartType PartType => PartType.AeroKit;

    public override void Apply(KartStats stats)
    {
        // Por ahora el kit aero modifica solamente el aspecto visual.
        // Las estadísticas aerodinámicas se agregarán cuando definamos
        // qué parámetros concretos tendrá cada kit.
    }

    public override void Install(RuntimeKartConfiguration configuration)
    {
        configuration.InstallAeroKit(this);
    }
}