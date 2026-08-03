using UnityEngine;

public class KartModelController : MonoBehaviour
{
    [Header("Puntos de montaje")]

    [SerializeField] private Transform engineMount;
    [SerializeField] private Transform chassisMount;
    [SerializeField] private Transform wheelsMount;

    private GameObject currentEngine;
    private GameObject currentChassis;
    private GameObject currentWheels;

    public void Refresh(RuntimeKartConfiguration configuration)
    {
        UpdateEngine(configuration.Engine);
        UpdateChassis(configuration.Chassis);
        UpdateWheels(configuration.Wheels);
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