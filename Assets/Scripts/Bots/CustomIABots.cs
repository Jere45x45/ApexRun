using UnityEngine;

/// <summary>
/// Bot que aprende a correr la pista con Q-learning (BotLearning). Maneja el
/// mismo kart que los jugadores (KartBehaviour, la física nueva) con SetInputs.
///
/// Se reparte el trabajo:
/// - El volante lo maneja solo: apunta a un punto un poco más adelante sobre la
///   línea central (TrackCenterline).
/// - Lo que APRENDE es lo difícil: en cada parte de la pista, cuándo acelerar,
///   soltar o frenar. O sea, se aprende la pista: dónde frenar y a cuánto se
///   puede tomar cada curva.
///
/// Lo que ve (el "estado"):
/// - En qué tramo de la pista está (la pista se corta en tramos de segmentLength metros).
/// - A qué velocidad va (6 rangos).
/// Con la pista de 1438 m y tramos de 15 m son 96 × 6 = 576 situaciones.
/// Como depende de los tramos, la tabla sirve para esta pista: otra pista
/// necesita su propio entrenamiento (y su propio archivo de tabla).
///
/// Lo que puede hacer: acelerar, soltar o frenar (3 acciones).
///
/// Recompensa en cada decisión:
/// - + por cada metro que avanzó sobre la pista (y - si retrocedió): ir más
///   rápido da más.
/// - - si no tiene pista debajo (y fuera de la pista no cuenta lo que avanza).
/// - + al completar una vuelta. Caerse, salirse más de un segundo, hacer un
///   trompo o trabarse termina el intento con castigo.
/// Cada intento arranca en un punto al azar de la pista, así practica todas las curvas.
/// </summary>
public class CustomIABots : MonoBehaviour
{
    [Header("Kart")]
    [SerializeField] private KartBehaviour kart;
    [SerializeField] private BotLearning learning;

    [Header("Pista")]
    [Tooltip("Línea central de la pista: con ella el bot sabe en qué parte de la pista está.")]
    [SerializeField] private TrackCenterline centerline;

    [Tooltip("Capa del asfalto (para saber si se salió).")]
    [SerializeField] private LayerMask trackLayer = 1 << 3;

    [Tooltip("Largo de cada tramo en el que se corta la pista (m). Más corto = aprende más fino, pero tarda más.")]
    [SerializeField] private float segmentLength = 15f;

    [Header("Volante y freno")]
    [Tooltip("Ángulo (°) hacia el punto de mira con el que gira el volante a fondo.")]
    [SerializeField] private float fullSteerAngle = 20f;

    [Tooltip("Cuánto pisa el freno al frenar (0 a 1). A fondo, el kart bloquea las " +
             "ruedas de atrás y hace un trompo; con 0.4 frena derecho.")]
    [SerializeField, Range(0f, 1f)] private float brakeStrength = 0.4f;

    [Header("Rewards")]
    [Tooltip("Por cada metro que avanza sobre la pista.")]
    [SerializeField] private float progressRewardPerMeter = 0.5f;

    [Tooltip("Cuando no tiene pista debajo.")]
    [SerializeField] private float offTrackPenalty = -1f;

    [Tooltip("Si pasa este tiempo fuera de la pista, el intento termina como si se hubiera caído (s).")]
    [SerializeField] private float maxOffTrackTime = 1f;

    [SerializeField] private float lapReward = 20f;
    [SerializeField] private float fallPenalty = -20f;
    [SerializeField] private float stuckPenalty = -10f;

    [Header("Entrenamiento")]
    [Tooltip("Cada cuánto decide (s). El volante se corrige siempre, en cada paso de física.")]
    [SerializeField] private float decisionTime = 0.2f;

    [Tooltip("Probabilidad de que un intento arranque en un punto al azar de la pista (si no, en la largada).")]
    [SerializeField, Range(0f, 1f)] private float randomStartChance = 0.8f;

    [Tooltip("Cada intento arranca ya andando a esta velocidad (m/s): en una subida, " +
             "desde parado algunos motores no llegan a arrancar.")]
    [SerializeField] private float startSpeed = 8f;

    [Tooltip("Si queda mirando más de este ángulo respecto de la pista (°), hizo un trompo: el intento termina.")]
    [SerializeField] private float spinAngle = 100f;

    [Header("Stuck Detection")]
    [Tooltip("Si pasa este tiempo casi quieto (por ejemplo, trabado en el borde de la pista), el intento termina (s).")]
    [SerializeField] private float stuckSeconds = 1.5f;
    [SerializeField] private float minimumSpeed = 0.5f;

    [Tooltip("Por debajo de esta velocidad siempre acelera (m/s).")]
    [SerializeField] private float alwaysAccelerateBelow = 3f;

    private const int SpeedBuckets = 6;

