using UnityEngine;

public class KartFrictionController
{
    private readonly WheelPhysics[] wheels;

    private float gripMultiplier = 1f;

    public float GripMultiplier => gripMultiplier;

    public KartFrictionController(KartPhysics physics)
    {
        if (physics == null)
        {
            Debug.LogError(
                "KartFrictionController recibió un KartPhysics nulo."
            );

            wheels = new WheelPhysics[0];
            return;
        }

        wheels = new WheelPhysics[]
        {
            physics.FrontLeftWheel,
            physics.FrontRightWheel,
            physics.RearLeftWheel,
            physics.RearRightWheel
        };
    }

    public void UpdateFriction()
    {
        gripMultiplier = 1f;

        if (WeatherManager.Instance != null)
        {
            gripMultiplier =
                Mathf.Max(
                    0f,
                    WeatherManager.Instance.CurrentGripMultiplier
                );
        }

        foreach (WheelPhysics wheel in wheels)
        {
            if (wheel == null)
                continue;

            wheel.SetGripMultiplier(
                gripMultiplier
            );
        }
    }
}