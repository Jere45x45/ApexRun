
using UnityEngine;

public class CustomIABots : MonoBehaviour
{
    [Header("Kart")]
    [SerializeField] private KartBehaviour kart;
    [SerializeField] private BotLearning learning;

    [Header("Pista")]
    [SerializeField] private TrackCenterline centerline;
    [SerializeField] private LayerMask trackLayer = 1 << 3;
    [SerializeField] private float segmentLength = 15f;

    [Header("Conduccion")]
    [SerializeField] private float fullSteerAngle = 20f;
    [SerializeField, Range(0f, 1f)] private float brakeStrength = 0.4f;
    [SerializeField] private float startSpeed = 8f;
    [SerializeField] private float alwaysAccelerateBelow = 3f;

    [Header("Deteccion de caidas")]
    [SerializeField] private float maxOffTrackTime = 1f;
    [SerializeField] private float fallCheckDistance = 6f;

    [Header("Dificultad")]
    [SerializeField] private float maxSpeed = 13f;

    private const int SpeedBuckets = 6;

    private static readonly float[] SpeedLimits =
    {
        8f, 11f, 14f, 17f, 20f
    };

    private const int Accelerate = 0;
    private const int Coast = 1;
    private const int Brake = 2;

    private Rigidbody rb;

    private float trackDistance;
    private float timeOffTrack;
    private float decisionTimer;

    private int lastAction = Accelerate;

    private bool eliminated;
    private bool raceStarted;

    private void Awake()
    {
        if (kart == null)
            kart = GetComponent<KartBehaviour>();

        rb = GetComponent<Rigidbody>();
    }

    private void Start()
    {
        if (kart == null || rb == null)
        {
            Debug.LogError(
                "CustomIABots: falta KartBehaviour o Rigidbody.",
                this
            );

            enabled = false;
            return;
        }

        if (centerline == null || !centerline.IsValid)
        {
            Debug.LogError(
                "CustomIABots necesita una TrackCenterline valida.",
                this
            );

            enabled = false;
            return;
        }

        if (learning == null)
            learning = BotLearning.Instance;

        if (learning == null)
        {
            Debug.LogError(
                "CustomIABots: no se encontro BotLearning.",
                this
            );

            enabled = false;
            return;
        }

        trackDistance = centerline.GetDistance(transform.position);

        kart.SetInputEnabled(true);

        raceStarted = false;

        Debug.Log(
            "Bot listo para correr. Dificultad actual: " +
            maxSpeed + " m/s",
            this
        );
    }

    private void FixedUpdate()
    {
        if (eliminated || !raceStarted)
            return;

        if (learning == null)
            learning = BotLearning.Instance;

        if (learning == null)
            return;

        if (!HasTrackBelow())
        {
            Vector3 origin = transform.position + Vector3.up * 0.5f;

    return Physics.Raycast(
        origin,
        Vector3.down,
        fallCheckDistance + 1f,
        trackLayer,
        QueryTriggerInteraction.Ignore
    );
        }
        else
        {
            timeOffTrack = 0f;
        }

        trackDistance = FindTrackDistance(trackDistance);

        decisionTimer += Time.fixedDeltaTime;

        if (decisionTimer >= 0.2f)
        {
            decisionTimer = 0f;
            ChooseDrivingAction();
        }

        Drive();
    }

