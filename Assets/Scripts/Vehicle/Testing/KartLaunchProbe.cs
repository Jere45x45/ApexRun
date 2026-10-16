using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Banco de pruebas de arranque doblando, con el kart del juego (KartBehaviour).
/// Para cada volante de la lista: parte quieto, acelera a fondo con ese volante
/// y anota cada segundo la velocidad, cuánto giró el kart y cuánto avanzó.
/// El resultado queda en Report (y en la consola). Se agrega al kart en Play.
/// </summary>
public class KartLaunchProbe : MonoBehaviour
{
    [Tooltip("Volantes a probar (−1 a 1). 1 = a fondo a la derecha.")]
    public float[] steerInputs = { 1f, 0.6f, 0f };

    [Tooltip("Acelerador (0 a 1).")]
    public float throttleInput = 1f;

    [Tooltip("Duración de cada arranque (s).")]
    public float duration = 4f;

    public bool Done { get; private set; }

    public string Report => report.ToString();

    private readonly StringBuilder report = new StringBuilder();

    private KartBehaviour kart;
    private Rigidbody body;
    private Vector3 startPosition;
    private Quaternion startRotation;

    private int index;
    private float time;
    private float heading;
    private float distance;
    private float maxYaw;
    private int nextMark = 1;

    private void Start()
    {
        kart = GetComponent<KartBehaviour>();
        body = GetComponent<Rigidbody>();
        startPosition = body.position;
        startRotation = body.rotation;

        kart.SetInputEnabled(true);
        Restart();

        report.AppendLine("volante | s | km/h | giró ° | avanzó m | giro máx °/s");
    }

    private void FixedUpdate()
    {
        if (Done || kart.Vehicle == null || !kart.Vehicle.IsConfigured)
            return;

        float dt = Time.fixedDeltaTime;
        kart.SetInputs(throttleInput, steerInputs[index], false);

        float yaw = Mathf.Abs(Vector3.Dot(body.angularVelocity, body.rotation * Vector3.up)) * Mathf.Rad2Deg;
        heading += yaw * dt;
        distance += body.linearVelocity.magnitude * dt;
        maxYaw = Mathf.Max(maxYaw, yaw);
        time += dt;

        if (time + 0.0001f < nextMark)
            return;

        string line = string.Format(CultureInfo.InvariantCulture,
            "{0,5:0.0} | {1} | {2,5:0.0} | {3,5:0} | {4,5:0.0} | {5,4:0}",
            steerInputs[index], nextMark, kart.Vehicle.ForwardSpeed * 3.6f, heading, distance, maxYaw);

        report.AppendLine(line);
        Debug.Log("ARRANQUE: " + line);
        nextMark++;

        if (nextMark <= duration)
            return;

        index++;

        if (index >= steerInputs.Length)
        {
            Done = true;
            kart.SetInputs(0f, 0f, false);
            Debug.Log("ARRANQUE: listo\n" + report);
            return;
        }

        Restart();
    }

    private void Restart()
    {
        body.position = startPosition;
        body.rotation = startRotation;
        transform.SetPositionAndRotation(startPosition, startRotation);
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        kart.ResetMotion();

        time = 0f;
        heading = 0f;
        distance = 0f;
        maxYaw = 0f;
        nextMark = 1;
    }
}
