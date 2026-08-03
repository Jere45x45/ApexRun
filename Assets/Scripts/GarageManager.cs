using UnityEngine;

public class GarageManager : MonoBehaviour
{
    [SerializeField] private KartBehaviour kartBehaviour;
    
    public void InstallPart(KartPart part)
    {
        if (part == null)
            return;
    
        part.Install(kartBehaviour.Kart.Configuration);
        
        kartBehaviour.RefreshKart();
    }
}