using UnityEngine;

public class KartFrictionController
{
    private readonly WheelCollider[] wheels;
    
    // Guardamos la rigidez (stiffness) base. En Unity por defecto es 1.
    private readonly float baseForwardStiffness = 1f;
    private readonly float baseSidewaysStiffness = 1f;

    public KartFrictionController(KartPhysics physics)
    {
        wheels = new WheelCollider[]
        {
            physics.FrontLeftWheel,
            physics.FrontRightWheel,
            physics.RearLeftWheel,
            physics.RearRightWheel
        };
    }

    public void UpdateFriction()
    {
        // Si no hay gestor de clima, asumimos agarre perfecto
        float gripMultiplier = 1f;
        
        if (WeatherManager.Instance != null)
        {
            gripMultiplier = WeatherManager.Instance.CurrentGripMultiplier;
        }

        foreach (WheelCollider wheel in wheels)
        {
            // Modificamos la fricción frontal (aceleración/frenado)
            WheelFrictionCurve forward = wheel.forwardFriction;
            forward.stiffness = baseForwardStiffness * gripMultiplier;
            wheel.forwardFriction = forward;

            // Modificamos la fricción lateral (derrape en curvas)
            WheelFrictionCurve sideways = wheel.sidewaysFriction;
            sideways.stiffness = baseSidewaysStiffness * gripMultiplier;
            wheel.sidewaysFriction = sideways;
        }
    }
}
