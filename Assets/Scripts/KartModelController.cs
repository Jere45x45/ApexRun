using UnityEngine;

public class KartModelController : MonoBehaviour
{
    [Header("Model Slots")]
    [SerializeField] private ModelSlot engineSlot;
    [SerializeField] private ModelSlot chassisSlot;

    [Header("Wheel Model Slots")]
    [SerializeField] private ModelSlot frontLeftWheelSlot;
    [SerializeField] private ModelSlot frontRightWheelSlot;
    [SerializeField] private ModelSlot rearLeftWheelSlot;
    [SerializeField] private ModelSlot rearRightWheelSlot;

    public void Refresh(RuntimeKartConfiguration configuration)
    {
        if (configuration == null)
        {
            Debug.LogError(
                "KartModelController recibió una configuración nula.",
                this
            );

            return;
        }

        if (configuration.Engine != null)
        {
            engineSlot.SetModel(
                configuration.Engine.modelPrefab
            );
        }

        if (configuration.Chassis != null)
        {
            chassisSlot.SetModel(
                configuration.Chassis.modelPrefab
            );
        }

        if (configuration.Wheels != null)
        {
            GameObject wheelPrefab =
                configuration.Wheels.modelPrefab;

            frontLeftWheelSlot.SetModel(wheelPrefab);
            frontRightWheelSlot.SetModel(wheelPrefab);
            rearLeftWheelSlot.SetModel(wheelPrefab);
            rearRightWheelSlot.SetModel(wheelPrefab);
        }
    }
}