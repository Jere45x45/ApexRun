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
            kartBehaviour =
                GetComponent<KartBehaviour>();
        }
    }

    private void Update()
    {
        if (kartBehaviour == null)
            return;

        KartPhysics physics =
            kartBehaviour.Physics;

        if (physics == null)
            return;

        UpdateWheel(
            "FL",
            physics.FrontLeftWheel,
            ref frontLeftSurface
        );

        UpdateWheel(
            "FR",
            physics.FrontRightWheel,
            ref frontRightSurface
        );

        UpdateWheel(
            "RL",
            physics.RearLeftWheel,
            ref rearLeftSurface
        );

        UpdateWheel(
            "RR",
            physics.RearRightWheel,
            ref rearRightSurface
        );
    }

    private void UpdateWheel(
        string wheelName,
        WheelPhysics wheel,
        ref string previousSurface)
    {
        if (wheel == null)
            return;

        string currentSurface =
            GetSurfaceName(wheel);

        if (logSurfaceChanges &&
            currentSurface != previousSurface)
        {
            Debug.Log(
                $"{wheelName} → {currentSurface}",
                this
            );

            previousSurface =
                currentSurface;
        }

        if (!drawRays)
            return;

        Vector3 origin =
            wheel.WheelPoint.position;

        Vector3 direction =
            -wheel.WheelPoint.up;

        float length =
            wheel.RayLength;

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

        Debug.DrawRay(
            origin,
            direction * length,
            rayColor
        );
    }

    private string GetSurfaceName(
        WheelPhysics wheel)
    {
        if (!wheel.IsGrounded)
            return "AIR";

        if (!wheel.HasSurface)
            return "UNKNOWN";

        if (wheel.CurrentSurface == null)
            return "UNKNOWN";

        return wheel.CurrentSurface.SurfaceName;
    }
}