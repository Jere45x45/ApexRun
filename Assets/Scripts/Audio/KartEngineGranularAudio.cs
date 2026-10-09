using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sonido del motor con síntesis granular: arma el sonido en tiempo real con
/// pedacitos ("granos") de una grabación real. Es la técnica de los juegos de
/// carreras actuales.
///
/// - La grabación viene analizada en un EngineSoundBank: para cada instante se
///   sabe a cuántas rpm iba el motor y si estaba acelerando o soltando.
/// - Cada ~30–60 ms empieza un grano nuevo: se elige un pedacito grabado a
///   rpm parecidas a las del kart y se reproduce un poco más rápido o más lento
///   para que coincida exacto. Los granos se superponen con un fundido
///   (ventana de Hann), así no se escuchan los cortes.
/// - Hay dos capas: granos grabados acelerando y granos grabados soltando el
///   acelerador. El acelerador del kart mezcla las dos.
/// - Si el kart gira más rápido que lo que se grabó, se usan los granos más
///   altos acelerados (suena más agudo, como corresponde).
///
/// El sonido se genera en el hilo de audio (AudioClip en modo stream).
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class KartEngineGranularAudio : MonoBehaviour
{
    [SerializeField] private KartBehaviour kart;

    [SerializeField] private EngineSoundBank bank;

    [Header("Mezcla")]
    [SerializeField, Range(0f, 1f)] private float volume = 0.8f;

    [Tooltip("Volumen sin acelerar, relativo al motor a fondo.")]
    [SerializeField, Range(0f, 1f)] private float offThrottleVolume = 0.45f;

    [Tooltip("Volumen en ralentí, relativo al motor a fondo arriba.")]
    [SerializeField, Range(0f, 1f)] private float idleVolume = 0.5f;

    [Header("Granos")]
    [Tooltip("Largo de cada grano, en explosiones del motor grabado.")]
    [SerializeField, Range(1f, 8f)] private float grainCycles = 3f;

    [Tooltip("Largo mínimo y máximo de un grano (s).")]
    [SerializeField] private Vector2 grainLengthRange = new Vector2(0.025f, 0.09f);

    [Tooltip("Cuánto pueden diferir las rpm del grano elegido de las buscadas (fracción).")]
    [SerializeField, Range(0.01f, 0.2f)] private float rpmTolerance = 0.05f;

    [Tooltip("Cuánto se puede acelerar o frenar un grano para igualar las rpm.")]
    [SerializeField] private Vector2 pitchRange = new Vector2(0.5f, 2.5f);

    private const int MaxGrains = 8;
    private const float RpmSmoothing = 0.02f;
    private const float LoadSmoothing = 0.05f;

    // Volumen (RMS) al que se llevan los granos: las grabaciones suelen venir
    // bajas (~0,03) y el motor tiene que sonar al nivel del resto del juego.
    private const float TargetLevel = 0.2f;

    private AudioSource source;
    private int outputRate;
    private float sourceToOutput;

    // Muestras de la grabación y granos disponibles, ordenados por rpm.
    private float[] samples;
    private Candidate[] onLoadCandidates;
    private Candidate[] offLoadCandidates;
    private float loudnessReference;
    private float makeupGain = 1f;

    // Copias de datos del banco: el hilo de audio no puede tocar la API de Unity.
    private int clipRate;
    private float firingsPerRevolution;

    // Los escribe el hilo principal y los lee el hilo de audio.
    private volatile float targetRpm = 2000f;
    private volatile float targetLoad;
    private volatile bool running;

    // Estado del hilo de audio.
    private readonly System.Random random = new System.Random();
    private readonly Grain[] grains = new Grain[MaxGrains];
    private float rpm = 2000f;
    private float load;
    private int onLoadCountdown;
    private int offLoadCountdown;

    private struct Candidate
    {
        public float Rpm;
        public int Sample;
        public float Gain;
    }

    private struct Grain
    {
        public bool Active;
        public double Position;   // en muestras de la grabación
        public float Step;        // muestras de la grabación por muestra de salida
        public int Length;        // en muestras de salida
        public int Played;
        public float Gain;
        public bool OnLoadLayer;
    }

    private void Reset()
    {
        kart = GetComponentInParent<KartBehaviour>();
    }

    private void Awake()
    {
        source = GetComponent<AudioSource>();
        outputRate = AudioSettings.outputSampleRate;

        if (kart == null)
            kart = GetComponentInParent<KartBehaviour>();

        if (!PrepareBank())
        {
            enabled = false;
            return;
        }

        source.clip = AudioClip.Create("Motor (granular)", outputRate, 1, outputRate, true, GenerateAudio);
        source.loop = true;
    }

    private void OnEnable()
    {
        if (source != null && source.clip != null && !source.isPlaying)
            source.Play();
    }

    private void OnDisable()
    {
        if (source != null)
            source.Stop();
    }

    private void Update()
    {
        // Kart propio: rpm de su motor. Kart de otro jugador: las que manda por red.
        if (kart == null || !kart.HasEngineState)
        {
            running = false;
            return;
        }

        targetRpm = kart.EngineRpm;
        targetLoad = Mathf.Clamp01(kart.EngineThrottle);
        running = true;
    }

    /// <summary>Lee la grabación y arma las listas de granos por rpm.</summary>
    private bool PrepareBank()
    {
        if (bank == null || !bank.IsAnalyzed)
        {
            Debug.LogError("KartEngineGranularAudio necesita un EngineSoundBank analizado.", this);
            return false;
        }

        samples = bank.ReadMonoSamples();

        if (samples == null)
        {
            Debug.LogError("KartEngineGranularAudio: no se pudo leer la grabación (¿Decompress On Load?).", this);
            return false;
        }

        clipRate = bank.clip.frequency;
        firingsPerRevolution = bank.firingsPerRevolution;
        sourceToOutput = (float)clipRate / outputRate;

        List<Candidate> onLoad = new List<Candidate>();
        List<Candidate> offLoad = new List<Candidate>();
        List<float> levels = new List<float>();

        int stepSamples = Mathf.Max(1, Mathf.RoundToInt(bank.analysisStep * bank.clip.frequency));

        for (int f = 0; f < bank.frameRpm.Length; f++)
        {
            if (bank.frameRpm[f] > 0f)
                levels.Add(bank.frameLevel[f]);
        }

        if (levels.Count == 0)
        {
            Debug.LogError("KartEngineGranularAudio: el análisis no encontró ningún tramo útil.", this);
            return false;
        }

        levels.Sort();
        loudnessReference = levels[levels.Count / 2];
        makeupGain = TargetLevel / Mathf.Max(loudnessReference, 1e-4f);

        for (int f = 0; f < bank.frameRpm.Length; f++)
        {
            float frameRpm = bank.frameRpm[f];

            if (frameRpm <= 0f)
                continue;

            Candidate candidate = new Candidate
            {
                Rpm = frameRpm,
                Sample = f * stepSamples,
                // Todos los granos al mismo volumen; el volumen lo pone después
                // el estado del motor (acelerador y rpm).
                Gain = loudnessReference / Mathf.Max(bank.frameLevel[f], 1e-5f)
            };

            if (bank.frameLoad[f] == EngineSoundBank.OffLoad)
                offLoad.Add(candidate);
            else
                onLoad.Add(candidate);
        }

        onLoad.Sort((a, b) => a.Rpm.CompareTo(b.Rpm));
        offLoad.Sort((a, b) => a.Rpm.CompareTo(b.Rpm));

        onLoadCandidates = onLoad.ToArray();
        offLoadCandidates = offLoad.Count > 0 ? offLoad.ToArray() : onLoadCandidates;

        return onLoadCandidates.Length > 0;
    }

    /// <summary>Genera las muestras del clip. Corre en el hilo de audio.</summary>
    private void GenerateAudio(float[] data)
    {
        if (!running || samples == null)
        {
            System.Array.Clear(data, 0, data.Length);
            return;
        }

        float dt = 1f / outputRate;
        float rpmCoefficient = 1f - Mathf.Exp(-dt / RpmSmoothing);
        float loadCoefficient = 1f - Mathf.Exp(-dt / LoadSmoothing);

        float goalRpm = targetRpm;
        float goalLoad = targetLoad;

        for (int i = 0; i < data.Length; i++)
        {
            rpm += (goalRpm - rpm) * rpmCoefficient;
            load += (goalLoad - load) * loadCoefficient;

            // Cada capa lanza granos con superposición del 50 %.
            if (--onLoadCountdown <= 0)
                onLoadCountdown = StartGrain(true);

            if (--offLoadCountdown <= 0)
                offLoadCountdown = StartGrain(false);

            float onWeight = Mathf.Sqrt(load);
            float offWeight = Mathf.Sqrt(1f - load);
            float sum = 0f;

            for (int g = 0; g < MaxGrains; g++)
            {
                if (!grains[g].Active)
                    continue;

                Grain grain = grains[g];

                int index = (int)grain.Position;

                if (index + 1 >= samples.Length || grain.Played >= grain.Length)
                {
                    grains[g].Active = false;
                    continue;
                }

                float frac = (float)(grain.Position - index);
                float value = samples[index] + (samples[index + 1] - samples[index]) * frac;

                // Ventana de Hann: sube y baja suave; dos granos al 50 % suman 1.
                float window = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * grain.Played / grain.Length);

                sum += value * window * grain.Gain * (grain.OnLoadLayer ? onWeight : offWeight);

                grains[g].Position += grain.Step;
                grains[g].Played++;
            }

            float rpmFactor = Mathf.Clamp01((rpm - 2000f) / 10000f);
            float engineVolume = Mathf.Lerp(offThrottleVolume, 1f, load) * Mathf.Lerp(idleVolume, 1f, rpmFactor);

            data[i] = (float)System.Math.Tanh(sum * makeupGain * engineVolume * 1.5f) * volume;
        }
    }

    /// <summary>
    /// Empieza un grano en la capa pedida y devuelve en cuántas muestras de
    /// salida tiene que empezar el siguiente.
    /// </summary>
    private int StartGrain(bool onLoadLayer)
    {
        Candidate[] list = onLoadLayer ? onLoadCandidates : offLoadCandidates;
        Candidate chosen = Pick(list, rpm);

        float step = Mathf.Clamp(rpm / chosen.Rpm, pitchRange.x, pitchRange.y) * sourceToOutput;

        // Largo del grano: unas pocas explosiones del motor grabado.
        float sourceSeconds = Mathf.Clamp(
            grainCycles / (chosen.Rpm / 60f * firingsPerRevolution),
            grainLengthRange.x,
            grainLengthRange.y);

        int length = Mathf.Max(32, Mathf.RoundToInt(sourceSeconds * clipRate / step));

        for (int g = 0; g < MaxGrains; g++)
        {
            if (grains[g].Active)
                continue;

            // El pedacito queda centrado en el instante analizado.
            double start = chosen.Sample - 0.5 * length * step;

            grains[g] = new Grain
            {
                Active = true,
                Position = System.Math.Max(0.0, start),
                Step = step,
                Length = length,
                Played = 0,
                Gain = Mathf.Min(chosen.Gain, 4f),
                OnLoadLayer = onLoadLayer
            };

            break;
        }

        return Mathf.Max(16, length / 2);
    }

    /// <summary>
    /// Elige al azar uno de los granos con rpm cercanas a las buscadas, para que
    /// no se repita siempre el mismo pedacito.
    /// </summary>
    private Candidate Pick(Candidate[] list, float wantedRpm)
    {
        int low = 0;
        int high = list.Length - 1;

        while (low < high)
        {
            int middle = (low + high) / 2;

            if (list[middle].Rpm < wantedRpm)
                low = middle + 1;
            else
                high = middle;
        }

        int nearest = low;

        if (nearest > 0 && Mathf.Abs(list[nearest - 1].Rpm - wantedRpm) < Mathf.Abs(list[nearest].Rpm - wantedRpm))
            nearest--;

        // Vecinos dentro de la tolerancia (como mucho 12 de cada lado).
        float tolerance = Mathf.Max(list[nearest].Rpm, wantedRpm) * rpmTolerance;
        int from = nearest;
        int to = nearest;

        while (from > 0 && nearest - from < 12 && Mathf.Abs(list[from - 1].Rpm - list[nearest].Rpm) <= tolerance)
            from--;

        while (to < list.Length - 1 && to - nearest < 12 && Mathf.Abs(list[to + 1].Rpm - list[nearest].Rpm) <= tolerance)
            to++;

        return list[from + random.Next(to - from + 1)];
    }
}