    private void ChooseDrivingAction()
    {
        int state = GetState();

        // Solo consulta la politica de la IA.
        // No calcula recompensas ni actualiza la Q-table.
        lastAction = learning.ChooseAction(state);

        float speed = rb.linearVelocity.magnitude;

        if (speed < alwaysAccelerateBelow)
        {
            lastAction = Accelerate;
        }
        else if (speed >= maxSpeed)
        {
            lastAction = Brake;
        }
    }

    
private void Drive()
{
    float speed = rb.linearVelocity.magnitude;
    Vector3 position = transform.position;

    // Mira más adelante para anticipar las curvas.
    float lookAhead = Mathf.Clamp(
        5f + speed * 0.4f,
        5f,
        12f
    );

    float aimDistance =
        FindTrackDistanceNear(trackDistance, position)
        + lookAhead;

    Vector3 aimPoint =
        centerline.GetPointAtDistance(aimDistance);

    Vector3 directionToTarget = aimPoint - position;
    directionToTarget.y = 0f;

    if (directionToTarget.sqrMagnitude < 0.01f)
        return;

    directionToTarget.Normalize();

    Vector3 forward = transform.forward;
    forward.y = 0f;
    forward.Normalize();

    float angle = Vector3.SignedAngle(
        forward,
        directionToTarget,
        Vector3.up
    );

    // Limita el giro para evitar correcciones bruscas.
    float steering = Mathf.Clamp(
        angle / 45f,
        -0.65f,
        0.65f
    );

    float throttle;

    if (speed >= maxSpeed)
        throttle = -brakeStrength;
    else if (speed < alwaysAccelerateBelow)
        throttle = 1f;
    else
        throttle =
            lastAction == Accelerate ? 1f :
            lastAction == Brake ? -brakeStrength : 0f;

    kart.SetInputs(throttle, steering, false);
}


    private int GetState()
    {
        int segment = Mathf.Min(
            Mathf.FloorToInt(trackDistance / segmentLength),
            SegmentCount - 1
        );

        float speed = rb.linearVelocity.magnitude;
        int speedBucket = 0;

        while (
            speedBucket < SpeedLimits.Length &&
            speed >= SpeedLimits[speedBucket]
        )
        {
            speedBucket++;
        }

        return segment * SpeedBuckets + speedBucket;
    }

    private int SegmentCount =>
        Mathf.Max(
            1,
            Mathf.CeilToInt(centerline.Length / segmentLength)
        );

    private float FindTrackDistance(float around)
    {
        Vector3 position = transform.position;

        float best = around;
        float bestSqr = float.MaxValue;

        for (float offset = -10f; offset <= 30f; offset += 1f)
        {
            float candidate = Mathf.Repeat(
                around + offset, centerline.Length
            );

            float sqr = (
                centerline.GetPointAtDistance(candidate) - position
            ).sqrMagnitude;

            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = candidate;
            }
        }

        return best;
    }

    private float FindTrackDistanceNear(
        float around, Vector3 position)
    {
        float best = around;
        float bestSqr = float.MaxValue;

        for (float offset = -2f; offset <= 8f; offset += 1f)
        {
            float candidate = Mathf.Repeat(
                around + offset, centerline.Length
            );

            float sqr = (
                centerline.GetPointAtDistance(candidate) - position
            ).sqrMagnitude;

            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = candidate;
            }
        }

        return best;
    }

    private bool HasTrackBelow()
    {
        return Physics.Raycast(
            transform.position + Vector3.up * 2f,
            Vector3.down,
            fallCheckDistance,
            trackLayer,
            QueryTriggerInteraction.Ignore
        );
    }

    private static Vector3 Flat(Vector3 vector)
    {
        vector.y = 0f;

        return vector.sqrMagnitude > 1e-6f
            ? vector.normalized
            : Vector3.forward;
    }

    public void SetDifficulty(int level)
    {
        switch (level)
        {
            case 1:
                maxSpeed = 9f;
                break;

            case 2:
                maxSpeed = 13f;
                break;

            case 3:
                maxSpeed = 17f;
                break;

            default:
                Debug.LogWarning(
                    "Nivel invalido. Usa 1, 2 o 3.",
                    this
                );
                return;
        }

        Debug.Log(
            "Dificultad aplicada: " + level +
            " | Velocidad maxima: " + maxSpeed + " m/s",
            this
        );
    }

    
public void FallOffTrack()
{
    if (eliminated)
        return;

    eliminated = true;
    raceStarted = false;

    Debug.Log(
        "Bot eliminado de la carrera: " + gameObject.name,
        this
    );

    if (kart != null)
    {
        kart.SetInputs(0f, 0f, true);
        kart.SetInputEnabled(false);
    }

    if (rb != null)
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    gameObject.SetActive(false);
}


    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("FallZone"))
            FallOffTrack();
    }
}
