
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

    private static readonly float[] SpeedLimits =
    {
    5f, 8f, 11f, 14f, 17f, 20f, 24f, 28f, 32f
    };

    private const int SpeedBuckets = 10;

    private const int Accelerate = 0;
    private const int Coast = 1;
    private const int Brake = 2;

    private Rigidbody rb;

    private float trackDistance;
    private float timeOffTrack;
    private float decisionTimer;

    private int lastAction = Accelerate;
    private int previousState;
    private int previousAction;
    private float previousTrackDistance;
    private bool hasPreviousDecision;

    private bool eliminated;
    private bool raceStarted;

    private bool startRequested;
    private bool initialized;

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

        kart.SetInputEnabled(false);

        raceStarted = false;
        initialized = true;

        Debug.Log("Bot preparado y esperando el inicio de la carrera.", this);

        if (startRequested)
        {
        BeginRace();
        }
    }

    public void StartRace()
    {
        if (eliminated)
        {
            Debug.LogWarning(
            "Este bot ya fue eliminado y no puede volver a correr.",
            this
            );
            return;
        }

        startRequested = true;

        if (initialized)
        {
            BeginRace();
        }
    }

    private void BeginRace()
    {
        if (eliminated || !initialized || !startRequested)
        return;

        trackDistance = centerline.GetDistance(transform.position);
        timeOffTrack = 0f;
        decisionTimer = 0f;
        lastAction = Accelerate;

        hasPreviousDecision = false;
        previousState = 0;
        previousAction = Accelerate;
        previousTrackDistance = trackDistance;

        raceStarted = true;
        startRequested = false;

        kart.SetInputEnabled(true);

        Debug.Log(
        "Carrera iniciada para " + gameObject.name,
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
            timeOffTrack += Time.fixedDeltaTime;

            if (timeOffTrack >= maxOffTrackTime)
            {
                FallOffTrack();
                return;
            }
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

    // Aprender de la decision anterior.
    if (hasPreviousDecision)
    {
        float progress = trackDistance - previousTrackDistance;
        float length = centerline.Length;

        // Corregir el progreso cuando se cruza
        // el final de una pista circular.
        if (progress > length * 0.5f)
            progress -= length;
        else if (progress < -length * 0.5f)
            progress += length;

        // Recompensar avanzar y penalizar retroceder
        // o quedarse sin avanzar.
        float reward =
            Mathf.Clamp(progress, -2f, 2f) * 0.2f - 0.01f;

        learning.Learn(
            previousState,
            previousAction,
            reward,
            state,
            false
        );
    }

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

    previousState = state;
    previousAction = lastAction;
    previousTrackDistance = trackDistance;
    hasPreviousDecision = true;
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
        Vector3 origin = transform.position + Vector3.up * 0.5f;

        return Physics.Raycast
        (
            origin,
            Vector3.down,
            fallCheckDistance + 1f,
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
            maxSpeed = 100f / 3.6f; // 100 km/h
            break;

            case 2:
            maxSpeed = 110f / 3.6f; // 110 km/h
            break;

            case 3:
            maxSpeed = 120f / 3.6f; // 120 km/h
            break;

            default:
            Debug.LogWarning(
                "Nivel invalido. Usa 1, 2 o 3.",
                this
            );
            return;
        }

        Debug.Log
        (
            "Dificultad " + level +
            " | Velocidad maxima: " +
            (maxSpeed * 3.6f).ToString("F0") + " km/h",
            this
        );
    }

    
public void FallOffTrack()
{
    if (eliminated)
        return;
    if (learning != null && hasPreviousDecision)
    {
    learning.Learn(
        previousState,
        previousAction,
        -10f,
        GetState(),
        true
    );

    hasPreviousDecision = false;
    }

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
