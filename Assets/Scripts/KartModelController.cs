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

    [Header("Volante")]
    [Tooltip("Slot del volante. Su Mount Point tiene que estar en el origen del kart y sin rotación: " +
             "los modelos de volante vienen ubicados en coordenadas del kart, y es el Mount Point " +
             "el que gira cuando el piloto dobla.")]
    [SerializeField] private ModelSlot steeringWheelSlot;

    [Tooltip("Grados que gira el volante por cada grado que giran las ruedas delanteras. " +
             "En un kart, el volante gira ~90° para ~25° de las ruedas.")]
    [SerializeField, Min(0f)] private float steeringWheelRatio = 3.6f;

    private Vector3 steeringWheelHub;
    private Vector3 steeringWheelAxis = Vector3.back;

    public ModelSlot FrontLeftWheelSlot =>
        frontLeftWheelSlot;

    public ModelSlot FrontRightWheelSlot =>
        frontRightWheelSlot;

    public ModelSlot RearLeftWheelSlot =>
        rearLeftWheelSlot;

    public ModelSlot RearRightWheelSlot =>
        rearRightWheelSlot;

    public ModelSlot SteeringWheelSlot =>
        steeringWheelSlot;

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

        if (steeringWheelSlot != null)
        {
            SteeringWheelData steeringWheel = configuration.SteeringWheel;

            steeringWheelSlot.SetModel(
                steeringWheel != null
                    ? steeringWheel.modelPrefab
                    : null
            );

            if (steeringWheel != null)
            {
                steeringWheelHub = steeringWheel.hubPosition;
                steeringWheelAxis = steeringWheel.ColumnAxis;
            }

            SetSteeringAngle(0f);
        }
    }

    /// <summary>
    /// Gira el volante según el ángulo de las ruedas delanteras (grados,
    /// positivo = hacia la derecha). Gira alrededor del centro del volante,
    /// sobre el eje de la columna: a la derecha, en el sentido de las agujas
    /// del reloj visto por el piloto.
    /// </summary>
    public void SetSteeringAngle(float roadWheelAngle)
    {
        if (steeringWheelSlot == null || steeringWheelSlot.MountPoint == null)
            return;

        Transform mount = steeringWheelSlot.MountPoint;
        Quaternion rotation = Quaternion.AngleAxis(roadWheelAngle * steeringWheelRatio, steeringWheelAxis);

        mount.localRotation = rotation;
        mount.localPosition = steeringWheelHub - rotation * steeringWheelHub;
    }
}
