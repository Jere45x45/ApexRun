using UnityEngine;

[CreateAssetMenu(fileName = "New Wheels", menuName = "Kart/Wheels")]
public class WheelData : KartPart
{
    [Header("Dirección")]
    public float maxSteeringAngle = 30f;
    public float minSteeringAngle = 10f;
    public float steeringReductionSpeed = 20f;

    [Header("Frenado")]
    public float brakeTorque = 3000f;

    [Header("Suspensión")]
    [Min(0f)]
    public float suspensionDistance = 0.2f;

    [Min(0f)]
    public float springRate = 20000f;

    [Min(0f)]
    public float damperRate = 4000f;

    [Range(0f, 1f)]
    public float suspensionTargetPosition = 0.5f;

    [Header("Rueda")]
    [Min(0.001f)]
    public float radius = 0.25f;

    [Header("Física nueva (Fase 0)")]
    [Tooltip("Neumático de la física nueva: radio, rigidez, agarre (Pacejka) y deformación. " +
             "Cuando Race pase a la física nueva, reemplaza a la suspensión, el radio y las curvas de fricción.")]
    public TireSettings tireSettings = new TireSettings();

    [Tooltip("Inercia de giro de cada rueda con su llanta (kg·m²).")]
    [Min(0.001f)]
    public float wheelInertia = 0.04f;

    [Header("Fricción - Delanteras")]
    public WheelFrictionCurve frontForwardFriction = new WheelFrictionCurve
    {
        extremumSlip = 0.4f,
        extremumValue = 1f,
        asymptoteSlip = 0.8f,
        asymptoteValue = 0.5f,
        stiffness = 1f
    };

    public WheelFrictionCurve frontSidewaysFriction = new WheelFrictionCurve
    {
        extremumSlip = 0.2f,
        extremumValue = 1f,
        asymptoteSlip = 0.5f,
        asymptoteValue = 0.75f,
        stiffness = 1.3f
    };

    [Header("Fricción - Traseras")]
    public WheelFrictionCurve rearForwardFriction = new WheelFrictionCurve
    {
        extremumSlip = 0.4f,
        extremumValue = 1f,
        asymptoteSlip = 0.8f,
        asymptoteValue = 0.5f,
        stiffness = 1f
    };

    public WheelFrictionCurve rearSidewaysFriction = new WheelFrictionCurve
    {
        extremumSlip = 0.19f,
        extremumValue = 1f,
        asymptoteSlip = 0.45f,
        asymptoteValue = 0.68f,
        stiffness = 1.05f
    };

    public override PartType PartType => PartType.Wheels;

    public override void Apply(KartStats stats)
    {
        stats.maxSteeringAngle = maxSteeringAngle;
        stats.minSteeringAngle = minSteeringAngle;
        stats.steeringReductionSpeed = steeringReductionSpeed;

        stats.brakeTorque = brakeTorque;

        stats.wheelRadius = radius;
        stats.suspensionDistance = suspensionDistance;
        stats.springRate = springRate;
        stats.damperRate = damperRate;
        stats.suspensionTargetPosition = suspensionTargetPosition;

        stats.frontForwardFriction = frontForwardFriction;
        stats.frontSidewaysFriction = frontSidewaysFriction;
        stats.rearForwardFriction = rearForwardFriction;
        stats.rearSidewaysFriction = rearSidewaysFriction;

        // Copia: la física no tiene que poder modificar el asset.
        stats.tire = JsonUtility.FromJson<TireSettings>(JsonUtility.ToJson(tireSettings));
        stats.wheelInertia = wheelInertia;
    }

    public override void Install(RuntimeKartConfiguration configuration)
    {
        configuration.InstallWheels(this);
    }
}