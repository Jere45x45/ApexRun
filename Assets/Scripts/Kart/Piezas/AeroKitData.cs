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

    [Header("Física nueva (Fase 0)")]
    [Tooltip("Resistencia y carga aerodinámica del kart con piloto y este kit. " +
             "Cuando Race pase a la física nueva, reemplaza a downforce y aerodynamicDrag.")]
    public AeroSettings aeroSettings = new AeroSettings();

    public override PartType PartType => PartType.AeroKit;

    public override void Apply(KartStats stats)
    {
        stats.downforce = downforce;
        stats.aerodynamicDrag = aerodynamicDrag;

        // Copia: la física no tiene que poder modificar el asset.
        stats.aero = JsonUtility.FromJson<AeroSettings>(JsonUtility.ToJson(aeroSettings));
    }

    public override void Install(
        RuntimeKartConfiguration configuration)
    {
        configuration.InstallAeroKit(this);
    }
}