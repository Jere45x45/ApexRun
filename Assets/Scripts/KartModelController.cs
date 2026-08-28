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

    [Header("Aero Kit")]
    [SerializeField] private ModelSlot aeroKitSlot;

    public ModelSlot FrontLeftWheelSlot =>
        frontLeftWheelSlot;

    public ModelSlot FrontRightWheelSlot =>
        frontRightWheelSlot;

    public ModelSlot RearLeftWheelSlot =>
        rearLeftWheelSlot;

    public ModelSlot RearRightWheelSlot =>
        rearRightWheelSlot;

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

        if (engineSlot != null)
        {
            GameObject enginePrefab =
                configuration.Engine != null
                    ? configuration.Engine.modelPrefab
                    : null;

            engineSlot.SetModel(enginePrefab);
        }

        if (chassisSlot != null)
        {
            GameObject chassisPrefab =
                configuration.Chassis != null
                    ? configuration.Chassis.modelPrefab
                    : null;

            chassisSlot.SetModel(chassisPrefab);
        }

        GameObject wheelPrefab =
            configuration.Wheels != null
                ? configuration.Wheels.modelPrefab
                : null;

        if (frontLeftWheelSlot != null)
        {
            frontLeftWheelSlot.SetModel(wheelPrefab);
        }

        if (frontRightWheelSlot != null)
        {
            frontRightWheelSlot.SetModel(wheelPrefab);
        }

        if (rearLeftWheelSlot != null)
        {
            rearLeftWheelSlot.SetModel(wheelPrefab);
        }

        if (rearRightWheelSlot != null)
        {
            rearRightWheelSlot.SetModel(wheelPrefab);
        }

        if (aeroKitSlot != null)
        {
            GameObject aeroKitPrefab =
                configuration.AeroKit != null
                    ? configuration.AeroKit.modelPrefab
                    : null;

            aeroKitSlot.SetModel(aeroKitPrefab);
        }
    }
}