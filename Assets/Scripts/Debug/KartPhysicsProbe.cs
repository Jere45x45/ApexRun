using System.Text;
using UnityEngine;

/// <summary>
/// Herramienta de diagnóstico (solo para escenas de prueba).
/// Registra los picos físicos del kart durante una pasada:
/// velocidad vertical, altura sobre el piso, inclinación,
/// velocidad angular, fuerza de las ruedas y tiempo en el aire.
/// No modifica el comportamiento del kart: solo mide.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class KartPhysicsProbe : MonoBehaviour
{
    [SerializeField] private WheelCollider[] wheels;

    private Rigidbody rb;
    private bool recording;
    private float currentAirTime;
    private float runStartTime;
    private float launchSpeed;

    public float MaxUpwardVelocity { get; private set; }
    public float MaxGroundClearance { get; private set; }
    public float MaxTiltAngle { get; private set; }
    public float MaxAngularSpeed { get; private set; }
    public float MaxWheelForce { get; private set; }
    public float LongestAirTime { get; private set; }
    public float MinSpeed { get; private set; }
    public float MaxSpeed { get; private set; }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        if (wheels == null || wheels.Length == 0)
        {
            wheels = GetComponentsInChildren<WheelCollider>();
        }
    }

    /// <summary>Coloca el kart quieto, mirando hacia +Z.</summary>
    public void PlaceAt(Vector3 position)
    {
        recording = false;

        transform.SetPositionAndRotation(position, Quaternion.identity);
        rb.position = position;
        rb.rotation = Quaternion.identity;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    /// <summary>Le da velocidad hacia adelante y empieza a medir.</summary>
    public void Launch(float speed)
    {
        launchSpeed = speed;

        rb.linearVelocity = transform.forward * speed;
        rb.angularVelocity = Vector3.zero;

        MaxUpwardVelocity = 0f;
        MaxGroundClearance = 0f;
        MaxTiltAngle = 0f;
        MaxAngularSpeed = 0f;
        MaxWheelForce = 0f;
        LongestAirTime = 0f;
        MinSpeed = float.MaxValue;
        MaxSpeed = 0f;

        currentAirTime = 0f;
        runStartTime = Time.time;
        recording = true;
    }

    private void FixedUpdate()
    {
        if (!recording)
            return;

        Vector3 velocity = rb.linearVelocity;

        MaxUpwardVelocity = Mathf.Max(MaxUpwardVelocity, velocity.y);
        MaxAngularSpeed = Mathf.Max(MaxAngularSpeed, rb.angularVelocity.magnitude);
        MaxTiltAngle = Mathf.Max(MaxTiltAngle, Vector3.Angle(transform.up, Vector3.up));

        float horizontalSpeed = new Vector3(velocity.x, 0f, velocity.z).magnitude;
        MinSpeed = Mathf.Min(MinSpeed, horizontalSpeed);
        MaxSpeed = Mathf.Max(MaxSpeed, horizontalSpeed);

        MaxGroundClearance = Mathf.Max(MaxGroundClearance, MeasureGroundClearance());

        bool anyWheelGrounded = false;

        foreach (WheelCollider wheel in wheels)
        {
            if (wheel != null && wheel.GetGroundHit(out WheelHit hit))
            {
                anyWheelGrounded = true;
                MaxWheelForce = Mathf.Max(MaxWheelForce, hit.force);
            }
        }

        if (anyWheelGrounded)
        {
            currentAirTime = 0f;
        }
        else
        {
            currentAirTime += Time.fixedDeltaTime;
            LongestAirTime = Mathf.Max(LongestAirTime, currentAirTime);
        }
    }

    /// <summary>Distancia entre el centro de masa y el piso que tiene debajo.</summary>
    private float MeasureGroundClearance()
    {
        Vector3 origin = rb.worldCenterOfMass + Vector3.up * 5f;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 50f);

        float closest = float.MaxValue;

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.attachedRigidbody == rb)
                continue;

            closest = Mathf.Min(closest, hit.distance);
        }

        if (closest == float.MaxValue)
            return 0f;

        return Mathf.Max(0f, closest - 5f);
    }

    public string GetReport()
    {
        StringBuilder sb = new StringBuilder();

        sb.Append($"launch={launchSpeed:F1} m/s ");
        sb.Append($"t={Time.time - runStartTime:F1}s ");
        sb.Append($"maxVy={MaxUpwardVelocity:F2} m/s ");
        sb.Append($"maxClearance={MaxGroundClearance:F2} m ");
        sb.Append($"maxTilt={MaxTiltAngle:F0}° ");
        sb.Append($"maxAngVel={MaxAngularSpeed:F1} rad/s ");
        sb.Append($"maxWheelForce={MaxWheelForce:F0} N ");
        sb.Append($"airTime={LongestAirTime:F2}s ");
        sb.Append($"speed {MinSpeed:F1}..{MaxSpeed:F1} m/s ");
        sb.Append($"pos={transform.position:F1}");

        return sb.ToString();
    }
}
