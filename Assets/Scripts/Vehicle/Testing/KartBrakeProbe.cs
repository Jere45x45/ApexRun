using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Banco de pruebas de frenado con el kart del juego (KartBehaviour, con sus
/// ayudas). Para cada caso de la lista:
/// 1. Acelera en recta hasta la velocidad del caso.
/// 2. Gira el volante (si el caso lo pide) y sostiene la velocidad un rato,
///    para entrar en la curva.
/// 3. Suelta el acelerador y frena a fondo (como la barra en el teclado),
///    sin soltar el volante, hasta quedar casi quieto.
/// Anota la deriva máxima (ángulo entre hacia dónde apunta y hacia dónde va),
/// si hizo trompo, cuánto tardó y cuánto recorrió frenando.
/// El resultado queda en Report (y en la consola). Se agrega al kart en Play.
/// </summary>
public class KartBrakeProbe : MonoBehaviour
{
    [Tooltip("Velocidad de cada caso (km/h).")]
    public float[] speedsKmh = { 60f, 60f, 45f, 30f };

    [Tooltip("Volante de cada caso (−1 a 1).")]
    public float[] steerInputs = { 0f, 0.4f, 0.7f, 1f };

    [Tooltip("Tiempo doblando antes de frenar (s).")]
    public float steerLeadTime = 0.6f;

    [Tooltip("Deriva desde la que se cuenta como trompo (grados).")]
    public float spinAngle = 45f;

    public bool Done { get; private set; }

    public string Report => report.ToString();

    private readonly StringBuilder report = new StringBuilder();

    private enum Phase { Accelerate, Steer, Brake }

    private KartBehaviour kart;
    private Rigidbody body;
    private Vector3 startPosition;
    private Quaternion startRotation;

    private int index;
    private Phase phase;
    private float phaseTime;
    private float stableTime;

    private float maxSlip;
    private float distance;
    private float heading;
    private float minRearSlipRatio;

    private void Start()
    {
        kart = GetComponent<KartBehaviour>();
        body = GetComponent<Rigidbody>();
        startPosition = body.position;
        startRotation = body.rotation;

        kart.SetInputEnabled(true);

        report.AppendLine("km/h | volante | deriva máx ° | trompo | tiempo s | distancia m | giró ° | patinamiento tras. mín");
    }

    private void FixedUpdate()
    {
        if (Done || kart.Vehicle == null || !kart.Vehicle.IsConfigured)
            return;

        float dt = Time.fixedDeltaTime;
        float target = speedsKmh[index] / 3.6f;
        float speed = kart.Vehicle.ForwardSpeed;
        float steer = steerInputs[index];
        float hold = Mathf.Clamp((target - speed) * 1.5f + 0.2f, -1f, 1f);

        phaseTime += dt;

        switch (phase)
        {
            case Phase.Accelerate:
                kart.SetInputs(hold, 0f, false);
                stableTime = Mathf.Abs(speed - target) < 0.3f ? stableTime + dt : 0f;

                if (stableTime > 0.5f || phaseTime > 25f)
                    SetPhase(Phase.Steer);
                break;

            case Phase.Steer:
                kart.SetInputs(hold, steer, false);

                if (phaseTime >= steerLeadTime)
                {
                    SetPhase(Phase.Brake);
                    maxSlip = 0f;
                    distance = 0f;
                    heading = 0f;
                    minRearSlipRatio = 0f;
                }
                break;

            case Phase.Brake:
                kart.SetInputs(0f, steer, true);

                Vector3 velocity = body.linearVelocity;
                float planarSpeed = new Vector2(velocity.x, velocity.z).magnitude;

                if (planarSpeed > 2f)
                {
                    Vector3 forward = body.rotation * Vector3.forward;
                    float slip = Vector3.Angle(new Vector3(forward.x, 0f, forward.z), new Vector3(velocity.x, 0f, velocity.z));
                    maxSlip = Mathf.Max(maxSlip, slip);
                }

                distance += planarSpeed * dt;
                heading += Mathf.Abs(Vector3.Dot(body.angularVelocity, body.rotation * Vector3.up)) * Mathf.Rad2Deg * dt;

                KartWheel left = kart.Vehicle.Wheels[KartVehicle.RearLeft];
                KartWheel right = kart.Vehicle.Wheels[KartVehicle.RearRight];
                minRearSlipRatio = Mathf.Min(minRearSlipRatio, Mathf.Max(left.SlipRatio, right.SlipRatio));

                if (planarSpeed < 1f || phaseTime > 8f)
                {
                    WriteLine();
                    Next();
                }
                break;
        }
    }

    private void SetPhase(Phase next)
    {
        phase = next;
        phaseTime = 0f;
        stableTime = 0f;
    }

    private void WriteLine()
    {
        string line = string.Format(CultureInfo.InvariantCulture,
            "{0,4:0} | {1,4:0.0} | {2,6:0.0} | {3} | {4,5:0.00} | {5,6:0.0} | {6,5:0} | {7,5:0.00}",
            speedsKmh[index], steerInputs[index], maxSlip, maxSlip >= spinAngle ? "SÍ" : "no",
            phaseTime, distance, heading, minRearSlipRatio);

        report.AppendLine(line);
        Debug.Log("FRENO: " + line);
    }

    private void Next()
    {
        index++;
        SetPhase(Phase.Accelerate);

        kart.SetInputs(0f, 0f, false);

        body.position = startPosition;
        body.rotation = startRotation;
        transform.SetPositionAndRotation(startPosition, startRotation);
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        kart.ResetMotion();

        if (index >= speedsKmh.Length || index >= steerInputs.Length)
        {
            Done = true;
            Debug.Log("FRENO: listo\n" + report);
        }
    }
}
