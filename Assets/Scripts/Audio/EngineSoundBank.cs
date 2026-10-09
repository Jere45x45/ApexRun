using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Una grabación real del motor, analizada para el sonido granular: para cada
/// instante guarda a cuántas rpm estaba el motor y si estaba acelerando
/// (con carga) o soltando el acelerador.
///
/// Cómo se usa: crear el asset (Create > ApexRun > Audio > Engine Sound Bank),
/// asignarle la grabación y hacer clic derecho > "Analizar grabación". El
/// análisis busca la frecuencia de las explosiones (un dos tiempos explota una
/// vez por vuelta: rpm = frecuencia × 60) y descarta lo que no se puede medir
/// con seguridad (silencios, viento, cambios de marcha).
///
/// La grabación tiene que importarse con Load Type = Decompress On Load para
/// poder leer sus muestras en el juego.
/// </summary>
[CreateAssetMenu(fileName = "EngineSoundBank", menuName = "ApexRun/Audio/Engine Sound Bank")]
public class EngineSoundBank : ScriptableObject
{
    public const sbyte OffLoad = -1;
    public const sbyte OnLoad = 1;

    [Tooltip("Grabación del motor, idealmente arriba del vehículo.")]
    public AudioClip clip;

    [Tooltip("Explosiones por vuelta del cigüeñal: 1 en un dos tiempos de un cilindro, 0,5 en un cuatro tiempos de un cilindro.")]
    [Min(0.1f)] public float firingsPerRevolution = 1f;

    [Tooltip("Rango de rpm en el que se buscan las explosiones.")]
    public Vector2 detectionRpmRange = new Vector2(1500f, 16000f);

    [Tooltip("Cada cuánto se mide (s).")]
    [Range(0.005f, 0.05f)] public float analysisStep = 0.01f;

    [Tooltip("Cuánto tienen que subir o bajar las rpm (rpm/s) para que un tramo cuente como acelerando o soltando.")]
    [Min(0f)] public float loadSlopeThreshold = 400f;

    [Header("Resultado del análisis")]
    [Tooltip("Rpm de cada paso de análisis. 0 = no se pudo medir.")]
    public float[] frameRpm = new float[0];

    [Tooltip("Volumen (RMS) de cada paso.")]
    public float[] frameLevel = new float[0];

    [Tooltip("1 = acelerando (con carga), -1 = soltando el acelerador.")]
    public sbyte[] frameLoad = new sbyte[0];

    public Vector2 measuredRpmRange;

    public bool IsAnalyzed => clip != null && frameRpm != null && frameRpm.Length > 0;

