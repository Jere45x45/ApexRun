using UnityEngine;

public class KartTrackSurfaceDebugger : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private KartBehaviour kartBehaviour;

    [Header("Debug")]
    [SerializeField] private bool drawRays = true;
    [SerializeField] private bool logSurfaceChanges = true;

    private string frontLeftSurface;
    private string frontRightSurface;
    private string rearLeftSurface;
    private string rearRightSurface;

    private void Awake()
    {
        if (kartBehaviour == null)
        {
            kartBehaviour = GetComponent<KartBehaviour>();
        }
    }

    private void Update()
    {
        if (kartBehaviour == null)
            return;

        KartVehicle vehicle = kartBehaviour.Vehicle;

        if (vehicle == null || !vehicle.IsConfigured)
            return;

        KartWheel[] wheels = vehicle.Wheels;

        UpdateWheel("FL", wheels[KartVehicle.FrontLeft], vehicle, ref frontLeftSurface);
        UpdateWheel("FR", wheels[KartVehicle.FrontRight], vehicle, ref frontRightSurface);
        UpdateWheel("RL", wheels[KartVehicle.RearLeft], vehicle, ref rearLeftSurface);
        UpdateWheel("RR", wheels[KartVehicle.RearRight], vehicle, ref rearRightSurface);
    }

    private void UpdateWheel(
        string wheelName,
        KartWheel wheel,
        KartVehicle vehicle,
        ref string previousSurface)
    {
        if (wheel == null)
            return;

        string currentSurface = GetSurfaceName(wheel);

        if (logSurfaceChanges && currentSurface != previousSurface)
        {
            Debug.Log($"{wheelName} → {currentSurface}", this);
            previousSurface = currentSurface;
        }

        if (!drawRays)
            return;

        // Del centro de la rueda hacia abajo del kart, el largo del radio.
        Rigidbody body = vehicle.Body;
        Vector3 origin = body.position + body.rotation * (wheel.LocalPosition + wheel.GeometryOffset);
        Vector3 direction = body.rotation * Vector3.down;
        float length = vehicle.Tire.radius;

        Color rayColor;

        if (!wheel.IsGrounded)
        {
            rayColor = Color.red;
        }
        else if (wheel.IsOnInvalidSurface)
        {
            rayColor = Color.yellow;
        }
        else
        {
            rayColor = Color.green;
        }

        Debug.DrawRay(origin, direction * length, rayColor);
    }

    private string GetSurfaceName(KartWheel wheel)
    {
        if (!wheel.IsGrounded)
            return "AIR";

        if (wheel.CurrentSurface == null)
            return "UNKNOWN";

        return wheel.CurrentSurface.SurfaceName;
    }
}
