using UnityEngine;

/// <summary>
/// Bot que aprende la pista con Q-learning (BotLearning).
/// Cada decisión (decisionInterval) mira los sensores de pista, elige
/// izquierda / derecho / derecha y recibe una recompensa por lo que pasó desde
/// la decisión anterior:
/// - + por cada metro que se acercó al próximo punto de progreso (y - si se alejó).
/// - - si el sensor del centro dejó de ver pista.
/// - + extra al pasar un punto de progreso y al terminar la pista.
/// - Caerse o trabarse termina el episodio con castigo.
/// Así aprende algo en cada decisión, no solo cuando llega a un punto.
/// </summary>
public class CustomIABots : MonoBehaviour
{
    [Header("Kart")]
    [SerializeField] private BotBehaviour bot;
    [SerializeField] private KartRaySensor sensor;
    [SerializeField] private BotLearning learning;

    [Header("Track Progress")]
    [SerializeField] private Transform[] progressPoints;

    [Tooltip("A esta distancia de un punto de progreso se lo cuenta como pasado (m).")]
    [SerializeField] private float reachDistance = 5f;

    [Header("Rewards")]
    [Tooltip("Por cada metro que se acerca al próximo punto de progreso.")]
    [SerializeField] private float progressRewardPerMeter = 0.5f;

    [Tooltip("Cuando el sensor del centro (adelante, al medio) no ve pista.")]
    [SerializeField] private float offTrackPenalty = -1f;

    [SerializeField] private float progressReward = 5f;
    [SerializeField] private float finishReward = 30f;
    [SerializeField] private float fallPenalty = -20f;
    [SerializeField] private float stuckPenalty = -10f;

    [Tooltip("Solo si no hay puntos de progreso: premio por moverse.")]
    [SerializeField] private float movementReward = 0.1f;

    [Header("Learning")]
    [SerializeField] private float decisionInterval = 0.1f;

    [Header("Stuck Detection")]
    [SerializeField] private float stuckTime = 3f;
    [SerializeField] private float minimumSpeed = 0.5f;

    // Sensor de adelante al medio (KartRaySensor: fila cercana, centro).
    private const int CenterSensor = 1;

    private float timeWithoutMovement = 0f;

    private int currentProgress = 0;

    private float decisionTimer;

    private float[] lastObservation;
    private int lastAction;
    private float lastDistance;

    // Mientras se reacomoda después de un reinicio no decide ni aprende.
    private bool resetting;

    private Vector3 startPosition;
    private Quaternion startRotation;

    private Rigidbody rb;

    private void Awake()
    {
        if (bot == null)
            bot = GetComponent<BotBehaviour>();

        if (sensor == null)
            sensor = GetComponent<KartRaySensor>();

        rb = GetComponent<Rigidbody>();

        startPosition = transform.position;
        startRotation = transform.rotation;
    }

    private void FixedUpdate()
    {
        // BotLearning puede despertar después que este bot.
        if (learning == null)
            learning = BotLearning.Instance;

        if (learning == null || resetting)
            return;

        decisionTimer += Time.fixedDeltaTime;

        if (decisionTimer < decisionInterval)
            return;

        decisionTimer = 0f;

        float[] observation = sensor.GetTrackSensors();
        float distance = DistanceToNextPoint();

        float reward = 0f;
        bool reachedPoint = HasNextPoint() && distance < reachDistance;

        if (HasNextPoint())
        {
            reward += (lastDistance - distance) * progressRewardPerMeter;
        }
        else if (rb.linearVelocity.magnitude > minimumSpeed)
        {
            reward += movementReward;
        }

        if (observation[CenterSensor] < 0.5f)
            reward += offTrackPenalty;

        if (reachedPoint)
        {
            reward += progressReward;
            currentProgress++;

            Debug.Log(
                "BOT PASÓ PROGRESO: " +
                currentProgress +
                "/" +
                progressPoints.Length
            );

            if (currentProgress >= progressPoints.Length)
            {
                Debug.Log("BOT TERMINÓ LA PISTA");

                EndEpisode(reward + finishReward);
                return;
            }

            // El próximo punto es otro: la distancia se mide desde acá.
            distance = DistanceToNextPoint();
        }

        if (lastObservation != null)
        {
            learning.Learn(
                lastObservation,
                lastAction,
                reward,
                observation,
                false
            );
        }

        int action = learning.ChooseAction(observation);

        bot.SetInputs(
            1f,
            action - 1f, // 0 = izquierda (-1), 1 = derecho (0), 2 = derecha (+1)
            false
        );

        lastObservation = observation;
        lastAction = action;
        lastDistance = distance;

        CheckIfStuck();
    }

    private bool HasNextPoint()
    {
        return progressPoints != null &&
               currentProgress < progressPoints.Length &&
               progressPoints[currentProgress] != null;
    }

    private float DistanceToNextPoint()
    {
        if (!HasNextPoint())
            return 0f;

        return Vector3.Distance(
            transform.position,
            progressPoints[currentProgress].position
        );
    }

    public void FallOffTrack()
    {
        if (resetting)
            return;

        Debug.Log("el bot se cayo");

        EndEpisode(fallPenalty);
    }

    /// <summary>Termina el episodio: la última decisión recibe la recompensa final, sin futuro.</summary>
    private void EndEpisode(float finalReward)
    {
        if (learning != null && lastObservation != null)
        {
            learning.Learn(
                lastObservation,
                lastAction,
                finalReward,
                null,
                true
            );
        }

        ResetBot();
    }

    private void ResetBot()
    {
        resetting = true;

        bot.SetInputs(0f, 0f, true);

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        transform.position = startPosition;
        transform.rotation = startRotation;

        currentProgress = 0;

        lastObservation = null;

        decisionTimer = 0f;
        timeWithoutMovement = 0f;

        Invoke(nameof(StartAfterReset), 0.3f);
    }

    private void StartAfterReset()
    {
        resetting = false;
        lastDistance = DistanceToNextPoint();

        if (bot == null)
            return;

        bot.SetInputs(1f, 0f, false);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("FallZone"))
        {
            FallOffTrack();
        }
    }

    private void CheckIfStuck()
    {
        // Se llama una vez por decisión: el tiempo que pasa es decisionInterval.
        if (rb.linearVelocity.magnitude < minimumSpeed)
        {
            timeWithoutMovement += decisionInterval;

            if (timeWithoutMovement >= stuckTime)
            {
                Debug.Log("BOT TRABADO → REINICIANDO");

                EndEpisode(stuckPenalty);
            }
        }
        else
        {
            timeWithoutMovement = 0f;
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            Debug.Log("R APRETADA → NUEVO EPISODIO");

            ResetBot();
        }
    }
}
