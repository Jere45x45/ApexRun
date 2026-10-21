using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bot que maneja un kart (KartBehaviour) y aprende la pista con Q-learning.
/// Lo aprendido lo guarda BotLearning: una tabla por nivel, compartida por
/// todos los bots de ese nivel.
///
/// Cada 0,25 s el bot mira en qué situación está y elige qué hacer:
/// - Situación: en qué tramo de la pista está (de 15 m), a qué velocidad va
///   (8 rangos hasta 120 km/h) y por qué carril de su trazada va (5
///   carriles, de la izquierda a la derecha).
/// - Acción: acelerar, soltar o frenar, y al mismo tiempo seguir en su
///   carril o pasarse uno a la izquierda o a la derecha. Así aprende también
///   la trazada: dónde abrirse y dónde cerrarse en cada curva.
/// El volante lo maneja solo: apunta a un punto más adelante sobre el
/// carril elegido (más lejos cuanto más rápido va).
///
/// Los tres niveles (BotLevelProfile) tienen el mismo kart y la misma
/// velocidad máxima (120 km/h). Cambia cuánto ancho de pista pueden usar
/// sus carriles y qué premia su entrenamiento.
///
/// Entrenando (BotLearning con inferenceOnly apagado): si se cae, se sale
/// de la pista, hace un trompo o queda trabado, recibe un castigo y vuelve a
/// arrancar en otro lugar de la pista (o un poco antes de donde se cayó, para
/// practicar esa parte). No se elimina: así el entrenamiento no se corta.
///
/// En carrera: usa lo aprendido sin explorar, esquiva al de adelante, no se
/// tira encima del que va al lado y, el difícil, tapa al que viene atrás.
/// Si tiene un incidente, reaparece un poco antes, en un lugar libre de la
/// pista. Con lluvia frena antes (lo aprendido es en seco). Maneja solo
/// mientras la carrera le habilita
/// los mandos (KartBehaviour.InputEnabled), y solo en la computadora que lo
/// simula (en red, el host).
/// </summary>
[DefaultExecutionOrder(-50)] // Antes que KartBehaviour: los mandos se usan en el mismo paso de física.
public class CustomIABots : MonoBehaviour
{
    public const int LaneCount = 5;
    public const int ThrottleChoices = 3;
    public const int LaneMoves = 3;
    public const int ActionCount = ThrottleChoices * LaneMoves;

    private const int Accelerate = 0;
    private const int Coast = 1;
    private const int Brake = 2;

    private const int KeepLane = 0;
    private const int MoveLeft = 1;
    private const int MoveRight = 2;

    // Rangos de velocidad (km/h): hasta 15, de 15 a 30, ... y más de 105.
    private static readonly float[] SpeedLimitsKmh = { 15f, 30f, 45f, 60f, 75f, 90f, 105f };

    private static int SpeedBucketCount => SpeedLimitsKmh.Length + 1;

    // Agarre del kart de los bots, medido en plano: unos 13,5 m/s² de costado
    // y 16 m/s² frenando. Se usa con margen para la idea de arranque de cada
    // situación nueva y para el seguro de la carrera.
    private const float LateralGrip = 11.5f;
    private const float BrakeDecel = 13f;

    // Desde cuántas veces aprendida una situación se confía en la tabla sin el seguro.
    private const int MinTrustedVisits = 20;

    [Header("Kart")]
    [SerializeField] private KartBehaviour kart;
    [SerializeField] private BotLearning learning;

    [Header("Nivel")]
    [SerializeField] private RaceDifficulty difficulty = RaceDifficulty.Medium;

    [Header("Pista")]
    [Tooltip("Línea central de la pista. Vacío = la que haya en la escena.")]
    [SerializeField] private TrackCenterline centerline;

    [Tooltip("Largo de cada tramo de la pista para el aprendizaje (m). Si se cambia, lo aprendido deja de servir.")]
    [SerializeField, Min(5f)] private float segmentLength = 15f;

    [Header("Conducción")]
    [Tooltip("Velocidad máxima de todos los bots, de cualquier nivel (km/h).")]
    [SerializeField, Min(10f)] private float maxSpeedKmh = 120f;

    [Tooltip("Por debajo de esta velocidad (m/s) siempre acelera, para no quedarse quieto.")]
    [SerializeField, Min(0f)] private float alwaysAccelerateBelow = 3f;

    [Tooltip("Cada cuánto decide qué hacer (s).")]
    [SerializeField, Range(0.1f, 1f)] private float decisionInterval = 0.25f;

