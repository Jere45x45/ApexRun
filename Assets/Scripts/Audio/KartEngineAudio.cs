using UnityEngine;

/// <summary>
/// Sonido del motor sintetizado en tiempo real, sin grabaciones, con un modelo
/// "informado por la física" (como Baldan et al., 2015): en lugar de imitar el
/// sonido, se simula de dónde sale.
///
/// Un Rotax 125 es un monocilíndrico de dos tiempos: explota una vez por vuelta
/// (a 12.000 rpm, 200 veces por segundo). Lo que se escucha son tres fuentes:
///
/// 1. Escape (la principal). En cada vuelta el pistón destapa la lumbrera de
///    escape y sale un golpe de gases a presión (el "soplido"). Ese golpe entra
///    en la cámara de expansión, un caño de ~0,9 m donde la onda va y vuelve
///    rebotando. Se modela con una guía de onda (dos líneas de retardo): cuando
///    el ritmo de las explosiones coincide con el ida y vuelta de la onda, el
///    caño "entra en resonancia" y el motor grita (lo que los pilotos llaman
///    "entrar en el caño"). Después pasa por el silenciador, que apaga los agudos.
/// 2. Admisión. Con el acelerador abierto, el motor chupa aire por el
///    carburador y la caja de aire en cada vuelta: es el rugido ronco que se
///    escucha arriba del kart. Sin acelerar casi desaparece.
/// 3. Mecánica: el golpe grave de cada explosión que transmite el block y el
///    chillido de la cadena (dientes de la corona por vuelta del eje).
///
/// Sin acelerar, un dos tiempos "cuatro tiempea": explota una vuelta sí y otra
/// no, débil y desparejo, con algún petardazo. Con el limitador se cortan
/// explosiones seguidas.
///
/// El sonido se genera en el hilo de audio (AudioClip en modo stream), así que
/// el AudioSource lo trata como cualquier clip: volumen 3D, distancia, Doppler.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class KartEngineAudio : MonoBehaviour
{
    [SerializeField] private KartBehaviour kart;

    [Header("Mezcla")]
    [SerializeField, Range(0f, 1f)] private float volume = 0.7f;

    [SerializeField, Range(0f, 2f)] private float exhaustLevel = 1f;

    [SerializeField, Range(0f, 2f)] private float intakeLevel = 0.8f;

    [SerializeField, Range(0f, 2f)] private float mechanicalLevel = 0.5f;

    [SerializeField, Range(0f, 1f)] private float chainLevel = 0.06f;

    [Header("Escape (cámara de expansión)")]
    [Tooltip("Largo del caño, de la lumbrera a la salida (m). Rotax 125: ~0,9 m.")]
    [SerializeField, Range(0.3f, 1.5f)] private float pipeLength = 0.9f;

    [Tooltip("Velocidad del sonido en los gases del escape (m/s). A ~400 °C ronda los 520 m/s.")]
    [SerializeField, Range(340f, 650f)] private float exhaustSoundSpeed = 520f;

    [Tooltip("Cuánto rebota la onda contra la lumbrera de escape (0 a 1).")]
    [SerializeField, Range(0f, 0.95f)] private float portReflection = 0.7f;

    [Tooltip("Cuánto rebota la onda en el cono de salida (0 a 1). Más alto = caño más resonante.")]
    [SerializeField, Range(0f, 0.95f)] private float outletReflection = 0.6f;

    [Tooltip("Frecuencia sobre la que la onda pierde energía en cada rebote (Hz).")]
    [SerializeField, Range(500f, 10000f)] private float pipeDamping = 6000f;

    [Tooltip("Corte del silenciador (Hz): por encima, el sonido sale apagado.")]
    [SerializeField, Range(500f, 8000f)] private float silencerCutoff = 3500f;

    [Tooltip("Parte del sonido que atraviesa el silenciador sin filtrar: el filo áspero del dos tiempos.")]
    [SerializeField, Range(0f, 0.5f)] private float silencerLeak = 0.25f;

    [Tooltip("Ruido turbulento de los gases dentro de cada soplido.")]
    [SerializeField, Range(0f, 1f)] private float rasp = 0.8f;

    [Header("Admisión (carburador y caja de aire)")]
    [Tooltip("Largo del conducto de admisión y la caja de aire (m).")]
    [SerializeField, Range(0.1f, 0.8f)] private float intakeLength = 0.35f;

    [Tooltip("Corte de la caja de aire (Hz).")]
    [SerializeField, Range(300f, 5000f)] private float intakeCutoff = 1500f;

    [Header("Cadena")]
    [Tooltip("Dientes de la corona del eje trasero: el chillido de la cadena es dientes × vueltas del eje.")]
    [SerializeField, Min(1)] private int axleSprocketTeeth = 84;

    [Header("Irregularidad")]
    [Tooltip("Variación de fuerza entre explosiones: un motor real nunca suena parejo.")]
    [SerializeField, Range(0f, 0.5f)] private float roughness = 0.1f;

    [Tooltip("Fuerza de las explosiones sin acelerar, relativa al motor a fondo.")]
    [SerializeField, Range(0f, 1f)] private float offThrottleLevel = 0.3f;

    [Tooltip("Probabilidad de que una vuelta no explote sin acelerar (por encima de 4.000 rpm).")]
    [SerializeField, Range(0f, 0.9f)] private float overrunMisfire = 0.5f;

    [Tooltip("Probabilidad de que una vuelta no explote en ralentí.")]
    [SerializeField, Range(0f, 0.9f)] private float idleMisfire = 0.25f;

    [Tooltip("Probabilidad de que una vuelta no explote con el limitador activo.")]
    [SerializeField, Range(0f, 1f)] private float limiterMisfire = 0.6f;

    [Tooltip("Probabilidad, por vuelta sin acelerar, de un petardazo en el escape.")]
    [SerializeField, Range(0f, 0.2f)] private float backfireChance = 0.02f;

    // Ciclo del dos tiempos, en fracciones de vuelta desde que abre la lumbrera de escape.
    private const float ExhaustWindow = 0.53f;   // escape abierto ~190°
    private const float BlowdownPeak = 0.08f;    // el soplido llega al máximo enseguida
    private const float IntakeStart = 0.45f;     // el cárter aspira en la otra mitad de la vuelta
    private const float IntakeWindow = 0.5f;

    private const float RpmSmoothing = 0.015f;
    private const float LoadSmoothing = 0.04f;
    private const float BodyCutoff = 120f;
    private const float DcCutoff = 25f;
    private const float IntakeSoundSpeed = 345f;
    private const int MaxDelay = 2048;

    private AudioSource source;
    private int sampleRate;

    // Los escribe el hilo principal y los lee el hilo de audio.
    private volatile float targetRpm = 2000f;
    private volatile float targetLoad;
    private volatile float targetChainHz;
    private volatile bool limiterActive;
    private volatile bool running;

    // Estado del sintetizador: solo lo toca el hilo de audio.
    private readonly System.Random random = new System.Random();
    private float rpm = 2000f;
    private float load;
    private float chainHz;
    private float phase;
    private float cycleScale = 1f;
    private float cycleStrength;
    private float crackle;

    private readonly float[] pipeForward = new float[MaxDelay];
    private readonly float[] pipeBackward = new float[MaxDelay];
    private int pipeIndex;
    private float pipeLoss;

    private readonly float[] intakeForward = new float[MaxDelay];
    private readonly float[] intakeBackward = new float[MaxDelay];
    private int intakeIndex;
    private float intakeLoss;
    private float intakeNoise;

    private float silencer1;
    private float silencer2;
    private float airbox;
    private float body;
    private float chainPhase;
    private float dcInput;
    private float dcOutput;

    private void Reset()
    {
        kart = GetComponentInParent<KartBehaviour>();
    }

    private void Awake()
    {
        source = GetComponent<AudioSource>();
        sampleRate = AudioSettings.outputSampleRate;

        if (kart == null)
            kart = GetComponentInParent<KartBehaviour>();

        source.clip = AudioClip.Create("Motor (sintetizado)", sampleRate, 1, sampleRate, true, GenerateAudio);
        source.loop = true;
    }

    private void OnEnable()
    {
        if (source != null && !source.isPlaying)
            source.Play();
    }

    private void OnDisable()
    {
        if (source != null)
            source.Stop();
    }

    private void Update()
    {
        KartVehicle vehicle = kart != null ? kart.Vehicle : null;
        KartEngine engine = vehicle != null ? vehicle.Engine : null;

        if (engine == null)
        {
            running = false;
            return;
        }

        targetRpm = engine.Rpm;
        targetLoad = Mathf.Clamp01(engine.Throttle);
        limiterActive = engine.IsLimiterActive;

        float axleTurnsPerSecond = vehicle.RearAxle != null
            ? Mathf.Abs(vehicle.RearAxle.AngularVelocity) / (2f * Mathf.PI)
            : 0f;

        targetChainHz = axleTurnsPerSecond * axleSprocketTeeth;
        running = true;
    }

    /// <summary>Genera las muestras del clip. Corre en el hilo de audio.</summary>
    private void GenerateAudio(float[] data)
    {
        if (!running || sampleRate <= 0)
        {
            System.Array.Clear(data, 0, data.Length);
            return;
        }

        float dt = 1f / sampleRate;

        // Ida de la onda por cada conducto, en muestras.
        int pipeDelay = Mathf.Clamp(Mathf.RoundToInt(pipeLength / exhaustSoundSpeed * sampleRate), 2, MaxDelay);
        int intakeDelay = Mathf.Clamp(Mathf.RoundToInt(intakeLength / IntakeSoundSpeed * sampleRate), 2, MaxDelay);

        float pipeLossCoefficient = OnePole(pipeDamping, dt);
        float silencerCoefficient = OnePole(silencerCutoff, dt);
        float intakeCoefficient = OnePole(intakeCutoff, dt);
        float bodyCoefficient = OnePole(BodyCutoff, dt);
        float dcCoefficient = Mathf.Exp(-2f * Mathf.PI * DcCutoff * dt);
        float rpmCoefficient = 1f - Mathf.Exp(-dt / RpmSmoothing);
        float loadCoefficient = 1f - Mathf.Exp(-dt / LoadSmoothing);
        float crackleFade = Mathf.Exp(-dt / 0.012f);

        float goalRpm = targetRpm;
        float goalLoad = targetLoad;
        float goalChain = targetChainHz;
        bool limiter = limiterActive;

        for (int i = 0; i < data.Length; i++)
        {
            rpm += (goalRpm - rpm) * rpmCoefficient;
            load += (goalLoad - load) * loadCoefficient;
            chainHz += (goalChain - chainHz) * rpmCoefficient;

            // Dos tiempos: una explosión por vuelta.
            phase += rpm / 60f * dt * cycleScale;

            if (phase >= 1f)
            {
                phase -= 1f;
                StartCycle(limiter);
            }

            float white = (float)(random.NextDouble() * 2.0 - 1.0);

            // ---- 1. Escape ----
            // Soplido: sube de golpe al abrir la lumbrera y se apaga mientras
            // sigue abierta, con turbulencia mezclada.
            float blowdown = 0f;

            if (phase < ExhaustWindow)
            {
                float u = phase / ExhaustWindow / BlowdownPeak;
                blowdown = cycleStrength * u * Mathf.Exp(1f - u) * (1f + rasp * white);
            }

            // Petardazo: un estallido de ruido que se apaga en ~12 ms.
            blowdown += crackle * white;
            crackle *= crackleFade;

            // Cámara de expansión: la onda va hacia la salida, rebota invertida
            // (extremo abierto) y vuelve a rebotar contra la lumbrera.
            float forwardOut = pipeForward[pipeIndex];
            float backwardOut = pipeBackward[pipeIndex];

            pipeForward[pipeIndex] = blowdown + portReflection * backwardOut;
            pipeLoss += (forwardOut - pipeLoss) * pipeLossCoefficient;
            pipeBackward[pipeIndex] = -outletReflection * pipeLoss;

            pipeIndex = (pipeIndex + 1) % pipeDelay;

            float transmitted = (1f - outletReflection) * forwardOut;

            // Silenciador: dos filtros en serie (12 dB/octava) más lo que se escapa sin filtrar.
            silencer1 += (transmitted - silencer1) * silencerCoefficient;
            silencer2 += (silencer1 - silencer2) * silencerCoefficient;

            float exhaust = silencer2 + silencerLeak * transmitted;

            // ---- 2. Admisión ----
            // Ruido de aire que entra solo mientras el cárter aspira, y más
            // cuanto más abierto está el acelerador.
            float intakeEnvelope = 0f;
            float intakePhase = phase - IntakeStart;

            if (intakePhase < 0f)
                intakePhase += 1f;

            if (intakePhase < IntakeWindow)
                intakeEnvelope = Mathf.Sin(Mathf.PI * intakePhase / IntakeWindow);

            intakeNoise += (white - intakeNoise) * intakeCoefficient;

            float intakeFlow = intakeNoise * intakeEnvelope * (0.1f + 0.9f * load) * Mathf.Clamp01(rpm / 12000f);

            float intakeForwardOut = intakeForward[intakeIndex];
            float intakeBackwardOut = intakeBackward[intakeIndex];

            intakeForward[intakeIndex] = intakeFlow + 0.6f * intakeBackwardOut;
            intakeLoss += (intakeForwardOut - intakeLoss) * intakeCoefficient;
            intakeBackward[intakeIndex] = -0.5f * intakeLoss;

            intakeIndex = (intakeIndex + 1) % intakeDelay;

            airbox += (intakeForwardOut - airbox) * intakeCoefficient;

            // ---- 3. Mecánica ----
            // Golpe grave de cada explosión que transmite el block.
            body += (blowdown - body) * bodyCoefficient;

            // Chillido de la cadena: cada diente que entra en la corona.
            chainPhase += chainHz * dt;

            if (chainPhase >= 1f)
                chainPhase -= Mathf.Floor(chainPhase);

            float chain =
                (Mathf.Sin(2f * Mathf.PI * chainPhase) + 0.35f * Mathf.Sin(4f * Mathf.PI * chainPhase)) *
                Mathf.Clamp01(chainHz / 800f) * (0.4f + 0.6f * load);

            // ---- Mezcla ----
            float mix =
                exhaustLevel * exhaust * 2.2f +
                intakeLevel * airbox * 6f +
                mechanicalLevel * body * 0.3f +
                chainLevel * chain;

            // Sin componente continua (la presión media del escape no se escucha).
            float dc = mix - dcInput + dcCoefficient * dcOutput;
            dcInput = mix;
            dcOutput = dc;

            // Saturación suave, como un micrófono cerca de un escape.
            data[i] = (float)System.Math.Tanh(dc * 1.2f) * volume;
        }
    }

    /// <summary>Empieza una vuelta del cigüeñal: decide si hay explosión y con qué fuerza.</summary>
    private void StartCycle(bool limiter)
    {
        bool offThrottle = load < 0.1f;

        float misfire =
            limiter ? limiterMisfire :
            offThrottle && rpm > 4000f ? overrunMisfire :
            rpm < 3000f ? idleMisfire :
            0.01f;

        bool fires = random.NextDouble() >= misfire;

        float rpmFactor = Mathf.Clamp01((rpm - 2000f) / 12000f);
        float level = Mathf.Lerp(offThrottleLevel, 1f, load) * Mathf.Lerp(0.55f, 1f, rpmFactor);

        cycleStrength = fires
            ? level * (1f + roughness * (float)(random.NextDouble() * 2.0 - 1.0))
            : level * 0.06f;

        // Sin acelerar, la mezcla que no se quemó a veces explota en el caño.
        if (offThrottle && rpm > 4000f && random.NextDouble() < backfireChance)
            crackle = 0.8f;

        // Cada vuelta dura un poco distinto, como en un motor real.
        cycleScale = 1f + roughness * 0.04f * (float)(random.NextDouble() * 2.0 - 1.0);
    }

    /// <summary>Coeficiente de un filtro pasabajos de un polo.</summary>
    private static float OnePole(float cutoff, float dt)
    {
        return 1f - Mathf.Exp(-2f * Mathf.PI * cutoff * dt);
    }
}
