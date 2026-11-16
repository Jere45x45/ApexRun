using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Collections;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Graba la vista Game a MP4 (H.264) para armar tráilers.
///
/// Cada toma se graba cuadro por cuadro con Time.captureFramerate: el juego
/// avanza 1/fps por cuadro aunque la PC tarde más en dibujarlo, así el video
/// sale fluido en cualquier máquina. Con CaptureFps mayor que Fps la toma
/// queda en cámara lenta (240 = cuatro veces más lenta a 60 cuadros).
///
/// Las tomas de la repetición (RaceReplay) llevan su propio reloj: la
/// repetición usa el tiempo real, así que acá se le ajusta la velocidad en
/// cada cuadro para que avance justo lo que corresponde.
///
/// Se maneja desde código, en Play (por ejemplo, con el MCP de Unity):
///   TrailerRecorder.Run(new TrailerRecorder.Shot { Name = "toma", Frames = 300 });
/// El tamaño del video es el de la vista Game (elegir "Full HD (1920x1080)").
/// Está en una carpeta Editor: no entra en el juego compilado.
/// </summary>
public static class TrailerRecorder
{
    /// <summary>Una toma: cuántos cuadros y qué hacer antes, en cada cuadro y con la cámara.</summary>
    public class Shot
    {
        /// <summary>Nombre del archivo (sin .mp4).</summary>
        public string Name = "toma";

        /// <summary>Cuadros que se graban.</summary>
        public int Frames = 300;

        /// <summary>Cuadros que corren antes de grabar, a velocidad normal (para que se acomoden el humo, la cámara, etc.).</summary>
        public int Preroll = 30;

        /// <summary>Cuadros por segundo del juego mientras graba (0: los del video). Más que los del video = cámara lenta.</summary>
        public int CaptureFps;

        /// <summary>Con el sonido del juego (solo a velocidad normal).</summary>
        public bool Audio;

        /// <summary>Toma de la repetición: segundo de la repetición en el primer cuadro grabado (NaN: no toca la repetición).</summary>
        public float ReplayFrom = float.NaN;

        /// <summary>Velocidad de la repetición mientras graba (1 = normal).</summary>
        public float ReplaySpeed = 1f;

        /// <summary>Una vez, antes de los cuadros de espera.</summary>
        public Action Setup;

        /// <summary>En cada cuadro, antes de Update (los de espera llegan con número negativo).</summary>
        public Action<int> Tick;

        /// <summary>En cada cuadro, después de los Update y antes de los LateUpdate.</summary>
        public Action<int> Mid;

        /// <summary>
        /// En cada cuadro, justo antes de dibujar cada cámara que va a la pantalla
        /// (después de todos los LateUpdate): acá se acomoda la cámara.
        /// </summary>
        public Action<Camera, int> Cam;

        /// <summary>Una vez, al terminar.</summary>
        public Action Teardown;
    }

    /// <summary>Carpeta de los videos (relativa al proyecto, fuera de Assets).</summary>
    public static string Folder = "Builds/itch/trailer/tomas";

    public static int Fps = 60;
    public static int BitrateMbps = 40;

    /// <summary>
    /// Vista previa: en vez del video guarda una tira con tres fotos chicas de
    /// la toma (principio, medio y final) en la carpeta "previas". Corre rápido:
    /// las tomas en cámara lenta saltean cuadros.
    /// </summary>
    public static bool Preview;

    /// <summary>Tomas guardadas por nombre, para verlas en vista previa y después grabarlas sin volver a escribirlas.</summary>
    public static readonly Dictionary<string, Shot> Bank = new Dictionary<string, Shot>();

    public static string Status { get; private set; } = "libre";
    public static bool Busy => current != null || queue.Count > 0;
    public static int Frame { get; private set; }
    public static int Done { get; private set; }
    public static string Log => log.ToString();

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly Queue<Shot> queue = new Queue<Shot>();
    private static readonly System.Text.StringBuilder log = new System.Text.StringBuilder();

    private static Shot current;
    private static MonoBehaviour host;
    private static MediaEncoder encoder;
    private static Texture2D frameTexture;
    private static bool audioOn;
    private static bool abort;
    private static bool hooked;
    private static bool camFailed;
    private static string tempPath;
    private static string finalPath;

    // ------------------------------------------------------------------
    // Cola
    // ------------------------------------------------------------------

    /// <summary>Pone las tomas en la cola y arranca (si no estaba grabando).</summary>
    public static void Run(params Shot[] shots)
    {
        if (!Application.isPlaying)
        {
            Status = "error: solo en Play";
            return;
        }

        foreach (Shot shot in shots)
        {
            if (shot != null)
                queue.Enqueue(shot);
        }

        abort = false;

        if (current != null)
            return;

        if (host == null)
        {
            var go = new GameObject("TrailerRecorder");
            UnityEngine.Object.DontDestroyOnLoad(go);
            host = go.AddComponent<EventTrigger>();
        }

        host.StartCoroutine(Loop());
    }