    // Límites de los rangos de velocidad (m/s): 8 ≈ 29 km/h, 11 ≈ 40, 14 ≈ 50, 17 ≈ 61, 20 ≈ 72.
    private static readonly float[] SpeedLimits = { 8f, 11f, 14f, 17f, 20f };

    // Las acciones (BotLearning.ActionCount).
    private const int Accelerate = 0;
    private const int Coast = 1;
    private const int Brake = 2;

    // Al reaparecer, el kart se apoya un poco por encima del asfalto (m).
    private const float SpawnHeight = 0.3f;

    private float timeWithoutMovement = 0f;
    private float timeOffTrack = 0f;
    private float decisionTimer;

    private int lastState = -1;
    private int lastAction = Accelerate;

    // Dónde está sobre la línea central y cuánto avanzó en este intento (m).
    private float trackDistance;
    private float episodeProgress;

    // Mientras se reacomoda después de un reinicio no decide ni aprende.
    private bool resetting;

    private Vector3 startPosition;
    private Quaternion startRotation;
    private float startDistance;

    private Rigidbody rb;

    // Lo más lejos que llegó un bot sin caerse (m), para seguir el entrenamiento en la consola.
    private static float record;

    private void Awake()
    {
        if (kart == null)
            kart = GetComponent<KartBehaviour>();

        rb = GetComponent<Rigidbody>();

        startPosition = transform.position;
        startRotation = transform.rotation;
    }

    private void Start()
    {
        if (centerline == null || !centerline.IsValid)
        {
            Debug.LogError("CustomIABots necesita una TrackCenterline generada.", this);
            enabled = false;
            return;
        }

        startDistance = centerline.GetDistance(startPosition);
        trackDistance = startDistance;

        // En la carrera los mandos los habilita la largada; acá entrena solo.
        kart.SetInputEnabled(true);
    }

    private void FixedUpdate()
    {
        // BotLearning puede despertar después que este bot.
        if (learning == null)
            learning = BotLearning.Instance;

        if (learning == null || resetting)
            return;

        decisionTimer += Time.fixedDeltaTime;

        if (decisionTimer >= decisionTime)
        {
            decisionTimer = 0f;

            if (!Decide())
                return;
        }

        Drive();
    }

    /// <summary>
    /// Una decisión: mira cómo le fue con la anterior, aprende y elige la
    /// próxima. Devuelve false si el intento terminó (y el bot se reinició).
    /// </summary>
    private bool Decide()
    {
        float previousDistance = trackDistance;
        trackDistance = FindTrackDistance(trackDistance);

        float advanced = Mathf.DeltaAngle(previousDistance / centerline.Length * 360f, trackDistance / centerline.Length * 360f) / 360f * centerline.Length;
        episodeProgress += advanced;

        int state = GetState();

        float reward;

        if (HasTrackBelow())
        {
            reward = advanced * progressRewardPerMeter;
            timeOffTrack = 0f;
        }
        else
        {
            // Fuera de la pista no cuenta lo que avance: si no, aprendería a cortar por el pasto.
            reward = offTrackPenalty;
            episodeProgress -= advanced;
            timeOffTrack += decisionTime;

            if (timeOffTrack >= maxOffTrackTime)
            {
                EndEpisode(fallPenalty);
                return false;
            }
        }

        // Trompo: quedó mirando para cualquier lado.
        Vector3 trackDirection = Flat(centerline.GetDirectionAtDistance(trackDistance));

        if (Vector3.Angle(trackDirection, Flat(transform.forward)) > spinAngle)
        {
            EndEpisode(fallPenalty);
            return false;
        }

        if (episodeProgress >= centerline.Length)
        {
            Debug.Log("BOT COMPLETÓ UNA VUELTA");
            EndEpisode(reward + lapReward);
            return false;
        }

        if (lastState >= 0)
            learning.Learn(lastState, lastAction, reward, state, false);

        lastState = state;
        lastAction = learning.ChooseAction(state);

        // Casi quieto siempre acelera: en una carrera nunca conviene quedarse
        // parado, y así no pierde intentos arrancando. Se aprende con la
        // acción que hizo de verdad.
        if (rb.linearVelocity.magnitude < alwaysAccelerateBelow)
            lastAction = Accelerate;

        if (CheckIfStuck())
            return false;

        return true;
    }

    /// <summary>
    /// Lleva el kart según la última acción: el volante apunta a un punto más
    /// adelante sobre la línea central y el pedal es el que eligió.
    /// </summary>
    private void Drive()
    {
        float speed = rb.linearVelocity.magnitude;
        Vector3 position = transform.position;

        // Más rápido, mira más lejos (si no, serpentea).
        float lookAhead = Mathf.Clamp(3f + speed * 0.6f, 5f, 14f);
        float aimDistance = FindTrackDistanceNear(trackDistance, position) + lookAhead;
        Vector3 aimPoint = centerline.GetPointAtDistance(aimDistance);

        float angle = Vector3.SignedAngle(Flat(transform.forward), Flat(aimPoint - position), Vector3.up);
        float steering = Mathf.Clamp(angle / fullSteerAngle, -1f, 1f);

        // Acelerador negativo = freno parcial (ver KartBehaviour.ApplyInputs).
        float throttle = lastAction == Accelerate ? 1f : lastAction == Brake ? -brakeStrength : 0f;

        kart.SetInputs(throttle, steering, false);
    }