    [Tooltip("Qué tan rápido se corre de costado al cambiar de carril (m/s).")]
    [SerializeField, Min(0.5f)] private float laneChangeSpeed = 3f;

    [Tooltip("A qué distancia mira para doblar (m): mínimo, más tanto por cada m/s, y máximo.")]
    [SerializeField] private Vector3 lookAhead = new Vector3(4f, 0.35f, 14f);

    [Tooltip("Cuánto corrige el volante según lo que gira de más o de menos.")]
    [SerializeField, Range(0f, 1f)] private float yawDamping = 0.3f;

    [Header("Incidentes (entrenamiento / carrera, s)")]
    [Tooltip("Tiempo sin asfalto debajo antes de contar como caída.")]
    [SerializeField] private Vector2 maxOffTrackTime = new Vector2(0.6f, 1.5f);

    [Tooltip("Tiempo con 3 o 4 ruedas fuera del asfalto antes de contar como salida.")]
    [SerializeField] private Vector2 maxWheelsOutTime = new Vector2(1f, 2.5f);

    [Tooltip("Tiempo casi quieto antes de contar como trabado.")]
    [SerializeField] private Vector2 stuckTime = new Vector2(2f, 3f);

    [Tooltip("Ángulo con la pista (°) que cuenta como trompo.")]
    [SerializeField, Range(45f, 180f)] private float spinAngle = 100f;

    [Header("Entrenamiento")]
    [Tooltip("Al caerse, probabilidad de volver a arrancar en un lugar al azar de la pista (si no, un poco antes de donde se cayó).")]
    [SerializeField, Range(0f, 1f)] private float randomStartChance = 0.6f;

    [Tooltip("Velocidad al volver a arrancar (m/s): entre estos dos valores, al azar.")]
    [SerializeField] private Vector2 startSpeedRange = new Vector2(6f, 22f);

    [Header("Carrera")]
    [Tooltip("Esquiva al de adelante y no se tira encima del que va al lado.")]
    [SerializeField] private bool avoidKarts = true;

    private Rigidbody rb;
    private BotTrackInfo info;
    private BotLevelProfile profile;
    private bool ready;
    private bool hasLearning;

    private int segmentCount;

    // Dónde está sobre la pista (m desde la largada) y por dónde va de costado.
    private float trackDistance;
    private int laneIndex;
    private float lateralFraction;

    // Lo que eligió en la última decisión.
    private int throttleChoice = Accelerate;
    private float decisionTimer;
    private bool hasPrevious;
    private int previousState;
    private int previousAction;
    private float progressSinceDecision;
    private float wheelsOutSum;
    private int wheelsOutSamples;

    // Incidentes.
    private float offTrackTimer;
    private float wheelsOutTimer;
    private float stuckTimer;
    private float spinTimer;
    private float backwardsProgress;
    private float lastIncidentDistance = -1f;

    // Vueltas limpias (para las estadísticas del entrenamiento).
    private float lapProgress;
    private float lapStartTime;

    // Tráfico (carrera).
    private readonly List<KartBehaviour> rivals = new List<KartBehaviour>();
    private float rivalsRefreshAt;
    private bool hasTrafficTarget;
    private float trafficFraction;
    private bool trafficCoast;
    private float defenseFraction;
    private float defenseHoldUntil;

    // Entrenamiento al estilo de la escena de Juli: arranca cuando le dicen (StartRace).
    private bool startRequested;

    // Agarre respecto del asfalto seco (1 = seco; con lluvia, menos). Lo
    // aprendido es en seco: con menos agarre, el bot hace de cuenta que va
    // más rápido (las curvas se toman a raíz del agarre) y frena antes.
    private float gripFactor = 1f;

    // Volante del kart (para pasar de curva deseada a mando).
    private float lockAngle = 20f;
    private float wheelBase = 1.05f;

    public RaceDifficulty Difficulty => difficulty;

    private bool Training => hasLearning && learning.IsTraining;

    private float MaxSpeed => maxSpeedKmh / 3.6f;

