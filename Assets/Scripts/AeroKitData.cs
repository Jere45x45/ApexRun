using UnityEngine;

[CreateAssetMenu(
    fileName = "New Aero Kit",
    menuName = "Kart/Aero Kit"
)]
public class AeroKitData : KartPart
{
    [Header("Aerodinámica")]
    [Min(0f)]
    public float downforce = 0f;

    [Min(0f)]
    public float aerodynamicDrag = 0f;

    public override PartType PartType => PartType.AeroKit;

    public override void Apply(KartStats stats)
    {
        stats.downforce = downforce;
        stats.aerodynamicDrag = aerodynamicDrag;
    }

    public override void Install(
        RuntimeKartConfiguration configuration)
    {
        configuration.InstallAeroKit(this);
    }
}