    /// <summary>Arma el número de estado con lo que ve el bot.</summary>
    private int GetState()
    {
        int segment = Mathf.Min(Mathf.FloorToInt(trackDistance / segmentLength), SegmentCount - 1);

        float speed = rb.linearVelocity.magnitude;
        int speedBucket = 0;

        while (speedBucket < SpeedLimits.Length && speed >= SpeedLimits[speedBucket])
            speedBucket++;

        return segment * SpeedBuckets + speedBucket;
    }

    private int SegmentCount => Mathf.Max(1, Mathf.CeilToInt(centerline.Length / segmentLength));

    private static Vector3 Flat(Vector3 vector)
    {
        vector.y = 0f;
        return vector.sqrMagnitude > 1e-6f ? vector.normalized : Vector3.forward;
    }

    /// <summary>
    /// Dónde está sobre la línea central, buscando cerca de donde estaba: si la
    /// pista pasa cerca de otro tramo (por ejemplo un puente), no salta a ese tramo.
    /// </summary>
    private float FindTrackDistance(float around)
    {
        Vector3 position = transform.position;
        float best = around;
        float bestSqr = float.MaxValue;

        for (float offset = -10f; offset <= 30f; offset += 1f)
        {
            float candidate = Mathf.Repeat(around + offset, centerline.Length);
            float sqr = (centerline.GetPointAtDistance(candidate) - position).sqrMagnitude;

            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>Lo mismo pero barato, para el volante (entre decisiones avanza poco).</summary>
    private float FindTrackDistanceNear(float around, Vector3 position)
    {
        float best = around;
        float bestSqr = float.MaxValue;

        for (float offset = -2f; offset <= 8f; offset += 1f)
        {
            float candidate = Mathf.Repeat(around + offset, centerline.Length);
            float sqr = (centerline.GetPointAtDistance(candidate) - position).sqrMagnitude;

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
        return Physics.Raycast(transform.position + Vector3.up * 2f, Vector3.down, 6f, trackLayer, QueryTriggerInteraction.Ignore);
    }

    public void FallOffTrack()
    {
        if (resetting)
            return;

        EndEpisode(fallPenalty);
    }

    /// <summary>Termina el intento: la última decisión recibe la recompensa final, sin futuro.</summary>
    private void EndEpisode(float finalReward)
    {
        if (episodeProgress > record + 10f)
        {
            record = episodeProgress;
            Debug.Log("BOTS: nuevo récord, " + record.ToString("0") + " m sin caerse (la vuelta tiene " + centerline.Length.ToString("0") + " m)");
        }

        if (learning != null && lastState >= 0)
            learning.Learn(lastState, lastAction, finalReward, -1, true);

        ResetBot();
    }

    private void ResetBot()
    {
        resetting = true;

        kart.SetInputs(0f, 0f, true);

        Vector3 position = startPosition;
        Quaternion rotation = startRotation;
        trackDistance = startDistance;

        if (centerline != null && Random.value < randomStartChance)
        {
            trackDistance = Random.Range(0f, centerline.Length);
            position = centerline.GetPointAtDistance(trackDistance) + Vector3.up * SpawnHeight;
            rotation = Quaternion.LookRotation(Flat(centerline.GetDirectionAtDistance(trackDistance)), Vector3.up);
        }

        rb.position = position;
        rb.rotation = rotation;
        transform.SetPositionAndRotation(position, rotation);

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // La física nueva guarda estado propio (motor, giro de ruedas): a cero también.
        kart.ResetMotion();

        lastState = -1;
        lastAction = Accelerate;
        episodeProgress = 0f;
        decisionTimer = 0f;
        timeWithoutMovement = 0f;
        timeOffTrack = 0f;

        Invoke(nameof(StartAfterReset), 0.3f);
    }

    private void StartAfterReset()
    {
        resetting = false;
        rb.linearVelocity = transform.forward * startSpeed;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("FallZone"))
        {
            FallOffTrack();
        }
    }

    /// <summary>Se llama una vez por decisión. Devuelve true si se trabó (y se reinició).</summary>
    private bool CheckIfStuck()
    {
        if (rb.linearVelocity.magnitude < minimumSpeed)
        {
            timeWithoutMovement += decisionTime;

            if (timeWithoutMovement >= stuckSeconds)
            {
                EndEpisode(stuckPenalty);
                return true;
            }
        }
        else
        {
            timeWithoutMovement = 0f;
        }

        return false;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetBot();
        }
    }
}