    /// <summary>Corta la toma que se está grabando y vacía la cola.</summary>
    public static void Abort()
    {
        abort = true;
        queue.Clear();
    }

    public static void ClearLog()
    {
        log.Length = 0;
        Done = 0;
    }

    public static void Note(string text)
    {
        log.Append(text).Append('\n');
    }

    /// <summary>Guarda las tomas en el banco (por nombre).</summary>
    public static void Keep(params Shot[] shots)
    {
        foreach (Shot shot in shots)
        {
            if (shot != null)
                Bank[shot.Name] = shot;
        }
    }

    /// <summary>Graba (o muestra en vista previa) tomas del banco, por nombre.</summary>
    public static string RunBank(bool preview, params string[] names)
    {
        var shots = new List<Shot>();
        string missing = "";

        foreach (string name in names)
        {
            if (Bank.TryGetValue(name, out Shot shot))
                shots.Add(shot);
            else
                missing += name + " ";
        }

        Preview = preview;
        Run(shots.ToArray());
        return missing.Length > 0 ? "faltan: " + missing : "ok " + shots.Count;
    }

    private static IEnumerator Loop()
    {
        var endOfFrame = new WaitForEndOfFrame();

        while (queue.Count > 0 && !abort)
        {
            current = queue.Dequeue();
            camFailed = false;
            Frame = -current.Preroll;
            Status = "preparando " + current.Name;

            string error = null;
            bool replayShot = !float.IsNaN(current.ReplayFrom);
            bool preview = Preview;
            int captureFps = current.CaptureFps > 0 ? current.CaptureFps : Fps;

            // En vista previa las tomas en cámara lenta saltean cuadros (se ve lo mismo, más rápido).
            int stride = preview ? Mathf.Max(1, captureFps / Fps) : 1;
            float replayAtZero = float.NaN;
            int lastFrame = ((current.Frames - 1) / stride) * stride;
            int middleFrame = ((current.Frames / 2) / stride) * stride;

            try
            {
                current.Setup?.Invoke();

                if (replayShot)
                    ReplayBegin(current.ReplayFrom - current.Preroll / (float)Fps);
            }
            catch (Exception exception)
            {
                error = "Setup: " + exception;
            }

            if (error == null)
            {
                Hook();
                audioOn = false;
                int channels = Channels();

                for (int f = -current.Preroll; f < current.Frames && !abort && error == null; f += f < 0 ? 1 : stride)
                {
                    Frame = f;
                    Status = (f < 0 ? "esperando " : preview ? "previa " : "grabando ") + current.Name + " " + f + "/" + current.Frames;

                    // Los cuadros de espera, a velocidad normal; los grabados, a la de la toma.
                    int fps = f < 0 ? Fps : captureFps / stride;
                    Time.captureFramerate = fps;

                    if (f == 0 && current.Audio && captureFps == Fps && !preview)
                        audioOn = AudioRenderer.Start();

                    try
                    {
                        current.Tick?.Invoke(f);
                    }
                    catch (Exception exception)
                    {
                        error = "Tick " + f + ": " + exception;
                        break;
                    }

                    // Cuadro nuevo: después de los Update, antes de los LateUpdate.
                    yield return null;

                    try
                    {
                        if (replayShot)
                        {
                            if (f == 0)
                                replayAtZero = ReplayTime;

                            ReplayStep(f < 0 ? 1f / Fps : current.ReplaySpeed * stride / captureFps);
                        }

                        current.Mid?.Invoke(f);
                    }
                    catch (Exception exception)
                    {
                        error = "Mid " + f + ": " + exception;
                        break;
                    }

                    yield return endOfFrame;

                    try
                    {
                        if (preview)
                        {
                            if (f == 0 || f == middleFrame || f == lastFrame)
                                PreviewFrame(f == 0 ? 0 : f == middleFrame && middleFrame != lastFrame ? 1 : 2);

                            continue;
                        }

                        if (f >= 0 && encoder == null)
                            OpenEncoder(current.Name, channels);

                        if (audioOn)
                        {
                            int samples = AudioRenderer.GetSampleCountForCaptureFrame();
                            var buffer = new NativeArray<float>(samples * channels, Allocator.Temp, NativeArrayOptions.ClearMemory);
                            AudioRenderer.Render(buffer);
                            encoder.AddSamples(buffer);
                            buffer.Dispose();
                        }

                        if (f >= 0)
                        {
                            frameTexture.ReadPixels(new Rect(0, 0, frameTexture.width, frameTexture.height), 0, 0, false);
                            encoder.AddFrame(frameTexture);
                        }
                    }
                    catch (Exception exception)
                    {
                        error = "Cuadro " + f + ": " + exception;
                    }
                }

                if (audioOn)
                    AudioRenderer.Stop();

                audioOn = false;
                Time.captureFramerate = 0;
                Unhook();
            }

            string file = preview ? SavePreview(current.Name) : CloseEncoder(error == null && !abort);
            string replayInfo = "";

            if (replayShot)
            {
                try
                {
                    replayInfo = " rep " + replayAtZero.ToString("0.000") + " a " + ReplayTime.ToString("0.000");
                    ReplayEnd();
                }
                catch (Exception exception)
                {
                    error = (error ?? "") + " ReplayEnd: " + exception;
                }
            }

            try
            {
                current.Teardown?.Invoke();
            }
            catch (Exception exception)
            {
                error = (error ?? "") + " Teardown: " + exception;
            }

            if (error != null)
                log.Append("ERROR ").Append(current.Name).Append(": ").Append(error).Append('\n');
            else if (abort)
                log.Append("CORTADA ").Append(current.Name).Append('\n');
            else
            {
                log.Append(preview ? "PREVIA " : "OK ").Append(current.Name).Append(replayInfo).Append('\n');
                Done++;
            }

            current = null;
        }

        current = null;
        Status = abort ? "cortado" : "libre";
    }