    /// <summary>Mezcla los canales de la grabación en uno solo.</summary>
    public float[] ReadMonoSamples()
    {
        if (clip == null)
            return null;

        // Recién abierto el editor (o después de recompilar) la grabación puede
        // no estar cargada todavía, y GetData devolvería silencio.
        if (clip.loadState != AudioDataLoadState.Loaded)
            clip.LoadAudioData();

        int channels = clip.channels;
        float[] raw = new float[clip.samples * channels];

        if (!clip.GetData(raw, 0))
            return null;

        float[] mono = new float[clip.samples];

        for (int i = 0; i < mono.Length; i++)
        {
            float sum = 0f;

            for (int c = 0; c < channels; c++)
                sum += raw[i * channels + c];

            mono[i] = sum / channels;
        }

        return mono;
    }

#if UNITY_EDITOR
    [ContextMenu("Analizar grabación")]
    private void Analyze()
    {
        float[] mono = ReadMonoSamples();

        if (mono == null)
        {
            Debug.LogError("EngineSoundBank: no se pudieron leer las muestras. ¿La grabación está en Decompress On Load?", this);
            return;
        }

        int sampleRate = clip.frequency;

        // Pasabajos a 1,5 kHz y a la mitad de muestras: alcanza para medir la
        // frecuencia de las explosiones y es el doble de rápido.
        const int Decimation = 2;
        float lowPass = 1f - Mathf.Exp(-2f * Mathf.PI * 1500f / sampleRate);
        float stage1 = 0f;
        float stage2 = 0f;

        float[] signal = new float[mono.Length / Decimation];

        for (int i = 0, k = 0; i < mono.Length && k < signal.Length; i++)
        {
            stage1 += (mono[i] - stage1) * lowPass;
            stage2 += (stage1 - stage2) * lowPass;

            if (i % Decimation == 0)
                signal[k++] = stage2;
        }

        int rate = sampleRate / Decimation;
        float minHz = detectionRpmRange.x / 60f * firingsPerRevolution;
        float maxHz = detectionRpmRange.y / 60f * firingsPerRevolution;

        int tauMin = Mathf.Max(2, Mathf.FloorToInt(rate / maxHz));
        int tauMax = Mathf.CeilToInt(rate / minHz);
        int window = tauMax * 2;
        int hop = Mathf.Max(1, Mathf.RoundToInt(analysisStep * rate));

        int frameCount = Mathf.Max(0, (signal.Length - window - tauMax) / hop);

        float[] rpm = new float[frameCount];
        float[] level = new float[frameCount];
        float[] difference = new float[tauMax + 1];
        float[] normalized = new float[tauMax + 1];

        for (int f = 0; f < frameCount; f++)
        {
            int start = f * hop;

            double energy = 0.0;

            for (int i = 0; i < window; i++)
                energy += signal[start + i] * signal[start + i];

            level[f] = (float)System.Math.Sqrt(energy / window);

            // YIN: diferencia de la señal consigo misma corrida tau muestras.
            // Donde la diferencia es mínima, tau es el período de las explosiones.
            for (int tau = 1; tau <= tauMax; tau++)
            {
                double sum = 0.0;

                for (int i = 0; i < window; i += 2)
                {
                    float d = signal[start + i] - signal[start + i + tau];
                    sum += d * d;
                }

                difference[tau] = (float)sum;
            }

            double running = 0.0;
            normalized[0] = 1f;

            for (int tau = 1; tau <= tauMax; tau++)
            {
                running += difference[tau];
                normalized[tau] = running > 0.0 ? (float)(difference[tau] * tau / running) : 1f;
            }

            int best = -1;

            for (int tau = tauMin; tau <= tauMax; tau++)
            {
                if (normalized[tau] < 0.25f)
                {
                    while (tau + 1 <= tauMax && normalized[tau + 1] < normalized[tau])
                        tau++;

                    best = tau;
                    break;
                }
            }

            // Si el mínimo quedó pegado al borde del rango, el motor en realidad
            // gira más rápido que lo que se busca (o es un error): no se usa.
            if (best == tauMin)
                best = -1;

            rpm[f] = best > 0 ? rate / (float)best * 60f / firingsPerRevolution : 0f;
        }

        // Descarta lo que no es confiable: muy bajo de volumen o saltos aislados.
        float loudest = 0f;

        foreach (float l in level)
            loudest = Mathf.Max(loudest, l);

        float quietLimit = loudest * 0.06f; // ~24 dB por debajo de lo más fuerte

        float[] cleaned = new float[frameCount];

        for (int f = 0; f < frameCount; f++)
        {
            if (rpm[f] <= 0f || level[f] < quietLimit)
                continue;

            float median = Median(rpm, f - 3, f + 3);

            if (median > 0f && Mathf.Abs(rpm[f] - median) / median < 0.06f)
                cleaned[f] = rpm[f];
        }

        // Acelerando o soltando: pendiente de las rpm en ±50 ms.
        int span = Mathf.Max(1, Mathf.RoundToInt(0.05f / analysisStep));
        sbyte[] load = new sbyte[frameCount];
        float[] measured = (float[])cleaned.Clone();
        float minRpm = float.MaxValue;
        float maxRpm = 0f;
        int valid = 0;

        for (int f = 0; f < frameCount; f++)
        {
            if (cleaned[f] <= 0f)
                continue;

            int a = Mathf.Max(0, f - span);
            int b = Mathf.Min(frameCount - 1, f + span);

            // Se lee de la copia: si no, al descartar un paso se descartarían en cadena los siguientes.
            if (measured[a] <= 0f || measured[b] <= 0f)
            {
                cleaned[f] = 0f;
                continue;
            }

            float slope = (measured[b] - measured[a]) / ((b - a) * analysisStep);

            load[f] = slope < -loadSlopeThreshold ? OffLoad : OnLoad;

            minRpm = Mathf.Min(minRpm, cleaned[f]);
            maxRpm = Mathf.Max(maxRpm, cleaned[f]);
            valid++;
        }

        UnityEditor.Undo.RecordObject(this, "Analizar grabación");

        frameRpm = cleaned;
        frameLevel = level;
        frameLoad = load;
        measuredRpmRange = valid > 0 ? new Vector2(minRpm, maxRpm) : Vector2.zero;

        UnityEditor.EditorUtility.SetDirty(this);

        int onLoad = 0;

        foreach (sbyte l in load)
            if (l == OnLoad) onLoad++;

        Debug.Log(
            $"EngineSoundBank '{name}': {valid} de {frameCount} pasos medidos ({valid * analysisStep:F1} s), " +
            $"{measuredRpmRange.x:F0}–{measuredRpmRange.y:F0} rpm, {onLoad} acelerando y {valid - onLoad} soltando.",
            this);
    }

    private static float Median(float[] values, int from, int to)
    {
        List<float> window = new List<float>();

        for (int i = Mathf.Max(0, from); i <= Mathf.Min(values.Length - 1, to); i++)
        {
            if (values[i] > 0f)
                window.Add(values[i]);
        }

        if (window.Count == 0)
            return 0f;

        window.Sort();
        return window[window.Count / 2];
    }
#endif
}
