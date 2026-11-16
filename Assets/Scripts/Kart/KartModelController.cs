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
             "el que los lleva a la columna del chasis (ChassisData) y los gira cuando el piloto dobla.")]
    [SerializeField] private ModelSlot steeringWheelSlot;

    [Tooltip("Grados que gira el volante por cada grado que giran las ruedas delanteras. " +
             "En un kart, el volante gira ~90° para ~25° de las ruedas.")]
    [SerializeField, Min(0f)] private float steeringWheelRatio = 3.6f;

    [Header("Apariencia alternativa (solo bots)")]
    private bool useVisualOverrides;
    private GameObject visualEnginePrefab;
    private GameObject visualWheelsPrefab;
    private bool visualWheelsRimFacesRight = true;
    private GameObject visualAeroPrefab;

    // Dónde está el volante en su modelo (SteeringWheelData) y dónde tiene que
    // ir: la punta de la columna del chasis (ChassisData).
    private Vector3 steeringWheelModelHub;
    private Vector3 steeringWheelHub;
    private Vector3 steeringWheelAxis = Vector3.back;
    private Quaternion steeringWheelAlignment = Quaternion.identity;

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

    /// <summary>Hacia dónde mira la llanta en el modelo de las ruedas instaladas (WheelData.rimFacesRight).</summary>
    public bool WheelRimFacesRight { get; private set; } = true;
    public void SetVisualOverrides(
    KartPart engine,
    KartPart wheels,
    KartPart aero,
    RuntimeKartConfiguration configuration)
    {
    if (configuration == null)
    {
        Debug.LogError(
            "No se puede cambiar la apariencia: configuración nula.",
            this
        );
        return;
    }

    if (engine == null || wheels == null || aero == null)
    {
        Debug.LogError(
            "Falta una pieza visual para el bot.",
            this
        );
        return;
    }

    useVisualOverrides = true;
    visualEnginePrefab = engine.modelPrefab;
    visualWheelsPrefab = wheels.modelPrefab;
    WheelData visualWheelData = wheels as WheelData;
    visualWheelsRimFacesRight = visualWheelData == null || visualWheelData.rimFacesRight;
    visualAeroPrefab = aero.modelPrefab;

    Refresh(configuration);
    }
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
            GameObject enginePrefab = useVisualOverrides
            ? visualEnginePrefab
            : configuration.Engine != null
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

        GameObject wheelPrefab = useVisualOverrides
        ? visualWheelsPrefab
        : configuration.Wheels != null
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

        // Con apariencia al azar (bots), la llanta es la de las ruedas que se ven.
        WheelRimFacesRight = useVisualOverrides
            ? visualWheelsRimFacesRight
            : configuration.Wheels == null || configuration.Wheels.rimFacesRight;

        OrientWheelModel(frontLeftWheelSlot);
        OrientWheelModel(frontRightWheelSlot);
        OrientWheelModel(rearLeftWheelSlot);
        OrientWheelModel(rearRightWheelSlot);

        if (aeroKitSlot != null)
        {
            GameObject aeroKitPrefab = useVisualOverrides
            ? visualAeroPrefab
            : configuration.AeroKit != null
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
                steeringWheelModelHub = steeringWheel.hubPosition;

                // Va a la columna del chasis. Sin chasis, queda donde está en su modelo.
                ChassisData chassis = configuration.Chassis;
                steeringWheelHub = chassis != null ? chassis.steeringWheelHub : steeringWheel.hubPosition;
                steeringWheelAxis = chassis != null ? chassis.SteeringColumnAxis : steeringWheel.ColumnAxis;

                // Gira el volante para que quede perpendicular a la columna del chasis
                // (cada modelo viene con su propia inclinación). Los dos ejes están en el
                // plano vertical del kart, así que solo lo inclina, sin ladearlo.
                steeringWheelAlignment = Quaternion.FromToRotation(steeringWheel.ColumnAxis, steeringWheelAxis);
            }

            SetSteeringAngle(0f);
        }
    }

    /// <summary>
    /// Da vuelta el modelo de una rueda si su llanta quedaría hacia adentro.
    /// El lado sale de dónde está el slot (x negativa = izquierda). En la
    /// carrera, KartWheelVisual mueve las ruedas y hace lo mismo.
    /// </summary>
    private void OrientWheelModel(ModelSlot slot)
    {
        if (slot == null || slot.MountPoint == null || slot.CurrentInstance == null)
            return;

        bool isLeftSide = slot.MountPoint.localPosition.x < 0f;

        slot.CurrentInstance.transform.localRotation = isLeftSide == WheelRimFacesRight
            ? Quaternion.Euler(0f, 180f, 0f)
            : Quaternion.identity;
    }

    /// <summary>
    /// Gira el volante según el ángulo de las ruedas delanteras (grados,
    /// positivo = hacia la derecha). Gira alrededor del centro del volante,
    /// sobre el eje de la columna: a la derecha, en el sentido de las agujas
    /// del reloj visto por el piloto. El centro del volante de su modelo
    /// queda en la punta de la columna del chasis.
    /// </summary>
    public void SetSteeringAngle(float roadWheelAngle)
    {
        if (steeringWheelSlot == null || steeringWheelSlot.MountPoint == null)
            return;

        Transform mount = steeringWheelSlot.MountPoint;
        Quaternion rotation =
            Quaternion.AngleAxis(roadWheelAngle * steeringWheelRatio, steeringWheelAxis) * steeringWheelAlignment;

        mount.localRotation = rotation;
        mount.localPosition = steeringWheelHub - rotation * steeringWheelModelHub;
    }
}