    private float LaneWidth => profile != null ? profile.laneWidth : 0.6f;

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
            Debug.LogError("CustomIABots: falta KartBehaviour o Rigidbody.", this);
            enabled = false;
            return;
        }

        if (centerline == null)
            centerline = FindFirstObjectByType<TrackCenterline>();

        info = BotTrackInfo.Get(centerline);

        if (info == null)
        {
            Debug.LogError("CustomIABots necesita una TrackCenterline válida.", this);
            enabled = false;
            return;
        }

        if (learning == null)
            learning = BotLearning.Instance;

        segmentCount = Mathf.Max(1, Mathf.CeilToInt(info.Length / segmentLength));

        PrepareLearning();

        trackDistance = centerline.GetDistance(transform.position);
        lateralFraction = Mathf.Clamp(info.GetFraction(transform.position, trackDistance), -LaneWidth, LaneWidth);
        laneIndex = NearestLane(lateralFraction);
        lapStartTime = Time.time;

        ready = true;

        if (startRequested)
            BeginRace();
    }

    /// <summary>Lo usa la escena de entrenamiento (DifficultyManager): habilita los mandos y arranca.</summary>
    public void StartRace()
    {
        startRequested = true;

        if (ready)
            BeginRace();
    }

    private void BeginRace()
    {
        startRequested = false;
        ResetDecision();
        kart.SetInputEnabled(true);
    }

    /// <summary>Nivel: 1 fácil, 2 medio, 3 difícil. Todos con la misma velocidad máxima.</summary>
    public void SetDifficulty(int level)
    {
        if (level < 1 || level > 3)
        {
            Debug.LogWarning("Nivel inválido. Usá 1, 2 o 3.", this);
            return;
        }

        SetDifficulty((RaceDifficulty)(level - 1));
    }

    public void SetDifficulty(RaceDifficulty value)
    {
        difficulty = value;

        if (ready)
        {
            PrepareLearning();
            laneIndex = Mathf.Clamp(laneIndex, -2, 2);
            ResetDecision();
        }
    }

    private void PrepareLearning()
    {
        hasLearning = false;
        profile = null;

        if (learning == null)
        {
            Debug.LogWarning("CustomIABots: no hay BotLearning en la escena; el bot maneja con lo básico, sin lo aprendido.", this);
            return;
        }

        profile = learning.GetProfile(difficulty);

        // Sin depender del idioma de la computadora (15.0 y no 15,0).
        string layout = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}x{1}x{2}x{3}@{4:F1}/{5:F0}",
            segmentCount, SpeedBucketCount, LaneCount, ActionCount, segmentLength, info.Length);
        hasLearning = profile != null && learning.Prepare(difficulty, segmentCount * SpeedBucketCount * LaneCount, ActionCount, layout);
    }

    private void FixedUpdate()
    {
        // Las copias de los karts de otra computadora no se manejan acá.
        if (!ready || !kart.IsSimulated)
            return;

        // Cuenta regresiva, carrera terminada o todavía sin arrancar.
        if (!kart.InputEnabled)
        {
            hasPrevious = false;
            return;
        }

        float deltaTime = Time.fixedDeltaTime;
        float speed = Vector3.Dot(rb.linearVelocity, transform.forward);

        UpdateTrackDistance(deltaTime);

        if (CheckIncident(speed, deltaTime))
        {
            HandleIncident();
            return;
        }

        decisionTimer += deltaTime;

        if (decisionTimer >= decisionInterval)
        {
            decisionTimer = 0f;
            Decide(speed);
        }

        Drive(speed, deltaTime);
    }

    // ------------------------------------------------------------ Decisiones

    private void Decide(float speed)
    {
        gripFactor = MeasureGrip();

        int state = GetState(speed / Mathf.Sqrt(gripFactor));

        if (Training && hasPrevious)
        {
            float wheelsOut = wheelsOutSamples > 0 ? wheelsOutSum / wheelsOutSamples : 0f;

            float reward =
                profile.progressReward * progressSinceDecision
                - profile.timePenalty
                - profile.offTrackPenalty * wheelsOut
                - profile.centerPreference * Mathf.Abs(laneIndex) / 2f;

            learning.Learn(difficulty, previousState, previousAction, reward, state, false);
            learning.ReportDistance(difficulty, progressSinceDecision);
        }

        int action = ChooseAction(state, speed);

        throttleChoice = action / LaneMoves;
        int move = action % LaneMoves;

        if (move == MoveLeft)
            laneIndex = Mathf.Max(-2, laneIndex - 1);
        else if (move == MoveRight)
            laneIndex = Mathf.Min(2, laneIndex + 1);

        if (!Training)
            UpdateTraffic(speed);

        previousState = state;
        previousAction = action;
        hasPrevious = true;
        progressSinceDecision = 0f;
        wheelsOutSum = 0f;
        wheelsOutSamples = 0;
    }

    private int ChooseAction(int state, float speed)
    {
        if (!hasLearning)
            return BestOf(GetPrior(state));

        if (learning.NeedsPrior(difficulty, state))
            learning.SetPrior(difficulty, state, GetPrior(state));

        int action = learning.ChooseAction(difficulty, state);

        // Seguro de la carrera, solo en situaciones que casi no entrenó: si va
        // bastante más rápido de lo que la curva que viene permite (medida
        // sobre la línea central), frena aunque la tabla diga otra cosa. En
        // lo bien entrenado no se mete: la trazada del difícil pasa las
        // curvas más abiertas y más rápido que eso.
        if (!Training && learning.GetVisits(difficulty, state) < MinTrustedVisits &&
            speed > SpeedLimitAhead(trackDistance, speed) * 1.1f)
        {
            action = Brake * LaneMoves + action % LaneMoves;
        }

        return action;
    }

    /// <summary>
    /// Idea de arranque para una situación nueva: acelerar si la curva que
    /// viene se puede tomar a esta velocidad (con margen), frenar si no, y
    /// seguir en el carril. Se mira desde el final del tramo y con la
    /// velocidad más alta de su rango: la decisión vale para todo el tramo,
    /// así que tiene que servir también en el peor caso. El
    /// entrenamiento la corrige enseguida.
    /// </summary>
    private float[] GetPrior(int state)
    {
        int segment = state / LaneCount / SpeedBucketCount;
        int speedBucket = state / LaneCount % SpeedBucketCount;
        float topSpeed = (speedBucket < SpeedLimitsKmh.Length ? SpeedLimitsKmh[speedBucket] : maxSpeedKmh + 5f) / 3.6f;

        bool tooFast = topSpeed > SpeedLimitAhead((segment + 1) * segmentLength, topSpeed);
        float[] values = new float[ActionCount];

        for (int action = 0; action < ActionCount; action++)
        {
            int throttle = action / LaneMoves;
            int move = action % LaneMoves;

            float value = throttle == Accelerate ? (tooFast ? -0.5f : 0.5f)
                : throttle == Brake ? (tooFast ? 0.5f : -0.3f)
                : 0f;

            if (move != KeepLane)
                value -= 0.05f;

            values[action] = value;
        }

        return values;
    }

    /// <summary>
    /// Velocidad a la que se puede ir en ese punto de la pista para llegar a
    /// tiempo frenado a las curvas que vienen (según la línea central y un
    /// agarre con margen, menos con lluvia).
    /// </summary>
    private float SpeedLimitAhead(float from, float speed)
    {
        float grip = LateralGrip * gripFactor;
        float decel = BrakeDecel * gripFactor;
        float span = speed * speed / (2f * decel) + 20f;
        float limit = float.MaxValue;

        for (float x = 0f; x <= span; x += 3f)
        {
            float k = Mathf.Abs(info.GetCurvature(from + x));

            if (k < 1e-4f)
                continue;

            float cornerSpeed = Mathf.Sqrt(grip / k);
            limit = Mathf.Min(limit, Mathf.Sqrt(cornerSpeed * cornerSpeed + 2f * decel * x));
        }

        return limit;
    }

    /// <summary>Agarre de las ruedas en el asfalto respecto del seco (1 seco, menos mojado).</summary>
    private float MeasureGrip()
    {
        KartVehicle vehicle = kart.Vehicle;

        if (vehicle == null)
            return gripFactor;

        float sum = 0f;
        int count = 0;

        foreach (KartWheel wheel in vehicle.Wheels)
        {
            if (wheel == null || !wheel.IsGrounded || wheel.CurrentSurface == null || !wheel.CurrentSurface.IsValidForTrack)
                continue;

            sum += Mathf.Lerp(1f, wheel.CurrentSurface.WetGripMultiplier, TrackConditions.Wetness);
            count++;
        }

        return count > 0 ? Mathf.Clamp(sum / count, 0.3f, 1f) : gripFactor;
    }

    private static int BestOf(float[] values)
    {
        int best = 0;

        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] > values[best])
                best = i;
        }

        return best;
    }

    private int GetState(float speed)
    {
        int segment = Mathf.Clamp(Mathf.FloorToInt(trackDistance / segmentLength), 0, segmentCount - 1);

        float kmh = speed * 3.6f;
        int speedBucket = 0;

        while (speedBucket < SpeedLimitsKmh.Length && kmh >= SpeedLimitsKmh[speedBucket])
            speedBucket++;

        return (segment * SpeedBucketCount + speedBucket) * LaneCount + (laneIndex + 2);
    }

    private void ResetDecision()
    {
        hasPrevious = false;
        decisionTimer = decisionInterval;
        progressSinceDecision = 0f;
        wheelsOutSum = 0f;
        wheelsOutSamples = 0;
        throttleChoice = Accelerate;
        offTrackTimer = 0f;
        wheelsOutTimer = 0f;
        stuckTimer = 0f;
        spinTimer = 0f;
        backwardsProgress = 0f;
        hasTrafficTarget = false;
        trafficCoast = false;
    }

    // ------------------------------------------------------------ Manejo

    private void Drive(float speed, float deltaTime)
    {
        // Carril elegido (o el que pide el tráfico), como fracción del ancho utilizable.
        float desired = hasTrafficTarget ? trafficFraction : laneIndex / 2f * LaneWidth;
        float halfWidth = Mathf.Max(0.5f, info.GetHalfWidth(trackDistance));

        lateralFraction = Mathf.MoveTowards(lateralFraction, desired, laneChangeSpeed / halfWidth * deltaTime);

        // Punto al que apunta: sobre la trazada, más adelante cuanto más rápido va.
        float distanceAhead = Mathf.Clamp(lookAhead.x + lookAhead.y * Mathf.Max(0f, speed), lookAhead.x, lookAhead.z);
        Vector3 aim = info.GetPoint(trackDistance + distanceAhead, lateralFraction);

        kart.SetInputs(GetThrottle(speed), GetSteering(aim, speed), false);
    }

    private float GetThrottle(float speed)
    {
        if (speed < alwaysAccelerateBelow)
            return 1f;

        float throttle = throttleChoice == Accelerate ? 1f : throttleChoice == Brake ? -1f : 0f;

        if (trafficCoast)
            throttle = Mathf.Min(throttle, speed > 8f ? -0.3f : 0f);

        // Nadie pasa de la velocidad máxima de los bots.
        if (speed >= MaxSpeed)
            throttle = Mathf.Min(throttle, 0f);

        return throttle;
    }

    /// <summary>
    /// Volante para llegar al punto (pure pursuit): la curva que hay que
    /// hacer para pasar por él, como fracción de la curva más cerrada que el
    /// kart puede hacer a esta velocidad. Con el mando a fondo, KartBehaviour
    /// deja la rueda en el máximo agarre, así que no hace trompo. Además
    /// corrige según lo que el kart gira de más o de menos.
    /// </summary>
    private float GetSteering(Vector3 aim, float speed)
    {
        Vector3 toAim = aim - transform.position;
        toAim.y = 0f;

        Vector3 forward = transform.forward;
        forward.y = 0f;

        if (toAim.sqrMagnitude < 0.01f || forward.sqrMagnitude < 1e-4f)
            return 0f;

        UpdateSteeringGeometry();

        float alpha = Vector3.SignedAngle(forward, toAim, Vector3.up) * Mathf.Deg2Rad;
        float distance = Mathf.Max(1f, toAim.magnitude);
        float curvature = 2f * Mathf.Sin(alpha) / distance;

        float v = Mathf.Max(1f, Mathf.Abs(speed));
        float maxByLock = Mathf.Tan(lockAngle * Mathf.Deg2Rad) / wheelBase;
        float maxByGrip = 13.5f / (v * v);
        float maxCurvature = Mathf.Max(0.01f, Mathf.Min(maxByLock, maxByGrip));

        float steering = curvature / maxCurvature;
        float yawError = curvature * v - rb.angularVelocity.y;
        steering += yawDamping * yawError / (maxCurvature * v);

        return Mathf.Clamp(steering, -1f, 1f);
    }

    private void UpdateSteeringGeometry()
    {
        KartVehicle vehicle = kart.Vehicle;

        if (vehicle == null || !vehicle.IsConfigured)
            return;

        lockAngle = Mathf.Max(5f, vehicle.Chassis.steering.maxSteerAngle);

        float front = 0.5f * (vehicle.Wheels[KartVehicle.FrontLeft].LocalPosition.z + vehicle.Wheels[KartVehicle.FrontRight].LocalPosition.z);
        float rear = 0.5f * (vehicle.Wheels[KartVehicle.RearLeft].LocalPosition.z + vehicle.Wheels[KartVehicle.RearRight].LocalPosition.z);
        wheelBase = Mathf.Max(0.5f, front - rear);
    }

    // ------------------------------------------------------------ Pista

    /// <summary>Avanza la posición sobre la pista (buscando cerca de la anterior) y suma el progreso.</summary>
    private void UpdateTrackDistance(float deltaTime)
    {
        float previous = trackDistance;
        trackDistance = FindTrackDistanceNear(trackDistance, transform.position, -10f, 30f, 1f);

        float progress = Wrap(trackDistance - previous);

        progressSinceDecision += progress;
        backwardsProgress = progress < 0f ? backwardsProgress + progress : Mathf.Min(0f, backwardsProgress + progress);

        if (Training)
        {
            lapProgress += progress;

            if (lapProgress >= info.Length)
            {
                learning.ReportLap(difficulty, Time.time - lapStartTime);
                lapProgress -= info.Length;
                lapStartTime = Time.time;
            }
        }

        wheelsOutSum += WheelsOut();
        wheelsOutSamples++;
    }

    /// <summary>
    /// Distancia sobre la pista del punto de la línea más cercano a la
    /// posición, buscando cerca de "around" (de from a to metros, de a step).
    /// Después afina entre los dos vecinos, así no salta de a un paso.
    /// </summary>
    private float FindTrackDistanceNear(float around, Vector3 position, float from, float to, float step)
    {
        float best = around;
        float bestSqr = float.MaxValue;

        for (float offset = from; offset <= to; offset += step)
        {
            float candidate = around + offset;
            float sqr = (centerline.GetPointAtDistance(candidate) - position).sqrMagnitude;

            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = candidate;
            }
        }

        float a = best - step;
        Vector3 start = centerline.GetPointAtDistance(a);
        Vector3 segment = centerline.GetPointAtDistance(best + step) - start;
        float t = Mathf.Clamp01(Vector3.Dot(position - start, segment) / Mathf.Max(1e-4f, segment.sqrMagnitude));

        return Mathf.Repeat(a + 2f * step * t, info.Length);
    }

    private float Wrap(float delta)
    {
        float length = info.Length;
        return Mathf.Repeat(delta + length * 0.5f, length) - length * 0.5f;
    }

    /// <summary>Fracción de ruedas apoyadas fuera del asfalto (0 a 1).</summary>
    private float WheelsOut()
    {
        KartVehicle vehicle = kart.Vehicle;

        if (vehicle == null)
            return 0f;

        int count = 0;

        foreach (KartWheel wheel in vehicle.Wheels)
        {
            if (wheel != null && wheel.IsGrounded && wheel.IsOnInvalidSurface)
                count++;
        }

        return count / 4f;
    }

    // ------------------------------------------------------------ Incidentes

    /// <summary>True si se cayó, se salió de la pista, hizo un trompo, va al revés o quedó trabado.</summary>
    private bool CheckIncident(float speed, float deltaTime)
    {
        int mode = Training ? 0 : 1;
        Vector3 position = transform.position;

        // Sin asfalto debajo (se cayó o se fue lejos).
        offTrackTimer = info.IsOnAsphalt(position) ? 0f : offTrackTimer + deltaTime;

        if (offTrackTimer > maxOffTrackTime[mode])
            return true;

        // Tres o cuatro ruedas en el pasto.
        wheelsOutTimer = WheelsOut() >= 0.75f ? wheelsOutTimer + deltaTime : 0f;

        if (wheelsOutTimer > maxWheelsOutTime[mode])
            return true;

        // Muy por debajo de la pista.
        if (position.y < centerline.GetPointAtDistance(trackDistance).y - 4f)
            return true;

        // Trompo: mirando para cualquier lado respecto de la pista.
        Vector3 forward = transform.forward;
        forward.y = 0f;
        Vector3 direction = centerline.GetDirectionAtDistance(trackDistance);
        direction.y = 0f;

        bool spun = Vector3.Angle(forward, direction) > spinAngle;
        spinTimer = spun ? spinTimer + deltaTime : 0f;

        if (spinTimer > (Training ? 0.3f : 1.5f))
            return true;

        // Volcado.
        if (Vector3.Angle(transform.up, Vector3.up) > 70f)
            return true;

        // Va al revés.
        if (backwardsProgress < -10f)
            return true;

        // Trabado.
        stuckTimer = Mathf.Abs(speed) < 1.5f ? stuckTimer + deltaTime : 0f;

        return stuckTimer > stuckTime[mode];
    }

    /// <summary>Lo usan TrainingBounds y las zonas de caída: el bot se cayó.</summary>
    public void FallOffTrack()
    {
        if (ready && kart.IsSimulated && kart.InputEnabled)
            HandleIncident();
    }

    private void HandleIncident()
    {
        if (Training)
        {
            if (hasPrevious)
                learning.Learn(difficulty, previousState, previousAction, -profile.incidentPenalty, 0, true);

            learning.ReportIncident(difficulty);
            lastIncidentDistance = trackDistance;
            RespawnForTraining();
            return;
        }

        RespawnInRace();
    }

    /// <summary>Entrenamiento: vuelve a arrancar en otro lugar, con otra velocidad y otro carril.</summary>
    private void RespawnForTraining()
    {
        float distance = lastIncidentDistance < 0f || Random.value < randomStartChance
            ? Random.Range(0f, info.Length)
            : Mathf.Repeat(lastIncidentDistance - Random.Range(20f, 60f), info.Length);

        int lane = Random.Range(-2, 3);
        float fraction = lane / 2f * LaneWidth;
        float speed = Random.Range(startSpeedRange.x, startSpeedRange.y);

        // No arranca más rápido de lo que la curva de ahí permite.
        trackDistance = distance;
        speed = Mathf.Min(speed, SpeedLimitAhead(distance, speed) * 0.9f);

        Vector3 point = info.PlaceOnAsphalt(info.GetPoint(distance, fraction), out Vector3 normal);
        Vector3 forward = Vector3.ProjectOnPlane(centerline.GetDirectionAtDistance(distance), normal).normalized;
        Quaternion rotation = Quaternion.LookRotation(forward, normal);

        Teleport(point + normal * 0.35f, rotation, forward * speed);

        laneIndex = lane;
        lateralFraction = fraction;
        lapProgress = 0f;
        lapStartTime = Time.time;
        ResetDecision();
    }

    /// <summary>
    /// Carrera: reaparece en la pista un poco antes de donde se fue, quieto y
    /// en un lugar libre (si hay otro kart, prueba al costado y más atrás).
    /// Nunca encima de otro kart: dos karts superpuestos salen despedidos.
    /// Volver a pasar un checkpoint que ya pasó no cuenta dos veces.
    /// </summary>
    private void RespawnInRace()
    {
        RefreshRivals();

        float[] fractions = { 0f, -0.5f, 0.5f };
        Vector3 position = Vector3.zero;
        Quaternion rotation = Quaternion.identity;
        bool found = false;

        for (int attempt = 0; attempt < 8 && !found; attempt++)
        {
            float distance = Mathf.Repeat(trackDistance - 12f - attempt * 8f, info.Length);

            foreach (float fraction in fractions)
            {
                Vector3 point = info.PlaceOnAsphalt(info.GetPoint(distance, fraction), out Vector3 normal);
                Vector3 forward = Vector3.ProjectOnPlane(centerline.GetDirectionAtDistance(distance), normal).normalized;

                position = point + normal * 0.35f;
                rotation = Quaternion.LookRotation(forward, normal);

                if (IsFree(position))
                {
                    found = true;
                    break;
                }
            }
        }

        Teleport(position, rotation, Vector3.zero);

        trackDistance = centerline.GetDistance(rb.position);
        lateralFraction = Mathf.Clamp(info.GetFraction(rb.position, trackDistance), -LaneWidth, LaneWidth);
        laneIndex = NearestLane(lateralFraction);
        ResetDecision();
    }

    private void Teleport(Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        var networkTransform = GetComponent<Unity.Netcode.Components.NetworkTransform>();

        if (networkTransform != null && networkTransform.IsSpawned && networkTransform.CanCommitToTransform)
        {
            networkTransform.Teleport(position, rotation, transform.localScale);
        }
        else
        {
            rb.position = position;
            rb.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
        }

        rb.linearVelocity = velocity;
        rb.angularVelocity = Vector3.zero;

        // La física del kart (ruedas, motor) se pone de acuerdo con la velocidad nueva.
        kart.ResetMotion();
    }

    private int NearestLane(float fraction)
    {
        return LaneWidth > 0.01f ? Mathf.Clamp(Mathf.RoundToInt(fraction / LaneWidth * 2f), -2, 2) : 0;
    }

    // ------------------------------------------------------------ Tráfico (carrera)

    /// <summary>
    /// Mira a los karts de alrededor y decide si tiene que salirse de su
    /// carril:
    /// 1. Si alcanza al de adelante, se abre para pasarlo por el lado con más
    ///    lugar; si no hay lugar, suelta.
    /// 2. Si hay alguien al lado, no se le tira encima: sigue por donde va.
    /// 3. El difícil, si alguien se le acerca de atrás, se pone en su línea
    ///    para taparlo. Mantiene cada movimiento al menos 1,2 s, para no
    ///    zigzaguear.
    /// </summary>
    private void UpdateTraffic(float speed)
    {
        hasTrafficTarget = false;
        trafficCoast = false;

        bool defend = profile != null && profile.defendPosition;

        if (!avoidKarts && !defend)
            return;

        if (Time.time >= rivalsRefreshAt)
            RefreshRivals();

        float halfWidth = Mathf.Max(0.5f, info.GetHalfWidth(trackDistance));
        float aheadGap = float.MaxValue;
        float aheadFraction = 0f;
        float aheadSpeed = 0f;
        bool alongside = false;
        float behindGap = float.MaxValue;
        float behindFraction = 0f;

        foreach (KartBehaviour rival in rivals)
        {
            if (rival == null || !rival.isActiveAndEnabled)
                continue;

            Vector3 position = rival.transform.position;

            if ((position - transform.position).sqrMagnitude > 45f * 45f)
                continue;

            float rivalDistance = FindTrackDistanceNear(trackDistance, position, -35f, 35f, 1.5f);
            float gap = Wrap(rivalDistance - trackDistance);
            float rivalFraction = info.GetFraction(position, rivalDistance);
            float rivalSpeed = GetSpeedAlongTrack(rival);
            float lateralGap = Mathf.Abs(rivalFraction - lateralFraction) * halfWidth;

            if (gap > 0.5f && gap < aheadGap && lateralGap < 1.8f)
            {
                aheadGap = gap;
                aheadFraction = rivalFraction;
                aheadSpeed = rivalSpeed;
            }

            if (Mathf.Abs(gap) <= 2.5f && lateralGap < 2.2f)
                alongside = true;

            if (gap < -1f && -gap < behindGap && rivalSpeed > speed - 2f)
            {
                behindGap = -gap;
                behindFraction = rivalFraction;
            }
        }

        // 1. Alcanza al de adelante.
        if (avoidKarts && aheadGap < 3f + speed * 0.6f && speed > aheadSpeed + 0.5f)
        {
            float step = 2.2f / halfWidth;
            float limit = Mathf.Max(LaneWidth, 0.85f);
            float leftFraction = Mathf.Clamp(aheadFraction - step, -limit, limit);
            float rightFraction = Mathf.Clamp(aheadFraction + step, -limit, limit);
            bool leftFree = Mathf.Abs(leftFraction - aheadFraction) * halfWidth > 1.6f;
            bool rightFree = Mathf.Abs(rightFraction - aheadFraction) * halfWidth > 1.6f;

            if (leftFree || rightFree)
            {
                bool goRight = rightFree &&
                               (!leftFree || Mathf.Abs(rightFraction - lateralFraction) < Mathf.Abs(leftFraction - lateralFraction));

                SetTrafficTarget(goRight ? rightFraction : leftFraction);
            }
            else
            {
                trafficCoast = aheadGap < 2f + speed * 0.3f;
            }

            return;
        }

        // 2. Alguien al lado: sigue por donde va.
        if (alongside)
        {
            SetTrafficTarget(lateralFraction);
            return;
        }

        // 3. Defender la posición.
        if (defend && behindGap < profile.defendDistance)
        {
            if (Time.time >= defenseHoldUntil)
            {
                defenseFraction = Mathf.Clamp(behindFraction, -LaneWidth, LaneWidth);
                defenseHoldUntil = Time.time + 1.2f;
            }

            SetTrafficTarget(defenseFraction);
        }
    }

    /// <summary>True si no hay otro kart a menos de 4 m de ese lugar.</summary>
    private bool IsFree(Vector3 position)
    {
        foreach (KartBehaviour rival in rivals)
        {
            if (rival != null && rival.isActiveAndEnabled && (rival.transform.position - position).sqrMagnitude < 16f)
                return false;
        }

        return true;
    }

    private void SetTrafficTarget(float fraction)
    {
        hasTrafficTarget = true;
        trafficFraction = fraction;

        // Lo que decide la tabla sigue desde el carril más cercano.
        laneIndex = NearestLane(Mathf.Clamp(fraction, -LaneWidth, LaneWidth));
    }

    private void RefreshRivals()
    {
        rivals.Clear();

        foreach (KartBehaviour other in FindObjectsByType<KartBehaviour>(FindObjectsSortMode.None))
        {
            if (other != kart && other.Vehicle != null)
                rivals.Add(other);
        }

        rivalsRefreshAt = Time.time + 1f;
    }

    /// <summary>Velocidad del otro kart (m/s). Las copias de red tienen el Rigidbody quieto: se usa la que manda su dueño.</summary>
    private static float GetSpeedAlongTrack(KartBehaviour other)
    {
        NetworkKart networkKart = other.GetComponent<NetworkKart>();

        if (networkKart != null && !other.IsSimulated)
            return Vector3.Dot(networkKart.OwnerVelocity, other.transform.forward);

        Rigidbody body = other.GetComponent<Rigidbody>();
        return body != null ? Vector3.Dot(body.linearVelocity, other.transform.forward) : 0f;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("FallZone"))
            FallOffTrack();
    }
}
