using UnityEngine;

public class KartModelController : MonoBehaviour
{
    [Header("Model Slots")]
    [SerializeField] private ModelSlot engineSlot;
    [SerializeField] private ModelSlot chassisSlot;
    [SerializeField] private ModelSlot wheelsSlot;

    public void Refresh(RuntimeKartConfiguration configuration)
    {
        engineSlot.SetModel(configuration.Engine.modelPrefab);
        chassisSlot.SetModel(configuration.Chassis.modelPrefab);
        wheelsSlot.SetModel(configuration.Wheels.modelPrefab);
    }
}