using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;

/// <summary>
/// Una vuelta grabada para un fantasma: el tiempo y, varias veces por segundo,
/// dónde estaba el kart (posición y rotación). Se reproduce interpolando entre
/// muestras, así no depende de la física (sirve aunque la física cambie).
/// - Archivo local (KartGhost): Write / Read, formato versión 1 (32 bytes por muestra).
/// - Tabla online (TimeTrialRecords): el mismo formato comprimido con GZip y en
///   Base64 (ToCompressedBase64 / FromCompressedBase64).
/// </summary>
public class GhostRecording
{
    public const int FormatVersion = 1;

    /// <summary>Más muestras que esto es un archivo roto.</summary>
    private const int MaxSamples = 100000;

    public struct Sample
    {
        public float Time;
        public Vector3 Position;
        public Quaternion Rotation;
    }

    public float LapTime;
    public Sample[] Samples;

    public float Duration => Samples != null && Samples.Length > 0 ? Samples[Samples.Length - 1].Time : 0f;

    /// <summary>Dónde estaba el kart en ese momento de la vuelta (interpola entre las dos muestras de al lado).</summary>
    public void Evaluate(float time, out Vector3 position, out Quaternion rotation)
    {
        int low = 0;
        int high = Samples.Length - 1;

        if (time <= Samples[0].Time)
        {
            position = Samples[0].Position;
            rotation = Samples[0].Rotation;
            return;
        }

        if (time >= Samples[high].Time)
        {
            position = Samples[high].Position;
            rotation = Samples[high].Rotation;
            return;
        }

        while (high - low > 1)
        {
            int middle = (low + high) / 2;

            if (Samples[middle].Time <= time)
                low = middle;
            else
                high = middle;
        }

        Sample a = Samples[low];
        Sample b = Samples[high];
        float k = Mathf.InverseLerp(a.Time, b.Time, time);

        position = Vector3.Lerp(a.Position, b.Position, k);
        rotation = Quaternion.Slerp(a.Rotation, b.Rotation, k);
    }

    // ------------------------------------------------------------------
    // Binario
    // ------------------------------------------------------------------

    public void Write(BinaryWriter writer)
    {
        writer.Write(FormatVersion);
        writer.Write(LapTime);
        writer.Write(Samples.Length);

        foreach (Sample sample in Samples)
        {
            writer.Write(sample.Time);
            writer.Write(sample.Position.x);
            writer.Write(sample.Position.y);
            writer.Write(sample.Position.z);
            writer.Write(sample.Rotation.x);
            writer.Write(sample.Rotation.y);
            writer.Write(sample.Rotation.z);
            writer.Write(sample.Rotation.w);
        }
    }

    /// <summary>Lee una vuelta, o null si la versión no es esta o los datos están rotos.</summary>
    public static GhostRecording Read(BinaryReader reader)
    {
        if (reader.ReadInt32() != FormatVersion)
            return null;

        var recording = new GhostRecording { LapTime = reader.ReadSingle() };
        int count = reader.ReadInt32();

        if (count < 2 || count > MaxSamples)
            return null;

        recording.Samples = new Sample[count];

        for (int i = 0; i < count; i++)
        {
            recording.Samples[i] = new Sample
            {
                Time = reader.ReadSingle(),
                Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                Rotation = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle())
            };
        }

        return recording;
    }

    // ------------------------------------------------------------------
    // Texto comprimido (para la nube)
    // ------------------------------------------------------------------

    public string ToCompressedBase64()
    {
        using (var memory = new MemoryStream())
        {
            using (var gzip = new GZipStream(memory, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            using (var writer = new BinaryWriter(gzip))
                Write(writer);

            return Convert.ToBase64String(memory.ToArray());
        }
    }

    /// <summary>La vuelta a partir del texto de la nube, o null si está rota.</summary>
    public static GhostRecording FromCompressedBase64(string text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        try
        {
            using (var memory = new MemoryStream(Convert.FromBase64String(text)))
            using (var gzip = new GZipStream(memory, CompressionMode.Decompress))
            using (var reader = new BinaryReader(gzip))
                return Read(reader);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("GhostRecording: fantasma roto (" + exception.Message + ").");
            return null;
        }
    }
}