    private static int Channels()
    {
        switch (AudioSettings.speakerMode)
        {
            case AudioSpeakerMode.Mono: return 1;
            case AudioSpeakerMode.Quad: return 4;
            case AudioSpeakerMode.Surround: return 5;
            case AudioSpeakerMode.Mode5point1: return 6;
            case AudioSpeakerMode.Mode7point1: return 8;
            default: return 2;
        }
    }

    // ------------------------------------------------------------------
    // Video
    // ------------------------------------------------------------------

    private static void OpenEncoder(string name, int channels)
    {
        int width = Screen.width;
        int height = Screen.height;

        string folder = Path.GetFullPath(Folder);
        Directory.CreateDirectory(folder);
        finalPath = Path.Combine(folder, name + ".mp4");
        tempPath = Path.Combine(folder, name + ".parcial.mp4");

        var h264 = new H264EncoderAttributes
        {
            gopSize = (uint)Mathf.Max(1, Fps / 2),
            numConsecutiveBFrames = 2,
            profile = VideoEncodingProfile.H264High
        };

        var video = new VideoTrackEncoderAttributes(h264)
        {
            frameRate = new MediaRational(Fps),
            width = (uint)width,
            height = (uint)height,
            includeAlpha = false,
            bitRateMode = VideoBitrateMode.High,
            targetBitRate = (uint)(BitrateMbps * 1000000)
        };

        if (audioOn)
        {
            var audio = new AudioTrackAttributes
            {
                sampleRate = new MediaRational(AudioSettings.outputSampleRate),
                channelCount = (ushort)channels,
                language = ""
            };

            encoder = new MediaEncoder(tempPath, video, audio);
        }
        else
        {
            encoder = new MediaEncoder(tempPath, video);
        }

        if (frameTexture == null || frameTexture.width != width || frameTexture.height != height)
        {
            if (frameTexture != null)
                UnityEngine.Object.Destroy(frameTexture);

            frameTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        }
    }

    /// <summary>Cierra el video. Si salió bien le pone el nombre final; devuelve la ruta.</summary>
    private static string CloseEncoder(bool ok)
    {
        if (encoder == null)
            return null;

        encoder.Dispose();
        encoder = null;

        if (!ok || tempPath == null || !File.Exists(tempPath))
            return tempPath;

        // El nombre bueno, recién al terminar: un video cortado no pisa la toma anterior.
        if (File.Exists(finalPath))
            File.Delete(finalPath);

        File.Move(tempPath, finalPath);
        return finalPath;
    }

    private const int PreviewWidth = 640;
    private const int PreviewHeight = 360;
    private static Texture2D previewSheet;
    private static Texture2D previewFull;

    /// <summary>Copia la pantalla, achicada, a una de las tres casillas de la tira.</summary>
    private static void PreviewFrame(int slot)
    {
        int width = Screen.width;
        int height = Screen.height;

        if (previewFull == null || previewFull.width != width || previewFull.height != height)
            previewFull = new Texture2D(width, height, TextureFormat.RGB24, false);

        if (previewSheet == null)
            previewSheet = new Texture2D(PreviewWidth * 3, PreviewHeight, TextureFormat.RGB24, false);

        previewFull.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
        previewFull.Apply(false);

        RenderTexture small = RenderTexture.GetTemporary(PreviewWidth, PreviewHeight, 0);
        RenderTexture before = RenderTexture.active;
        Graphics.Blit(previewFull, small);
        RenderTexture.active = small;
        previewSheet.ReadPixels(new Rect(0, 0, PreviewWidth, PreviewHeight), slot * PreviewWidth, 0, false);
        RenderTexture.active = before;
        RenderTexture.ReleaseTemporary(small);
    }

