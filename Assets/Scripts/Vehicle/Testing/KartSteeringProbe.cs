using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Banco de pruebas del giro con el kart del juego (KartBehaviour, con su
/// volante y sus ayudas). Para cada velocidad de la lista:
/// 1. Acelera en recta hasta esa velocidad.
/// 2. Dobla con el volante a fondo, manteniendo la velocidad con el acelerador.
/// 3. Mide el giro ya estable y vuelve al punto de partida para la siguiente.
/// El resultado queda en Report (y en la consola). Se agrega al kart en Play.
/// </summary>
public class KartSteeringProbe : MonoBehaviour
{
    [Tooltip("Velocidades a probar (km/h).")]
    public float[] speedsKmh = { 5f, 10f, 15f, 20f, 30f, 45f, 60f };

    [Tooltip("Volante (−1 a 1). 1 = a fondo a la derecha.")]
    public float steerInput = 1f;

    [Tooltip("Tiempo doblando antes de medir (s).")]
    public float settleTime = 2f;

    [Tooltip("Tiempo que se promedia (s).")]
    public float measureTime = 2f;

    public bool Done { get; private set; }

    public string Report => report.ToString();

    private readonly StringBuilder report = new StringBuilder();

    private KartBehaviour kart;
    private Rigidbody body;
    private Vector3 startPosition;
    private Quaternion startRotation;

    private int index;
    private bool turning;
    private float phaseTime;
    private float stableTime;

    private float sumYaw;
    private float sumSpeed;
    private float sumSteer;
    private float sumThrottle;
    private float minRearInnerLoad;
    private int samples;

    private void Start()
    {
        kart = GetComponent<KartBehaviour>();
        body = GetComponent<Rigidbody>();
        startPosition = body.position;
        startRotation = body.rotation;

        kart.SetInputEnabled(true);

        report.AppendLine("km/h obj | km/h real | volante° | giro °/s | radio m | radio geom. m | acelerador | carga tras. int. N");
    }

    private void FixedUpdate()
    {
        if (Done || kart.Vehicle == null || !kart.Vehicle.IsConfigured)
            return;

        float dt = Time.fixedDeltaTime;
        float target = speedsKmh[index] / 3.6f;
        float speed = kart.Vehicle.ForwardSpeed;

        // Acelerador para sostener la velocidad (S = freno).
        float throttle = Mathf.Clamp((target - speed) * 1.5f + (turning ? 0.25f : 0.1f), -1f, 1f);

        if (!turning)
        {
            kart.SetInputs(throttle, 0f, false);

            stableTime = Mathf.Abs(speed - target) < 0.25f ? stableTime + dt : 0f;
            phaseTime += dt;

            if (stableTime > 0.5f || phaseTime > 20f)
            {
                turning = true;
                phaseTime = 0f;
                ResetSums();
            }

            return;
        }

        kart.SetInputs(throttle, steerInput, false);
        phaseTime += dt;

        if (phaseTime < settleTime)
            return;

        if (phaseTime < settleTime + measureTime)
        {
            sumYaw += Mathf.Abs(body.angularVelocity.y) * Mathf.Rad2Deg;
            sumSpeed += Mathf.Abs(speed);
            sumSteer += Mathf.Abs(kart.SteerAngle);
            sumThrottle += Mathf.Clamp01(throttle);

            KartWheel rearInner = kart.Vehicle.Wheels[steerInput > 0f ? KartVehicle.RearRight : KartVehicle.RearLeft];
            minRearInnerLoad = Mathf.Min(minRearInnerLoad, rearInner.VerticalLoad);

            samples++;
            return;
        }

        WriteLine();
        Next();
    }

    private void WriteLine()
    {
        float yaw = sumYaw / samples;
        float speed = sumSpeed / samples;
        float steer = sumSteer / samples;
        float radius = yaw > 0.01f ? speed / (yaw * Mathf.Deg2Rad) : float.PositiveInfinity;
        float geometricRadius = kart.Vehicle.Steering.Wheelbase / Mathf.Tan(Mathf.Max(0.01f, steer) * Mathf.Deg2Rad);

        string line = string.Format(CultureInfo.InvariantCulture,
            "{0,5:0} | {1,6:0.0} | {2,6:0.0} | {3,6:0.0} | {4,6:0.0} | {5,6:0.0} | {6,4:0.00} | {7,5:0}",
            speedsKmh[index], speed * 3.6f, steer, yaw, radius, geometricRadius, sumThrottle / samples, minRearInnerLoad);

        report.AppendLine(line);
        Debug.Log("GIRO: " + line);
    }

    private void Next()
    {
        index++;
        turning = false;
        phaseTime = 0f;
        stableTime = 0f;

        kart.SetInputs(0f, 0f, false);

        body.position = startPosition;
        body.rotation = startRotation;
        transform.SetPositionAndRotation(startPosition, startRotation);
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        kart.ResetMotion();

        if (index >= speedsKmh.Length)
        {
            Done = true;
            Debug.Log("GIRO: listo\n" + report);
        }
    }

    private void ResetSums()
    {
        sumYaw = 0f;
        sumSpeed = 0f;
        sumSteer = 0f;
        sumThrottle = 0f;
        minRearInnerLoad = float.MaxValue;
        samples = 0;
    }
}
