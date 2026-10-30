using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Piloto automático para probar una pista: se agrega en Play al kart del
/// jugador y maneja solo siguiendo la línea central, sin pasarse de la
/// velocidad que permite cada curva. Anota cada checkpoint que cuenta la
/// carrera, cada vuelta y cada reaparición. El resultado queda en Report (y en
/// la consola, con el prefijo "PISTA:").
/// Sirve para comprobar una pista recién preparada (TrackSetup) sin manejar.
/// </summary>
public class TrackLapProbe : MonoBehaviour
{
    [Tooltip("Velocidad máxima (km/h).")]
    public float maxSpeedKmh = 70f;

    [Tooltip("Aceleración lateral que se permite en las curvas (m/s²).")]
    public float lateralGrip = 7f;

    [Tooltip("Cuántos metros hacia adelante mira para doblar, además de 0,6 s de recorrido.")]
    public float lookAhead = 5f;

    [Tooltip("Cuántos metros hacia adelante revisa las curvas para frenar a tiempo.")]
    public float brakingLookAhead = 45f;

    public bool Done { get; private set; }

    public int CheckpointsPassed { get; private set; }

    public int LapsCompleted { get; private set; }

    public int Respawns { get; private set; }

    public string Report => report.ToString();

    private readonly StringBuilder report = new StringBuilder();

    private KartBehaviour kart;
    private Rigidbody body;
    private TrackCenterline centerline;
    private RaceCheckpointManager checkpoints;
    private RaceLapManager laps;
    private PlayerKartInput playerInput;

    private float startTime = -1f;
    private float lapStart;
    private Vector3 lastPosition;

    private void Start()
    {
        kart = GetComponent<KartBehaviour>();
        body = GetComponent<Rigidbody>();
        centerline = FindFirstObjectByType<TrackCenterline>();
        checkpoints = FindFirstObjectByType<RaceCheckpointManager>();
        laps = FindFirstObjectByType<RaceLapManager>();
        playerInput = GetComponent<PlayerKartInput>();

        if (playerInput != null)
            playerInput.enabled = false;

        if (checkpoints != null)
            checkpoints.CheckpointPassed += HandleCheckpoint;

        if (laps != null)
            laps.LapCompleted += HandleLap;

        lastPosition = body.position;
        Log($"listo; vuelta de {(centerline != null ? centerline.Length : 0f):F0} m, {(checkpoints != null ? checkpoints.CheckpointCount : 0)} checkpoints");
    }

    private void OnDestroy()
    {
        if (checkpoints != null)
            checkpoints.CheckpointPassed -= HandleCheckpoint;

        if (laps != null)
            laps.LapCompleted -= HandleLap;

        if (playerInput != null)
            playerInput.enabled = true;
    }

    private void FixedUpdate()
    {
        if (kart == null || centerline == null || !centerline.IsValid)
            return;

        // Un salto grande de un paso al otro es una reaparición.
        if ((body.position - lastPosition).sqrMagnitude > 25f)
        {
            Respawns++;
            Log($"reapareció en {body.position.ToString("F1")} (a {centerline.GetDistance(body.position):F0} m de la llegada)");
        }

        lastPosition = body.position;

        if (Done)
        {
            kart.SetInputs(0f, 0f, true);
            return;
        }

        if (startTime < 0f && body.linearVelocity.sqrMagnitude > 1f)
        {
            startTime = Time.time;
            lapStart = startTime;
        }

        float speed = Vector3.Dot(body.linearVelocity, transform.forward);
        float distance = centerline.GetDistance(body.position);

        // Volante: apunta a un punto de la línea más adelante.
        Vector3 aim = centerline.GetPointAtDistance(distance + lookAhead + Mathf.Max(speed, 0f) * 0.6f);
        Vector3 toAim = Vector3.ProjectOnPlane(aim - body.position, transform.up);
        float angle = Vector3.SignedAngle(transform.forward, toAim, transform.up);
        float steer = Mathf.Clamp(angle / 25f, -1f, 1f);

        // Velocidad: la que permite la curva más cerrada que viene.
        float target = maxSpeedKmh / 3.6f;

        for (float ahead = 0f; ahead <= brakingLookAhead; ahead += 5f)
        {
            float radius = RadiusAt(distance + ahead);
            float cornerSpeed = Mathf.Sqrt(lateralGrip * radius);

            // Margen para frenar hasta esa curva (~5 m/s²).
            float allowed = Mathf.Sqrt(cornerSpeed * cornerSpeed + 2f * 5f * ahead);
            target = Mathf.Min(target, allowed);
        }

        float throttle = Mathf.Clamp((target - speed) * 0.8f, -1f, 1f);

        kart.SetInputs(throttle, steer, false);
    }

    /// <summary>Radio de la curva de la línea central en ese punto (m).</summary>
    private float RadiusAt(float distance)
    {
        Vector3 a = centerline.GetDirectionAtDistance(distance - 6f);
        Vector3 b = centerline.GetDirectionAtDistance(distance + 6f);
        a.y = 0f;
        b.y = 0f;

        float turn = Vector3.Angle(a, b) * Mathf.Deg2Rad;

        return turn > 1e-3f ? 12f / turn : 1000f;
    }

    private void HandleCheckpoint(Rigidbody passed, int index)
    {
        if (passed != body)
            return;

        CheckpointsPassed++;
        Log($"checkpoint {index}");
    }

    private void HandleLap(Rigidbody passed)
    {
        if (passed != body)
            return;

        LapsCompleted++;
        Log($"VUELTA {LapsCompleted} en {Time.time - lapStart:F1} s");
        lapStart = Time.time;

        // Al completar la carrera, frena y deja de manejar.
        if (laps != null && LapsCompleted >= laps.TotalLaps)
        {
            Done = true;
            Log("carrera terminada");
        }
    }

    /// <summary>Termina la prueba y deja el kart frenado.</summary>
    public void Stop()
    {
        Done = true;
    }

    private void Log(string message)
    {
        string time = startTime < 0f ? "  -  " : (Time.time - startTime).ToString("0.0", CultureInfo.InvariantCulture).PadLeft(5);
        string line = $"{time} s | {message}";

        report.AppendLine(line);
        Debug.Log("PISTA: " + line);
    }
}