    private static string SavePreview(string name)
    {
        if (previewSheet == null)
            return null;

        string folder = Path.GetFullPath(Path.Combine(Folder, "..", "previas"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name + ".jpg");
        File.WriteAllBytes(path, previewSheet.EncodeToJPG(82));
        return path;
    }

    private static void Hook()
    {
        if (hooked)
            return;

        // Adelante de los demás (el reflejo de la pista mojada se calcula en
        // onPreCull con la cámara ya acomodada).
        Camera.onPreCull = (Camera.CameraCallback)Delegate.Combine(new Camera.CameraCallback(PreCull), Camera.onPreCull);
        hooked = true;
    }

    private static void Unhook()
    {
        if (!hooked)
            return;

        Camera.onPreCull -= PreCull;
        hooked = false;
    }

    private static void PreCull(Camera camera)
    {
        if (current == null || current.Cam == null || camFailed)
            return;

        if (camera == null || camera.cameraType != CameraType.Game || camera.targetTexture != null)
            return;

        try
        {
            current.Cam(camera, Frame);
        }
        catch (Exception exception)
        {
            camFailed = true;
            log.Append("ERROR cámara ").Append(current.Name).Append(": ").Append(exception).Append('\n');
        }
    }

    /// <summary>Pone la cámara en un lugar mirando a un punto.</summary>
    public static void Look(Camera camera, Vector3 position, Vector3 target, float fieldOfView, float roll = 0f)
    {
        Vector3 direction = target - position;

        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector3.forward;

        camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction) * Quaternion.Euler(0f, 0f, roll));
        camera.fieldOfView = fieldOfView;
    }

    // ------------------------------------------------------------------
    // Repetición (RaceReplay por dentro)
    // ------------------------------------------------------------------

    private static RaceReplay replay;
    private static float[] speedsBackup;

    public static RaceReplay Replay
    {
        get
        {
            if (replay == null)
                replay = UnityEngine.Object.FindFirstObjectByType<RaceReplay>();

            return replay;
        }
    }

    public static object Get(object target, string field)
    {
        return target.GetType().GetField(field, Any).GetValue(target);
    }

    public static void Set(object target, string field, object value)
    {
        target.GetType().GetField(field, Any).SetValue(target, value);
    }

    public static object Call(object target, string method, params object[] arguments)
    {
        return target.GetType().GetMethod(method, Any).Invoke(target, arguments);
    }

    public static float ReplayTime => (float)Get(Replay, "time");

    public static float ReplayDuration => (float)typeof(RaceReplay).GetProperty("Duration", Any).GetValue(Replay, null);

    public static Camera ReplayCamera => (Camera)Get(Replay, "replayCamera");

    private static IList Actors => (IList)Get(Replay, "actors");

    public static int ActorCount => Actors.Count;

    /// <summary>La copia del kart número i de la repetición.</summary>
    public static Transform Actor(int index)
    {
        return (Transform)Get(Actors[index], "Root");
    }

    public static string ActorName(int index)
    {
        return (string)Get(Get(Actors[index], "Track"), "Name");
    }

    private static float[] Speeds => (float[])typeof(RaceReplay).GetField("Speeds", Any).GetValue(null);

    /// <summary>Lleva la repetición a ese segundo (aunque sea antes de donde suele empezar) y la deja lista.</summary>
    public static void ReplayBegin(float time)
    {
        if (speedsBackup == null)
            speedsBackup = (float[])Speeds.Clone();

        if (time < (float)Get(Replay, "startTime"))
            Set(Replay, "startTime", time);

        Call(Replay, "Seek", time);
        Set(Replay, "playing", true);
    }

    /// <summary>Que la repetición avance justo esos segundos en este cuadro (ella multiplica por el tiempo real).</summary>
    private static void ReplayStep(float seconds)
    {
        float[] speeds = Speeds;
        int index = (int)Get(Replay, "speedIndex");
        speeds[index] = seconds / Mathf.Max(0.0001f, Time.unscaledDeltaTime);
        Set(Replay, "playing", true);
    }

    /// <summary>Deja la repetición en pausa, con sus velocidades de siempre.</summary>
    public static void ReplayEnd()
    {
        if (speedsBackup != null)
            Array.Copy(speedsBackup, Speeds, speedsBackup.Length);

        if (Replay != null)
            Set(Replay, "playing", false);
    }
}
