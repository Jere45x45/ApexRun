using UnityEngine;

public class KartModelController : MonoBehaviour
{
    [Header("Puntos de montaje")]

    [SerializeField] private Transform engineMount;
    [SerializeField] private Transform chassisMount;
    [SerializeField] private Transform wheelsMount;

    public void Refresh(RuntimeKartConfiguration configuration)
    {
        engineSlot.SetModel(configuration.Engine.modelPrefab);
        chassisSlot.SetModel(configuration.Chassis.modelPrefab);
        wheelsSlot.SetModel(configuration.Wheels.modelPrefab);
    }
    

    private void UpdateEngine(EngineData engine)
    {
        ReplaceModel(ref currentEngine, engine.modelPrefab, engineMount);
    }

    private void UpdateChassis(ChassisData chassis)
    {
        ReplaceModel(ref currentChassis, chassis.modelPrefab, chassisMount);
    }

    private void UpdateWheels(WheelData wheels)
    {
        ReplaceModel(ref currentWheels, wheels.modelPrefab, wheelsMount);
    }

    private void ReplaceModel(
        ref GameObject current,
        GameObject prefab,
        Transform parent)
    {
        if (current != null)
            Destroy(current);

        if (prefab != null)
            current = Instantiate(prefab, parent);
    }
}