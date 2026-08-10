using UnityEngine;

public class KartWorkshopManager : MonoBehaviour
{
    [SerializeField] private KartBehavior kartBehavior;

    public void InstallPart(KartPart part)
    {
        if (part == null)
            return;

        part.Install(kartBehavior.Kart.Configuration);

        kartBehavior.RefreshKart();
    }
}