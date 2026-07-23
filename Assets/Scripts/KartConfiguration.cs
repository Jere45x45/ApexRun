using UnityEngine;

[CreateAssetMenu(fileName = "New Kart Configuration", menuName = "Kart/Kart Configuration")]
public class KartConfiguration : ScriptableObject
{
    public EngineData engine;
    public ChassisData chassis;
    public WheelData wheels;

    // Más adelante:
    // public AccessoryData accessory;
}