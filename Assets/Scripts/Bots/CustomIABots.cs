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
/// sus carriles, qué premia su entrenamiento y, en el fácil y el medio, un
/// tope de velocidad en las curvas.
///
/// Saltos y derrapes: la pista tiene lomos que hacen saltar al kart. En el
/// aire el bot suelta el acelerador (si no, las ruedas de atrás se aceleran
/// solas y al caer patinan) y pone las ruedas hacia donde va el kart (si no,
/// cae con el volante cruzado y hace un trompo). Si la cola se le va, suelta
/// el acelerador hasta que vuelve a agarrar.
///
/// Entrenando (BotLearning con inferenceOnly apagado): si se cae, se sale
/// de la pista, hace un trompo o queda trabado, recibe un castigo y vuelve a
/// arrancar en otro lugar de la pista (o un poco antes de donde se cayó, para
/// practicar esa parte). No se elimina: así el entrenamiento no se corta.
///
/// En carrera: usa lo aprendido sin explorar, esquiva al de adelante, no se
/// tira encima del que va al lado y, el difícil, tapa al que viene atrás.
/// Respeta las reglas de carrera (ver UpdateTraffic): en las rectas deja
/// lugar al que va a la par, no se mueve de costado mientras frena, solo se
/// pone delante de otro con la posición ganada y, con tres a la par en una
/// recta, no se cruza. Estas reglas no son parte de lo aprendido: no hace
/// falta volver a entrenar. Si tiene un incidente, reaparece un poco antes, en un lugar libre de la
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

    // Si la tabla cree que acelerar es al menos esto peor que lo mejor, el piso del nivel no la obliga.
    private const float FloorTrust = 2f;

    // Riesgo de un tramo (incidentes recientes del entrenamiento) desde el que el piso no obliga.
    private const float RiskThreshold = 0.5f;

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

    [Header("Saltos y derrapes")]
    [Tooltip("Con menos ruedas apoyadas que esto, el kart está en el aire. Con 2 ruedas no cuenta: " +
             "puede ser el kart con las ruedas de un lado colgando del borde de la pista.")]
    [SerializeField, Range(1, 4)] private int minGroundedWheels = 2;

    [Tooltip("Tiempo en el aire (s) desde el que cuenta como salto: el bot suelta el acelerador y pone " +
             "las ruedas hacia donde va, así no cae cruzado. Los saltitos más cortos no cuentan.")]
    [SerializeField, Min(0f)] private float jumpTime = 0.06f;

    [Tooltip("Después de caer de un salto, cuánto tiempo más sigue sin acelerar (s, como mucho lo que duró el salto).")]
    [SerializeField, Min(0f)] private float landingTime = 0.25f;

    [Tooltip("Control de tracción: si las ruedas de atrás patinan más que estas veces el patinamiento de " +
             "máximo agarre, suelta el acelerador (por ejemplo al caer de un salto).")]
    [SerializeField, Range(1f, 4f)] private float wheelspinMargin = 1.5f;

    [Tooltip("Lomos: hasta cuántas veces la velocidad a la que el kart despega puede llegar a uno (1 = justo " +
             "sin despegar). Un poco más deja saltos cortos, que no molestan; menos hace frenar justo antes " +
             "del lomo, a veces ya doblando, y es peor.")]
    [SerializeField, Range(0.8f, 2f)] private float jumpSpeedMargin = 1.1f;

    [Tooltip("Derrape (°, entre hacia dónde mira el kart y hacia dónde va) desde el que suelta el acelerador " +
             "hasta que la cola vuelve a agarrar.")]
    [SerializeField, Range(5f, 45f)] private float slideAngle = 12f;

    [Header("Carrera")]
    [Tooltip("Esquiva al de adelante y no se tira encima del que va al lado.")]
    [SerializeField] private bool avoidKarts = true;

    [Header("Reglas de carrera")]
    [Tooltip("Largo de un kart (m). Dos karts van a la par si están a menos de esto; la posición está " +
             "ganada si el bot tiene la mitad o más del kart delante del otro.")]
    [SerializeField, Min(0.5f)] private float kartLength = 1.8f;

    [Tooltip("En las rectas, lugar que le deja de costado al que va a la par: distancia entre los centros de " +
             "los dos karts (m). Un kart mide 1,5 m de ancho.")]
    [SerializeField, Min(1.5f)] private float sideSpace = 2.5f;

    [Tooltip("Cuenta como recta donde, de 5 m atrás a 30 m adelante, las curvas son más abiertas que este radio (m).")]
    [SerializeField, Min(10f)] private float straightRadius = 60f;

    [Tooltip("Mientras frena no se mueve de costado si hay un kart a la par o a menos de esto atrás (m).")]
    [SerializeField, Min(0f)] private float brakeHoldDistance = 20f;

    [Tooltip("Desaceleración (m/s²) desde la que cuenta que está frenando, aunque no esté pisando el freno.")]
    [SerializeField, Min(0.5f)] private float brakingDecel = 3f;

    [Tooltip("El difícil: cuántas veces puede moverse para tapar al que viene atrás, hasta que ese kart se aleja.")]
    [SerializeField, Min(0)] private int maxDefenseMoves = 1;

    // Cada cuánto mira a los otros karts en carrera (s).
    private const float TrafficInterval = 0.1f;

    // El difícil tapa solo andando (no en la largada) y si el de atrás lo
    // está alcanzando: más rápido que él por lo menos esto (m/s).
    private const float MinDefenseSpeed = 12f;
    private const float AttackSpeed = 0.5f;

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
    private int defenseMoves;
    private bool defending;
    private float trafficTimer;

    // Reglas de carrera: dónde puede ir de costado (m desde la línea
    // central, + = derecha) para dejar lugar a los que van a la par, y si
    // tiene prohibido moverse de costado (frenando con alguien cerca, o tres
    // a la par en una recta).
    private bool hasSpaceLimits;
    private float spaceMin;
    private float spaceMax;
    private bool lateralLocked;

    // Hay un kart a la par o cerca atrás: si frena, no se mueve de costado.
    // Lo mira el tráfico cada 0,1 s; la frenada se mira en cada paso, así
    // se traba apenas empieza a frenar.
    private bool nearForBraking;

    // Frenada: velocidad del paso anterior, desaceleración medida y hasta cuándo cuenta como frenando.
    private float lastSpeed;
    private float smoothedAccel;
    private float brakingUntil;

    // Entrenamiento al estilo de la escena de Juli: arranca cuando le dicen (StartRace).
    private bool startRequested;

    // Agarre respecto del asfalto seco (1 = seco; con lluvia, menos). Lo
    // aprendido es en seco: con menos agarre, el bot hace de cuenta que va
    // más rápido (las curvas se toman a raíz del agarre) y frena antes.
    private float gripFactor = 1f;

    // Volante del kart (para pasar de curva deseada a mando).
    private float lockAngle = 20f;
    private float wheelBase = 1.05f;

    // Saltos y derrapes: cuánto lleva en el aire, cuánto le queda sin
    // acelerar después de caer, y si está derrapando.
    private float airTimer;
    private float landingTimer;
    private bool sliding;

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

        UpdateBraking(speed, deltaTime);

        decisionTimer += deltaTime;

        if (decisionTimer >= decisionInterval)
        {
            decisionTimer = 0f;
            Decide(speed);
        }

        // Tráfico y reglas de carrera: solo en carrera, más seguido que las
        // decisiones (los otros karts se mueven).
        if (Training)
        {
            ClearTraffic();
        }
        else
        {
            trafficTimer += deltaTime;

            if (trafficTimer >= TrafficInterval)
            {
                trafficTimer = 0f;
                UpdateTraffic(speed);
            }
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

        // Con el costado trabado por las reglas de carrera, el carril es el
        // que tiene (la tabla sigue desde ahí cuando se destraba).
        if (LateralHeld)
            laneIndex = NearestLane(lateralFraction);
        else if (move == MoveLeft)
            laneIndex = Mathf.Max(-2, laneIndex - 1);
        else if (move == MoveRight)
            laneIndex = Mathf.Min(2, laneIndex + 1);

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

        // Tope del nivel en las curvas (el fácil y el medio): frena si va más
        // rápido que esa fracción de la velocidad segura de la curva que viene.
        // También entrenando: la acción que se aprende es la que se hizo.
        float cap = profile != null ? profile.cornerSpeedCap : 0f;

        if (cap > 0f && speed > SpeedLimitAhead(trackDistance, speed) * cap)
            return Brake * LaneMoves + action % LaneMoves;

        // Lomos, en todos los niveles: si al llegar al que viene saldría
        // volando, frena. En el aire no dobla y cae afuera de la trazada.
        if (speed > JumpLimitAhead(trackDistance, speed))
            return Brake * LaneMoves + action % LaneMoves;

        // Piso del nivel: si va bastante más lento de lo que la curva que
        // viene permite, acelera. Entre el piso y el tope decide la tabla.
        // Salvo donde la tabla ya aprendió que acelerar sale caro o donde
        // el entrenamiento tuvo incidentes hace poco (por ejemplo, una curva
        // en S donde la cuenta de la velocidad segura se queda corta): ahí
        // manda la tabla.
        float floor = profile != null ? profile.cornerSpeedFloor : 0f;
        int accelerate = Accelerate * LaneMoves + action % LaneMoves;

        if (floor > 0f && speed < MaxSpeed && speed < SpeedLimitAhead(trackDistance, speed) * floor &&
            learning.GetRegret(difficulty, state, accelerate) < FloorTrust && !IsRiskyAhead())
        {
            return accelerate;
        }

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

        return Mathf.Min(limit, JumpLimitAhead(from, speed));
    }

    /// <summary>True si este tramo o los dos siguientes tuvieron incidentes en el entrenamiento (BotLearning.GetRisk).</summary>
    private bool IsRiskyAhead()
    {
        int segment = Mathf.FloorToInt(trackDistance / segmentLength);

        for (int ahead = 0; ahead <= 2; ahead++)
        {
            if (learning.GetRisk(difficulty, segment + ahead) > RiskThreshold)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Velocidad a la que se puede ir en ese punto para llegar a tiempo
    /// frenado a los lomos que vienen sin salir volando (con un poco de
    /// margen: un salto corto no molesta).
    /// </summary>
    private float JumpLimitAhead(float from, float speed)
    {
        float decel = BrakeDecel * gripFactor;
        float span = speed * speed / (2f * decel) + 10f;
        float limit = float.MaxValue;

        for (float x = 0f; x <= span; x += 1f)
        {
            float jump = info.GetJumpSpeed(from + x) * jumpSpeedMargin;
            limit = Mathf.Min(limit, Mathf.Sqrt(jump * jump + 2f * decel * x));
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
        airTimer = 0f;
        landingTimer = 0f;
        sliding = false;
    }

    // ------------------------------------------------------------ Manejo

    private void Drive(float speed, float deltaTime)
    {
        // Carril elegido (o el que pide el tráfico), como fracción del ancho utilizable.
        float desired = hasTrafficTarget ? trafficFraction : laneIndex / 2f * LaneWidth;
        float halfWidth = Mathf.Max(0.5f, info.GetHalfWidth(trackDistance));

        // Reglas de carrera: con el costado trabado sigue por donde va, y
        // nunca se mete en el lugar que tiene que dejarle al que va a la par
        // (si el otro se le acerca, se corre).
        if (LateralHeld)
            desired = lateralFraction;

        if (hasSpaceLimits)
        {
            float offset = info.GetLateralOffset(trackDistance, desired);
            desired = OffsetToFraction(trackDistance, Mathf.Clamp(offset, spaceMin, spaceMax));

            // La línea a la que apunta tampoco puede quedar en ese lugar: si
            // quedó (venía cambiando de carril), pasa ya al borde permitido,
            // así deja de ir hacia el otro.
            float current = info.GetLateralOffset(trackDistance, lateralFraction);
            lateralFraction = OffsetToFraction(trackDistance, Mathf.Clamp(current, spaceMin, spaceMax));
        }

        lateralFraction = Mathf.MoveTowards(lateralFraction, desired, laneChangeSpeed / halfWidth * deltaTime);

        // Punto al que apunta: sobre la trazada, más adelante cuanto más rápido va.
        float distanceAhead = Mathf.Clamp(lookAhead.x + lookAhead.y * Mathf.Max(0f, speed), lookAhead.x, lookAhead.z);
        Vector3 aim = info.GetPoint(trackDistance + distanceAhead, lateralFraction);

        float throttle = GetThrottle(speed);
        float steering;

        UpdateSteeringGeometry();

        airTimer = IsAirborne() ? airTimer + deltaTime : 0f;

        // Más de un segundo "en el aire" no es un salto (por ejemplo, con
        // las ruedas colgando de un borde): sigue manejando normal.
        if (airTimer > jumpTime && airTimer < 1f)
        {
            // Saltó: ni acelera ni frena (las ruedas de atrás se aceleran o
            // se bloquean solas y al caer patinan) y deja las ruedas hacia
            // donde va el kart, así cae derecho.
            landingTimer = Mathf.Min(landingTime, airTimer);
            throttle = 0f;
            steering = GetSteeringAlongTravel();
        }
        else
        {
            landingTimer = Mathf.Max(0f, landingTimer - deltaTime);
            sliding = IsSliding(speed);

            // Recién caído, con la cola yéndose o con las de atrás
            // patinando: sin acelerar hasta que agarra.
            if (landingTimer > 0f || sliding || (speed > 8f && IsWheelspinning()))
                throttle = Mathf.Min(throttle, 0f);

            steering = GetSteering(aim, speed);
        }

        kart.SetInputs(throttle, steering, false);
    }

    /// <summary>True si tiene menos de minGroundedWheels ruedas apoyadas (saltó en un lomo).</summary>
    private bool IsAirborne()
    {
        KartVehicle vehicle = kart.Vehicle;

        if (vehicle == null)
            return false;

        int grounded = 0;

        foreach (KartWheel wheel in vehicle.Wheels)
        {
            if (wheel != null && wheel.IsGrounded)
                grounded++;
        }

        return grounded < minGroundedWheels;
    }

    /// <summary>
    /// True si alguna rueda de atrás apoyada patina acelerando más de lo que
    /// agarra. Solo andando: para arrancar ya está el control de tracción de
    /// KartBehaviour.
    /// </summary>
    private bool IsWheelspinning()
    {
        KartVehicle vehicle = kart.Vehicle;

        if (vehicle == null || !vehicle.IsConfigured)
            return false;

        float allowed = vehicle.Tire.LongitudinalPeakSlipRatio * wheelspinMargin;

        KartWheel left = vehicle.Wheels[KartVehicle.RearLeft];
        KartWheel right = vehicle.Wheels[KartVehicle.RearRight];

        return (left.IsGrounded && left.SlipRatio > allowed) || (right.IsGrounded && right.SlipRatio > allowed);
    }

    /// <summary>
    /// True si la cola se le está yendo: el kart avanza cruzado (más de
    /// slideAngle entre hacia dónde mira y hacia dónde va). Deja de contar
    /// cuando vuelve a menos de la mitad.
    /// </summary>
    private bool IsSliding(float speed)
    {
        if (speed < 8f)
            return false;

        Vector3 local = Quaternion.Inverse(rb.rotation) * rb.linearVelocity;
        float angle = Mathf.Abs(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg);

        return angle > (sliding ? slideAngle * 0.5f : slideAngle);
    }

    /// <summary>
    /// Mando del volante que deja las ruedas delanteras hacia donde va de
    /// verdad el eje delantero (sin deslizar de costado). Usa la misma
    /// cuenta que KartBehaviour: el mando es una fracción del ángulo útil
    /// hacia cada lado, que va de la dirección de avance más o menos el
    /// ángulo de máximo agarre.
    /// </summary>
    private float GetSteeringAlongTravel()
    {
        KartVehicle vehicle = kart.Vehicle;

        if (vehicle == null || !vehicle.IsConfigured)
            return 0f;

        KartWheel left = vehicle.Wheels[KartVehicle.FrontLeft];
        KartWheel right = vehicle.Wheels[KartVehicle.FrontRight];

        Vector3 localPoint = 0.5f * (left.LocalPosition + right.LocalPosition);
        Vector3 worldPoint = rb.position + rb.rotation * localPoint;
        Vector3 velocity = Quaternion.Inverse(rb.rotation) * rb.GetPointVelocity(worldPoint);

        if (velocity.z < 4f)
            return 0f;

        float travel = Mathf.Atan2(velocity.x, velocity.z) * Mathf.Rad2Deg;
        float peak = vehicle.Tire.LateralPeakSlipAngle * Mathf.Rad2Deg;

        if (travel >= 0f)
        {
            float upper = Mathf.Clamp(travel + peak, 0f, lockAngle);
            return upper > 0.01f ? Mathf.Clamp01(travel / upper) : 0f;
        }

        float lower = Mathf.Clamp(travel - peak, -lockAngle, 0f);
        return lower < -0.01f ? -Mathf.Clamp01(travel / lower) : 0f;
    }

    /// <summary>
    /// Mide si está frenando: eligió frenar o va perdiendo velocidad de golpe
    /// (más de brakingDecel). Sigue contando un rato después, así no se
    /// mueve entre una frenada y la siguiente. Soltar por tener a otro kart
    /// adelante no cuenta: si no, el que viene detrás de uno más lento nunca
    /// se podría abrir para pasarlo.
    /// </summary>
    private void UpdateBraking(float speed, float deltaTime)
    {
        if (deltaTime > 0f)
            smoothedAccel = Mathf.Lerp(smoothedAccel, (speed - lastSpeed) / deltaTime, 0.2f);

        lastSpeed = speed;

        if (throttleChoice == Brake || (smoothedAccel < -brakingDecel && !trafficCoast))
            brakingUntil = Time.time + 0.3f;
    }

    private bool IsBraking => Time.time < brakingUntil;

    /// <summary>No se mueve de costado: tres a la par en una recta, o frenando con alguien cerca.</summary>
    private bool LateralHeld => lateralLocked || (nearForBraking && IsBraking);

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
            // Pasó un tramo entero sin incidente: baja su riesgo.
            int previousSegment = Mathf.FloorToInt(previous / segmentLength);

            if (progress > 0f && previousSegment != Mathf.FloorToInt(trackDistance / segmentLength))
                learning.ReportPass(difficulty, previousSegment, segmentCount);

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
            learning.ReportIncidentAt(difficulty, Mathf.FloorToInt(trackDistance / segmentLength), segmentCount);
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

        // El cambio de velocidad del salto no es una frenada.
        lastSpeed = Vector3.Dot(velocity, rotation * Vector3.forward);
        smoothedAccel = 0f;
        brakingUntil = 0f;
    }

    private int NearestLane(float fraction)
    {
        return LaneWidth > 0.01f ? Mathf.Clamp(Mathf.RoundToInt(fraction / LaneWidth * 2f), -2, 2) : 0;
    }

    // ------------------------------------------------------------ Tráfico (carrera)

    /// <summary>
    /// Mira a los karts de alrededor (el jugador y los otros bots) y decide
    /// por dónde ir de costado. Reglas de carrera, que mandan sobre todo lo
    /// demás:
    /// - En las rectas le deja lugar al que va a la par: no se le acerca a
    ///   menos de sideSpace de costado y, si el otro se le acerca, se corre.
    /// - Mientras frena no se mueve de costado si hay alguien a la par o
    ///   cerca atrás: así no se le cruza en el camino.
    /// - Se pone delante de otro kart solo con la posición ganada: la mitad o
    ///   más del kart delante del otro. Si no, le deja lugar.
    /// - Con tres a la par en una recta (él y otros dos), no se cruza: sigue
    ///   por donde va.
    /// Dentro de esas reglas:
    /// 1. Si alcanza al de adelante, se abre para pasarlo por el lado con más
    ///    lugar; si no hay lugar o no se puede mover, suelta. Mientras lo va
    ///    pasando sigue por esa línea: no se le vuelve a meter detrás.
    /// 2. Si hay alguien al lado (en una curva), no se le tira encima: sigue
    ///    por donde va.
    /// 3. El difícil, en las rectas y andando (en la largada no), si alguien
    ///    lo alcanza de atrás, se pone en su línea para taparlo:
    ///    maxDefenseMoves veces (una), hasta que ese kart se aleja. Después
    ///    sigue por donde quedó: no zigzaguea siguiéndolo.
    /// </summary>
    private void UpdateTraffic(float speed)
    {
        hasTrafficTarget = false;
        trafficCoast = false;
        lateralLocked = false;
        hasSpaceLimits = false;
        nearForBraking = false;

        bool defend = profile != null && profile.defendPosition;

        if (Time.time >= rivalsRefreshAt)
            RefreshRivals();

        bool straight = IsStraight(trackDistance);

        // Dónde está de costado de verdad (no la línea a la que apunta, que
        // va adelante del kart): con eso se sabe de qué lado está cada uno.
        float myOffset = info.GetLateralOffset(trackDistance, info.GetFraction(transform.position, trackDistance));

        // Lugar de costado que puede usar (m desde la línea central): toda la
        // pista utilizable, menos el que les tiene que dejar a los otros.
        float minOffset = -info.GetUsableLeft(trackDistance);
        float maxOffset = info.GetUsableRight(trackDistance);
        float spaceLeft = minOffset;
        float spaceRight = maxOffset;

        float aheadGap = float.MaxValue;
        float aheadOffset = 0f;
        float aheadSpeed = 0f;
        float passingGap = float.MaxValue;
        float passingSpeed = 0f;
        bool alongside = false;
        float behindGap = float.MaxValue;
        float behindFraction = 0f;
        float behindSpeed = 0f;
        int abreast = 0;
        bool nearWhileBraking = false;

        foreach (KartBehaviour rival in rivals)
        {
            if (rival == null || !rival.isActiveAndEnabled)
                continue;

            Vector3 position = rival.transform.position;

            if ((position - transform.position).sqrMagnitude > 45f * 45f)
                continue;

            float rivalDistance = FindTrackDistanceNear(trackDistance, position, -35f, 35f, 1.5f);

            // Cuánto adelante está el otro (m, negativo = atrás) y de qué lado (m, + = a la derecha).
            float gap = Wrap(rivalDistance - trackDistance);
            float rivalFraction = info.GetFraction(position, rivalDistance);
            float rivalOffset = info.GetLateralOffset(rivalDistance, rivalFraction);
            float rivalSpeed = GetSpeedAlongTrack(rival);
            float side = rivalOffset - myOffset;
            float lateralGap = Mathf.Abs(side);

            // Adelante y en el camino.
            if (gap > 0.5f && gap < aheadGap && lateralGap < 1.8f)
            {
                aheadGap = gap;
                aheadOffset = rivalOffset;
                aheadSpeed = rivalSpeed;
            }

            // Adelante, al costado de su línea: lo está pasando.
            if (gap > 0.5f && gap < passingGap && lateralGap >= 1.8f && lateralGap < sideSpace + 0.3f)
            {
                passingGap = gap;
                passingSpeed = rivalSpeed;
            }

            if (Mathf.Abs(gap) <= 2.5f && lateralGap < 2.2f)
                alongside = true;

            if (gap < -1f && -gap < behindGap && rivalSpeed > speed - 2f)
            {
                behindGap = -gap;
                behindFraction = rivalFraction;
                behindSpeed = rivalSpeed;
            }

            // A la par: los karts se superponen de adelante a atrás.
            bool overlapping = Mathf.Abs(gap) < kartLength;

            // (Hasta dos lugares al costado: él en una punta y los otros dos al lado.)
            if (overlapping && lateralGap < 2.5f * sideSpace)
                abreast++;

            if (lateralGap < 2f * sideSpace && (overlapping || (gap < 0f && -gap < brakeHoldDistance)))
                nearWhileBraking = true;

            // Posición ganada: el bot tiene la mitad o más del kart delante
            // del otro. Si no la tiene y el otro no va entero adelante, en
            // las rectas le deja lugar de costado.
            bool earned = gap <= -0.5f * kartLength;

            if (straight && !earned && gap < kartLength)
            {
                if (side > 0f || (side == 0f && maxOffset - myOffset < myOffset - minOffset))
                    spaceRight = Mathf.Min(spaceRight, rivalOffset - sideSpace);
                else
                    spaceLeft = Mathf.Max(spaceLeft, rivalOffset + sideSpace);
            }
        }

        // Lugar para los que van a la par.
        if (spaceLeft > minOffset || spaceRight < maxOffset)
        {
            // Apretado entre dos: va por el medio del hueco.
            if (spaceLeft > spaceRight)
                spaceLeft = spaceRight = 0.5f * (spaceLeft + spaceRight);

            hasSpaceLimits = true;
            spaceMin = Mathf.Max(spaceLeft, minOffset);
            spaceMax = Mathf.Min(spaceRight, maxOffset);

            if (spaceMin > spaceMax)
                spaceMin = spaceMax = Mathf.Clamp(0.5f * (spaceLeft + spaceRight), minOffset, maxOffset);
        }

        // Frenando con alguien cerca, o tres a la par en una recta: no se
        // mueve de costado.
        nearForBraking = nearWhileBraking;
        lateralLocked = (IsBraking && nearWhileBraking) || (straight && abreast >= 2);

        // El difícil vuelve a poder tapar cuando el de atrás se alejó.
        if (behindGap > (defend ? profile.defendDistance : 0f) + 4f)
        {
            defenseMoves = 0;
            defending = false;
        }

        // 1. Alcanza al de adelante.
        if (avoidKarts && aheadGap < 3f + speed * 0.6f && speed > aheadSpeed + 0.5f)
        {
            float passLeft = aheadOffset - sideSpace;
            float passRight = aheadOffset + sideSpace;
            float low = hasSpaceLimits ? spaceMin : minOffset;
            float high = hasSpaceLimits ? spaceMax : maxOffset;
            bool leftFree = !lateralLocked && passLeft >= low && passLeft <= high;
            bool rightFree = !lateralLocked && passRight >= low && passRight <= high;

            if (leftFree || rightFree)
            {
                bool goRight = rightFree &&
                               (!leftFree || Mathf.Abs(passRight - myOffset) < Mathf.Abs(passLeft - myOffset));

                SetTrafficTarget(OffsetToFraction(trackDistance, goRight ? passRight : passLeft));
            }
            else
            {
                trafficCoast = aheadGap < 2f + speed * 0.3f;
            }

            return;
        }

        if (lateralLocked)
            return;

        // 2. Alguien al lado, o lo va pasando: sigue por donde va.
        if (alongside || (avoidKarts && passingGap < 3f + speed * 0.6f && speed > passingSpeed + 0.5f))
        {
            SetTrafficTarget(lateralFraction);
            return;
        }

        // 3. Defender la posición, solo en las rectas (frenando ya no se
        // mueve), andando (en la largada no) y con la posición ganada (el de
        // atrás está más de un metro atrás) cuando el de atrás lo ataca. Un
        // movimiento, y se queda en esa línea.
        bool attacked = behindSpeed > speed + AttackSpeed;

        if (defend && straight && speed > MinDefenseSpeed && behindGap < profile.defendDistance && (defending || attacked))
        {
            if (!defending && defenseMoves < maxDefenseMoves && Time.time >= defenseHoldUntil)
            {
                defenseFraction = Mathf.Clamp(behindFraction, -LaneWidth, LaneWidth);
                defenseHoldUntil = Time.time + 1.2f;
                defenseMoves++;
                defending = true;
            }

            // Mientras dura el movimiento va a esa línea; después sigue por
            // donde quedó (volver a la anterior sería otro movimiento).
            if (defending)
                SetTrafficTarget(Time.time < defenseHoldUntil ? defenseFraction : lateralFraction);
        }
    }

    /// <summary>Entrenando no hay reglas de carrera ni tráfico: maneja solo con lo aprendido.</summary>
    private void ClearTraffic()
    {
        hasTrafficTarget = false;
        trafficCoast = false;
        lateralLocked = false;
        hasSpaceLimits = false;
        nearForBraking = false;
    }

    /// <summary>True si de 5 m atrás a 30 m adelante las curvas son más abiertas que straightRadius.</summary>
    private bool IsStraight(float distance)
    {
        float limit = 1f / straightRadius;

        for (float x = -5f; x <= 30f; x += 2.5f)
        {
            if (Mathf.Abs(info.GetCurvature(distance + x)) > limit)
                return false;
        }

        return true;
    }

    /// <summary>Fracción del lugar utilizable para un corrimiento desde la línea central (m, + = derecha).</summary>
    private float OffsetToFraction(float distance, float offset)
    {
        float fraction = offset >= 0f
            ? offset / Mathf.Max(0.3f, info.GetUsableRight(distance))
            : offset / Mathf.Max(0.3f, info.GetUsableLeft(distance));

        return Mathf.Clamp(fraction, -1f, 1f);
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
