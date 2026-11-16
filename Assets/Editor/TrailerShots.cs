using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Las tomas del tráiler: las cámaras, la lista de tomas y las sesiones de
/// grabación (cada sesión es un clima y una hora). Usa TrailerRecorder.
///
/// De dónde salen las imágenes:
/// - Carrera: una carrera de 6 bots grabada una vez y guardada en
///   Builds/itch/trailer/datos/carrera1_suave.txt. Se ve con la repetición de
///   la tabla (RaceReplay), así se le puede cambiar el clima, la hora y las
///   piezas de cada kart sin volver a correrla.
/// - Taller: el Catálogo de verdad, apretando las pestañas y las piezas.
/// - Largada con HUD: una carrera de verdad; el kart del jugador acelera solo.
///
/// Uso (en Play, desde el Inicio):
///   TrailerShots.Queue(TrailerShots.Viewer(WeatherScenario.Clear, TimeOfDay.Noon, "", "a_grilla", "g_persecucion"));
///   TrailerShots.Info dice por dónde va.
/// Está en una carpeta Editor: no entra en el juego compilado.
/// </summary>
public static class TrailerShots
{
    // ------------------------------------------------------------------
    // Pista
    // ------------------------------------------------------------------

    private static readonly RaycastHit[] hits = new RaycastHit[32];
    private static TrackCenterline centerline;

    private static string PlayerName => PlayerPrefs.GetString("ApexRun.NombreJugador", "Jere");

    /// <summary>Altura del piso (asfalto o pasto) debajo de un punto.</summary>
    public static float Ground(Vector3 point)
    {
        int count = Physics.RaycastNonAlloc(new Vector3(point.x, point.y + 40f, point.z), Vector3.down, hits, 120f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;

        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider.GetComponent<TrackSurface>() != null && hits[i].point.y > best)
                best = hits[i].point.y;
        }

        return float.IsNegativeInfinity(best) ? point.y : best;
    }

    /// <summary>Punto de la pista: metros desde la meta, metros a la derecha (negativo: izquierda) y altura sobre el piso.</summary>
    public static Vector3 TrackPoint(float distance, float lateral, float height)
    {
        if (centerline == null)
            centerline = UnityEngine.Object.FindFirstObjectByType<TrackCenterline>();

        distance = Mathf.Repeat(distance, centerline.Length);
        Vector3 center = centerline.GetPointAtDistance(distance);
        Vector3 direction = Vector3.ProjectOnPlane(centerline.GetDirectionAtDistance(distance), Vector3.up).normalized;
        Vector3 point = center + Vector3.Cross(Vector3.up, direction) * lateral;
        point.y = Ground(point) + height;
        return point;
    }

    private static Vector3 TrackPoint(Vector3 track) => TrackPoint(track.x, track.y, track.z);

    // ------------------------------------------------------------------
    // Cámara
    // ------------------------------------------------------------------

    /// <summary>
    /// Una cámara: sigue a un kart (o al medio entre dos) desde un lugar fijo
    /// respecto del kart, o lo mira desde un punto de la pista. Lo que termina
    /// en 1 es el valor al final de la toma (va de uno a otro).
    /// </summary>
    public class Rig
    {
        /// <summary>Kart que sigue (0 = el primero de la grilla) y, si hay, el segundo (mira al medio).</summary>
        public int A;
        public int B = -1;
        public float Mix = 0.5f;

        /// <summary>Lugar de la cámara respecto del kart: derecha, arriba, adelante (m).</summary>
        public Vector3 Off0 = new Vector3(0f, 1.9f, -5f);
        public Vector3? Off1;

        /// <summary>Cámara fija en la pista: metros desde la meta, metros a la derecha, altura.</summary>
        public Vector3? Track0;
        public Vector3? Track1;

        /// <summary>A dónde mira, respecto del kart.</summary>
        public Vector3 Look0 = new Vector3(0f, 0.4f, 0f);
        public Vector3? Look1;

        /// <summary>Mira a un punto de la pista (en vez de al kart).</summary>
        public Vector3? LookTrack0;
        public Vector3? LookTrack1;

        /// <summary>Mira al semáforo, corrido esto.</summary>
        public Vector3? LookLights0;
        public Vector3? LookLights1;

        public float Fov0 = 60f;
        public float? Fov1;
        public float Roll0;
        public float? Roll1;

        /// <summary>Zoom automático: metros que entran de alto a la distancia del kart (0: usa Fov).</summary>
        public float Zoom;
        public float FovMin = 8f;
        public float FovMax = 70f;

        /// <summary>Suavizado del rumbo y de la posición (s), temblor (grados), altura mínima sobre el piso (m).</summary>
        public float Smooth = 0.25f;
        public float PosSmooth = 0.05f;
        public float Shake = 0.12f;
        public float MinHeight = 0.28f;
        public float Seed = 3.7f;

        /// <summary>0 parejo, 1 suave en las puntas, 2 frena al final.</summary>
        public int Ease = 1;

        public Action<Camera, int> Build(int frames, float dt)
        {
            bool fixedCamera = Track0.HasValue;
            Vector3 pos0 = fixedCamera ? TrackPoint(Track0.Value) : Vector3.zero;
            Vector3 pos1 = Track1.HasValue ? TrackPoint(Track1.Value) : pos0;

            bool worldLook = LookTrack0.HasValue || LookLights0.HasValue;
            Vector3 lookWorld0 = Vector3.zero;
            Vector3 lookWorld1 = Vector3.zero;

            if (LookLights0.HasValue)
            {
                Vector3 lights = UnityEngine.Object.FindFirstObjectByType<StartLights>().transform.position;
                lookWorld0 = lights + LookLights0.Value;
                lookWorld1 = lights + (LookLights1 ?? LookLights0.Value);
            }
            else if (LookTrack0.HasValue)
            {
                lookWorld0 = TrackPoint(LookTrack0.Value);
                lookWorld1 = LookTrack1.HasValue ? TrackPoint(LookTrack1.Value) : lookWorld0;
            }

            Vector3 off1 = Off1 ?? Off0;
            Vector3 look1 = Look1 ?? Look0;
            float fov1 = Fov1 ?? Fov0;
            float roll1 = Roll1 ?? Roll0;

            bool started = false;
            Vector3 smoothPosition = Vector3.zero;
            Vector3 smoothForward = Vector3.forward;
            int last = int.MinValue;
            float clock = 0f;
            Vector3 cameraPosition = Vector3.zero;
            Vector3 lookAt = Vector3.forward;
            float fov = 60f;
            float roll = 0f;

            return (camera, frame) =>
            {
                if (frame != last)
                {
                    float elapsed = !started ? 0f : frame < 0 ? 1f / 60f : dt * Mathf.Max(1, frame - last);
                    last = frame;
                    clock += elapsed;

                    Transform kartA = TrailerRecorder.Actor(A);
                    Vector3 position = kartA.position;
                    Vector3 forward = kartA.forward;

                    if (B >= 0)
                    {
                        Transform kartB = TrailerRecorder.Actor(B);
                        position = Vector3.Lerp(position, kartB.position, Mix);
                        forward = Vector3.Slerp(forward, kartB.forward, Mix);
                    }

                    forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;

                    if (!started)
                    {
                        smoothPosition = position;
                        smoothForward = forward;
                        started = true;
                    }
                    else
                    {
                        smoothPosition = Vector3.Lerp(smoothPosition, position, 1f - Mathf.Exp(-elapsed / Mathf.Max(0.001f, PosSmooth)));
                        smoothForward = Vector3.Slerp(smoothForward, forward, 1f - Mathf.Exp(-elapsed / Mathf.Max(0.001f, Smooth))).normalized;
                    }

                    float u = frames > 1 ? Mathf.Clamp01(frame / (float)(frames - 1)) : 0f;

                    if (Ease == 1)
                        u = u * u * (3f - 2f * u);
                    else if (Ease == 2)
                        u = 1f - (1f - u) * (1f - u);

                    Vector3 right = Vector3.Cross(Vector3.up, smoothForward);
                    Vector3 offset = Vector3.Lerp(Off0, off1, u);
                    Vector3 look = Vector3.Lerp(Look0, look1, u);

                    cameraPosition = fixedCamera
                        ? Vector3.Lerp(pos0, pos1, u)
                        : smoothPosition + right * offset.x + Vector3.up * offset.y + smoothForward * offset.z;

                    lookAt = worldLook
                        ? Vector3.Lerp(lookWorld0, lookWorld1, u)
                        : smoothPosition + right * look.x + Vector3.up * look.y + smoothForward * look.z;

                    float ground = Ground(cameraPosition);

                    if (cameraPosition.y < ground + MinHeight)
                        cameraPosition.y = ground + MinHeight;

                    fov = Mathf.Lerp(Fov0, fov1, u);
                    roll = Mathf.Lerp(Roll0, roll1, u);

                    if (Zoom > 0f)
                        fov = Mathf.Clamp(2f * Mathf.Atan(Zoom * 0.5f / Mathf.Max(1f, Vector3.Distance(cameraPosition, lookAt))) * Mathf.Rad2Deg, FovMin, FovMax);
                }

                TrailerRecorder.Look(camera, cameraPosition, lookAt, fov, roll);

                if (Shake > 0f)
                {
                    float k = Shake * fov / 60f;
                    camera.transform.rotation *= Quaternion.Euler(
                        (Mathf.PerlinNoise(clock * 1.7f, Seed) - 0.5f) * 2f * k,
                        (Mathf.PerlinNoise(Seed, clock * 1.3f) - 0.5f) * 2f * k,
                        (Mathf.PerlinNoise(clock * 0.9f, Seed + 9f) - 0.5f) * k);
                }
            };
        }
    }

    // ------------------------------------------------------------------
    // Lista de tomas (de la repetición)
    // ------------------------------------------------------------------

    public class Spec
    {
        /// <summary>Segundo de la carrera en el primer cuadro (0 = se apagan las luces).</summary>
        public float From;
        public int Frames = 240;

        /// <summary>60 = velocidad normal; 240 = cuatro veces más lento.</summary>
        public int Fps = 60;
        public int Preroll = 45;
        public Rig Rig = new Rig();

        /// <summary>Cuántas luces del semáforo van prendidas en cada cuadro (null: las de la repetición).</summary>
        public Func<int, int> Lights;
    }

    public static readonly Dictionary<string, Spec> Specs = DefineShots();

    private static Dictionary<string, Spec> DefineShots()
    {
        var s = new Dictionary<string, Spec>();

        // --- Largada ---
        s["a_grilla"] = new Spec { From = -4.4f, Frames = 204, Rig = new Rig { Track0 = new Vector3(1224f, 0f, 1.9f), Track1 = new Vector3(1234.5f, 0f, 0.8f), LookLights0 = new Vector3(-6f, -3.2f, 0f), LookLights1 = new Vector3(-4f, -2.6f, 0f), Fov0 = 48f, Fov1 = 44f, Shake = 0.08f } };
        s["b_semaforo"] = new Spec { From = -3.5f, Frames = 150, Lights = f => f < 30 ? 3 : f < 60 ? 4 : f < 120 ? 5 : 0, Rig = new Rig { Track0 = new Vector3(1256f, -1f, 3.4f), Track1 = new Vector3(1258.5f, -0.7f, 3.7f), LookLights0 = new Vector3(0f, -0.25f, 0f), Fov0 = 16f, Fov1 = 13.5f, Shake = 0.04f } };
        s["c_largada_frente"] = new Spec { From = -0.6f, Frames = 216, Rig = new Rig { Track0 = new Vector3(1258.5f, 0f, 0.24f), LookTrack0 = new Vector3(1247f, 0f, 0.5f), Fov0 = 44f, Shake = 0.1f, MinHeight = 0.2f } };
        s["d_largada_costado"] = new Spec { From = -0.25f, Frames = 210, Rig = new Rig { A = 0, Off0 = new Vector3(-2.6f, 0.5f, 2.4f), Off1 = new Vector3(-3.3f, 0.75f, -1.2f), Look0 = new Vector3(0f, 0.35f, 0f), Fov0 = 46f, Smooth = 0.2f } };
        s["e_largada_arriba"] = new Spec { From = -0.3f, Frames = 270, Rig = new Rig { A = 2, Off0 = new Vector3(0f, 17f, -4f), Off1 = new Vector3(0f, 13f, -6f), Look0 = new Vector3(0f, 0f, 3f), Fov0 = 50f, Smooth = 0.5f, Shake = 0.05f } };

        // --- Carrera ---
        s["f_curva1"] = new Spec { From = 3.9f, Frames = 240, Rig = new Rig { A = 2, Track0 = new Vector3(77f, 5.3f, 0.45f), Look0 = new Vector3(0f, 0.4f, 0f), Zoom = 8f, FovMin = 12f, FovMax = 60f, Shake = 0.15f } };
        s["g_persecucion"] = new Spec { From = 7.2f, Frames = 204, Rig = new Rig { A = 4, Off0 = new Vector3(0.9f, 1f, -3.6f), Look0 = new Vector3(0f, 0.5f, 4f), Fov0 = 62f } };
        s["h_duelo_frente"] = new Spec { From = 15.6f, Frames = 228, Rig = new Rig { A = 1, Off0 = new Vector3(-1.8f, 0.5f, 3.4f), Off1 = new Vector3(1.6f, 0.6f, 3.8f), Look0 = new Vector3(0f, 0.35f, -1.2f), Fov0 = 44f, Smooth = 0.35f } };
        s["i_horquilla_alto"] = new Spec { From = 24.3f, Frames = 210, Rig = new Rig { A = 1, B = 2, Track0 = new Vector3(388f, 10f, 4f), Track1 = new Vector3(394f, 9f, 3.2f), Look0 = new Vector3(0f, 0.3f, 0f), Zoom = 8f, FovMax = 55f, Shake = 0.1f } };
        s["j_curva180"] = new Spec { From = 34.3f, Frames = 180, Rig = new Rig { A = 2, B = 1, Track0 = new Vector3(580f, 4.6f, 0.4f), Look0 = new Vector3(0f, 0.4f, 0f), Zoom = 6f, FovMin = 12f, FovMax = 62f, Shake = 0.15f } };
        s["k_ladoalado_lenta"] = new Spec { From = 51.6f, Frames = 360, Fps = 240, Rig = new Rig { A = 1, B = 2, Off0 = new Vector3(-0.3f, 0.42f, 4.6f), Off1 = new Vector3(0.2f, 0.45f, 4.2f), Look0 = new Vector3(0f, 0.35f, 0f), Fov0 = 38f, Smooth = 0.4f, Shake = 0.06f } };
        s["l_dron"] = new Spec { From = 7.4f, Frames = 240, Rig = new Rig { A = 2, Off0 = new Vector3(0f, 20f, -16f), Off1 = new Vector3(0f, 17f, -12f), Look0 = new Vector3(0f, 0f, 8f), Fov0 = 50f, Smooth = 0.8f, Shake = 0.06f } };
        s["m_meta"] = new Spec { From = 75.4f, Frames = 228, Rig = new Rig { Track0 = new Vector3(4f, 1.2f, 0.22f), LookTrack0 = new Vector3(1232f, -0.6f, 0.55f), Fov0 = 60f, Shake = 0.3f, MinHeight = 0.18f } };
        s["n_abordo"] = new Spec { From = 34f, Frames = 180, Rig = new Rig { A = 1, Off0 = new Vector3(0f, 0.86f, -0.3f), Look0 = new Vector3(0f, 0.38f, 3f), Fov0 = 70f, Smooth = 0.03f, PosSmooth = 0.001f, Shake = 0.25f, MinHeight = 0.1f } };
        s["o_rueda"] = new Spec { From = 44.6f, Frames = 150, Rig = new Rig { A = 2, Off0 = new Vector3(-1.15f, 0.2f, 1.1f), Off1 = new Vector3(-1.3f, 0.24f, 0.7f), Look0 = new Vector3(0f, 0.22f, 0.35f), Fov0 = 58f, Smooth = 0.08f, PosSmooth = 0.01f, Shake = 0.25f, MinHeight = 0.12f } };
        s["p_lavarropas"] = new Spec { From = 8f, Frames = 180, Rig = new Rig { A = 1, Off0 = new Vector3(1.7f, 0.95f, -2.7f), Off1 = new Vector3(0.6f, 0.8f, -2.4f), Look0 = new Vector3(0f, 0.5f, 0.2f), Fov0 = 48f, Smooth = 0.2f } };
        s["q_orbita_lenta"] = new Spec { From = 40f, Frames = 480, Fps = 240, Rig = new Rig { A = 3, Off0 = new Vector3(-2.9f, 0.55f, 1.2f), Off1 = new Vector3(2.6f, 0.85f, 2.2f), Look0 = new Vector3(0f, 0.35f, 0f), Fov0 = 40f, Smooth = 0.3f, Shake = 0.05f } };
        s["r_pasada"] = new Spec { From = 50.6f, Frames = 168, Rig = new Rig { A = 1, B = 2, Track0 = new Vector3(832f, -4.4f, 0.2f), Look0 = new Vector3(0f, 0.35f, 0f), Zoom = 7f, FovMin = 12f, FovMax = 62f, Shake = 0.2f, MinHeight = 0.15f } };

        // --- Para revisar los karts en la grilla (que cada uno tenga sus piezas) ---
        s["y_grilla_arriba"] = new Spec { From = -3.6f, Frames = 6, Preroll = 6, Rig = new Rig { A = 2, Off0 = new Vector3(0f, 9.5f, -1.2f), Look0 = new Vector3(0f, 0f, 0f), Fov0 = 44f, Shake = 0f } };
        s["y_grilla_izq"] = new Spec { From = -3.6f, Frames = 6, Preroll = 6, Rig = new Rig { A = 2, Off0 = new Vector3(-6f, 1.5f, 0.5f), Look0 = new Vector3(0f, 0.3f, 0.5f), Fov0 = 62f, Shake = 0f } };
        s["y_grilla_der"] = new Spec { From = -3.6f, Frames = 6, Preroll = 6, Rig = new Rig { A = 3, Off0 = new Vector3(6f, 1.5f, 0.5f), Look0 = new Vector3(0f, 0.3f, 0.5f), Fov0 = 62f, Shake = 0f } };

        return s;
    }

    /// <summary>Arma la toma (la repetición tiene que estar abierta: las cámaras fijas se ubican al armarla).</summary>
    public static TrailerRecorder.Shot Build(string name, string suffix = "")
    {
        Spec spec = Specs[name];
        Action<Camera, int> camera = spec.Rig.Build(spec.Frames, 1f / spec.Fps);

        var shot = new TrailerRecorder.Shot
        {
            Name = name + suffix,
            ReplayFrom = spec.From,
            Frames = spec.Frames,
            CaptureFps = spec.Fps,
            Preroll = spec.Preroll,
            Cam = camera
        };

        if (spec.Lights != null)
        {
            shot.Cam = (cam, frame) =>
            {
                camera(cam, frame);
                StartLights.SetReplay(spec.Lights(Mathf.Max(0, frame)));
            };
        }

        return shot;
    }

    // ------------------------------------------------------------------
    // La carrera guardada
    // ------------------------------------------------------------------

    public const string RaceFile = "Builds/itch/trailer/datos/carrera1_suave.txt";

    private struct SparkData
    {
        public float Time;
        public Vector3 Point;
        public Vector3 Carry;
        public int Count;
        public float Strength;
    }

    private static readonly List<GhostRecording> ghosts = new List<GhostRecording>();
    private static readonly List<SparkData> sparkData = new List<SparkData>();

    /// <summary>Piezas de cada kart: motor, ruedas, kit aero y volante.</summary>
    public static readonly string[] Outfits =
    {
        "engine_moto", "wheels_slick", "kitaero_timeattack", "steeringwheel_f1",
        "engine_lavarropas", "wheels_honda-mean-mower", "kitaero_carton", "steeringwheel_barco",
        "engine_auto", "wheels_slick", "kitaero_kartcars", "steeringwheel_karting",
        "engine_lancha", "wheels_overboard", "kitaero_pro", "steeringwheel_avion",
        "engine_electric", "wheels_bici", "kitaero_timeattack", "steeringwheel_passat",
        "engine_moto", "wheels_honda-mean-mower", "kitaero_kartcars", "steeringwheel_f1"
    };

    /// <summary>Lee la carrera guardada: por kart, "tiempo x y z rumbo"; "K" empieza un kart y "S" es una chispa.</summary>
    public static int LoadRace()
    {
        ghosts.Clear();
        sparkData.Clear();

        CultureInfo ci = CultureInfo.InvariantCulture;
        var current = new List<GhostRecording.Sample>();

        void Flush()
        {
            if (current.Count > 1)
                ghosts.Add(new GhostRecording { LapTime = 75f, Samples = current.ToArray() });

            current = new List<GhostRecording.Sample>();
        }

        foreach (string line in File.ReadAllLines(Path.GetFullPath(RaceFile)))
        {
            string[] p = line.Split(' ');

            if (p.Length < 2)
                continue;

            if (p[0] == "K")
            {
                Flush();
                continue;
            }

            if (p[0] == "S")
            {
                sparkData.Add(new SparkData
                {
                    Time = float.Parse(p[1], ci),
                    Point = new Vector3(float.Parse(p[2], ci), float.Parse(p[3], ci), float.Parse(p[4], ci)),
                    Carry = new Vector3(float.Parse(p[5], ci), float.Parse(p[6], ci), float.Parse(p[7], ci)),
                    Count = int.Parse(p[8], ci),
                    Strength = float.Parse(p[9], ci)
                });

                continue;
            }

            current.Add(new GhostRecording.Sample
            {
                Time = float.Parse(p[0], ci),
                Position = new Vector3(float.Parse(p[1], ci), float.Parse(p[2], ci), float.Parse(p[3], ci)),
                Rotation = Quaternion.Euler(0f, float.Parse(p[4], ci), 0f)
            });
        }

        Flush();
        return ghosts.Count;
    }

    /// <summary>Desde el Inicio: entra a la repetición de la tabla con la carrera guardada, ese clima y esa hora.</summary>
    public static void StartViewer(WeatherScenario weather, TimeOfDay time, FogChoice fog)
    {
        if (ghosts.Count == 0)
            LoadRace();

        TimeTrialRecords.ViewerGhosts.Clear();

        // Un kart de más, de relleno: queda sin muestras y con el foco, así la repetición no mueve la cámara.
        for (int i = 0; i <= ghosts.Count; i++)
        {
            int ghost = Mathf.Min(i, ghosts.Count - 1);
            int o = (i % (Outfits.Length / 4)) * 4;

            var entry = new TimeTrialRecords.Entry
            {
                Name = "KART " + (i + 1),
                PlayerId = "kart" + i,
                Rank = i + 1,
                Time = 75f,
                Weather = weather,
                TimeOfDay = time,
                Parts = new KartParts { Engine = Outfits[o], Wheels = Outfits[o + 1], AeroKit = Outfits[o + 2], SteeringWheel = Outfits[o + 3] }
            };

            TimeTrialRecords.ViewerGhosts.Add(new TimeTrialRecords.Opponent
            {
                Entry = entry,
                Ghost = ghosts[ghost],
                Color = TimeTrialRecords.OpponentColors[i % TimeTrialRecords.OpponentColors.Length]
            });
        }

        TimeTrialRecords.Opponents.Clear();
        TimeTrialRecords.ActiveBoardId = null;
        RaceSetupStore.BotsForRace = 0;
        WeatherChoiceStore.Fog = fog;
        RaceSession.StartNew(0, 0f, false);
        RaceSession.TimeTrial = true;
        RaceSession.GhostViewer = true;
        RaceSession.MandatoryPitStop = false;
        RaceSession.ViewerWeather = weather;
        RaceSession.ViewerTimeOfDay = time;
        centerline = null;
        GameSession.Instance.StartSinglePlayerIn(PlayerName, GameSession.Instance.RaceScene);
    }

    /// <summary>Con la repetición abierta: saca la interfaz, deja la cámara libre y carga las chispas.</summary>
    public static string SetupViewer()
    {
        RaceReplay replay = UnityEngine.Object.FindFirstObjectByType<RaceReplay>();

        if (replay == null || !replay.IsOpen)
            return "repetición cerrada";

        TrailerRecorder.Set(replay, "playing", false);

        var tracks = (IList)TrailerRecorder.Get(replay, "tracks");
        int filler = tracks.Count - 1;
        ((IList)TrailerRecorder.Get(tracks[filler], "Samples")).Clear();
        TrailerRecorder.Set(replay, "focus", filler);

        var overlay = TrailerRecorder.Get(replay, "overlay") as Canvas;

        if (overlay != null)
            overlay.enabled = false;

        TrailerRecorder.Set(replay, "showNames", false);

        var sparks = (IList)TrailerRecorder.Get(replay, "sparks");
        sparks.Clear();
        Type sparkType = typeof(RaceReplay).GetNestedType("SparkEvent", System.Reflection.BindingFlags.NonPublic);

        foreach (SparkData data in sparkData)
        {
            object spark = Activator.CreateInstance(sparkType);
            sparkType.GetField("Time").SetValue(spark, data.Time);
            sparkType.GetField("Point").SetValue(spark, data.Point);
            sparkType.GetField("Normal").SetValue(spark, Vector3.up);
            sparkType.GetField("Carry").SetValue(spark, data.Carry);
            sparkType.GetField("Count").SetValue(spark, data.Count * 2);
            sparkType.GetField("Strength").SetValue(spark, Mathf.Clamp01(data.Strength * 1.6f));
            sparks.Add(spark);
        }

        return "ok karts=" + (TrailerRecorder.ActorCount - 1) + " chispas=" + sparks.Count;
    }

    // ------------------------------------------------------------------
    // Taller y largada con HUD (en vivo)
    // ------------------------------------------------------------------

    private static void StartTaller()
    {
        TimeTrialRecords.ViewerGhosts.Clear();
        TimeTrialRecords.Opponents.Clear();
        TimeTrialRecords.ActiveBoardId = null;
        RaceSession.StartNew(0, 0f, false);
        RaceSession.TimeTrial = false;
        RaceSession.GhostViewer = false;
        GameSession.Instance.StartSinglePlayer(PlayerName);
    }

    private static void ClickPart(string partID)
    {
        foreach (CatalogPartItem item in UnityEngine.Object.FindObjectsByType<CatalogPartItem>(FindObjectsSortMode.None))
        {
            if (item.Part == null || item.Part.partID != partID)
                continue;

            item.OnPointerEnter(null);
            var button = TrailerRecorder.Get(item, "selectButton") as Button;

            if (button != null && button.interactable)
                button.onClick.Invoke();

            return;
        }

        TrailerRecorder.Note("taller: no encontré " + partID);
    }

    /// <summary>El taller: va por las cuatro pestañas y prueba piezas (cuadro → qué aprieta).</summary>
    private static TrailerRecorder.Shot TallerShot()
    {
        CatalogUIController ui = UnityEngine.Object.FindFirstObjectByType<CatalogUIController>();
        var plan = new Dictionary<int, Action>();

        void Click(int frame, string partID) => plan[frame] = () => ClickPart(partID);
        void Tab(int frame, PartType type) => plan[frame] = () => ui.ShowCategory(type);

        Click(20, "engine_lavarropas");
        Click(65, "engine_lancha");
        Click(110, "engine_moto");
        Tab(150, PartType.AeroKit);
        Click(235, "kitaero_carton");
        Click(280, "kitaero_kartcars");
        Click(325, "kitaero_timeattack");
        Tab(365, PartType.Wheels);
        Click(450, "wheels_bici");
        Click(495, "wheels_overboard");
        Click(540, "wheels_honda-mean-mower");
        Tab(580, PartType.SteeringWheel);
        Click(665, "steeringwheel_barco");
        Click(710, "steeringwheel_avion");

        return new TrailerRecorder.Shot
        {
            Name = "t_taller",
            Frames = 770,
            Preroll = 30,
            Tick = frame =>
            {
                if (plan.TryGetValue(frame, out Action action))
                    action();
            }
        };
    }

    private static void StartHudRace()
    {
        TimeTrialRecords.ViewerGhosts.Clear();
        TimeTrialRecords.Opponents.Clear();
        TimeTrialRecords.ActiveBoardId = null;
        RaceSetupStore.BotsForRace = 6;
        RaceSetupStore.BotDifficulty = RaceDifficulty.Hard;
        RaceSetupStore.Laps = 2;
        WeatherChoiceStore.Current = WeatherChoice.Clear;
        WeatherChoiceStore.Time = TimeOfDayChoice.Noon;
        WeatherChoiceStore.Fog = FogChoice.None;
        RaceSession.StartNew(0, 0f, false);
        RaceSession.TimeTrial = false;
        RaceSession.GhostViewer = false;
        RaceSession.MandatoryPitStop = false;
        GameSession.Instance.StartSinglePlayerIn(PlayerName, GameSession.Instance.RaceScene);
    }

    /// <summary>La largada como la ve el jugador, con el HUD: el kart acelera solo y va derecho.</summary>
    private static TrailerRecorder.Shot HudShot(RaceManager manager)
    {
        KartBehaviour player = null;

        foreach (KartBehaviour kart in manager.RaceKarts)
        {
            if (kart != null && kart.UsesPlayerAssists)
                player = kart;
        }

        if (player == null)
            throw new Exception("no encontré el kart del jugador");

        PlayerKartInput input = player.GetComponent<PlayerKartInput>();

        if (input != null)
            input.enabled = false;

        TrackCenterline line = UnityEngine.Object.FindFirstObjectByType<TrackCenterline>();
        float lateralStart = float.NaN;

        Unity.Netcode.NetworkManager network = Unity.Netcode.NetworkManager.Singleton;
        double clock = double.NaN;
        float averageReal = 1f / 15f;

        return new TrailerRecorder.Shot
        {
            Name = "v_hud_largada",
            Frames = 660,
            Preroll = 0,
            Tick = frame =>
            {
                // El reloj de la carrera es el de la red, que corre con el tiempo real. Grabando,
                // cada cuadro tarda más que 1/60 s, así que la cuenta y los tiempos irían
                // acelerados: acá se lleva ese reloj al ritmo del video.
                if (network != null && network.IsListening)
                {
                    averageReal = Mathf.Lerp(averageReal, Time.unscaledDeltaTime, 0.1f);

                    if (double.IsNaN(clock))
                        clock = network.ServerTime.Time;
                    else
                        clock += 1.0 / TrailerRecorder.Fps;

                    network.NetworkTimeSystem.Reset(clock - averageReal, 0.0);
                }

                if (player == null)
                    return;

                float distance = line.GetDistance(player.transform.position);
                Vector3 direction = Vector3.ProjectOnPlane(line.GetDirectionAtDistance(distance), Vector3.up).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, direction);
                float lateral = Vector3.Dot(player.transform.position - line.GetPointAtDistance(distance), right);

                if (float.IsNaN(lateralStart))
                    lateralStart = lateral;

                float heading = Vector3.SignedAngle(Vector3.ProjectOnPlane(player.transform.forward, Vector3.up), direction, Vector3.up);
                float steer = Mathf.Clamp(heading / 18f + (lateralStart - lateral) * 0.2f, -1f, 1f);

                // Sin acelerar antes de la largada (si no, es salida en falso).
                player.SetInputs(manager.IsRacing() ? 1f : 0f, steer, false, true);
            }
        };
    }

    // ------------------------------------------------------------------
    // Sesiones: de a una, cuando la cola de tomas queda libre
    // ------------------------------------------------------------------

    public enum JobKind
    {
        Viewer,
        Taller,
        Hud
    }

    public class Job
    {
        public JobKind Kind;
        public WeatherScenario Weather;
        public TimeOfDay Time;
        public FogChoice Fog = FogChoice.None;
        public string Suffix = "";
        public string[] Shots = new string[0];

        /// <summary>Solo vista previa (tiras de fotos), sin grabar.</summary>
        public bool Preview;
    }

    public static Job Viewer(WeatherScenario weather, TimeOfDay time, string suffix, params string[] shots)
    {
        return new Job { Kind = JobKind.Viewer, Weather = weather, Time = time, Suffix = suffix, Shots = shots };
    }

    private static readonly List<Job> jobs = new List<Job>();
    private static Job job;
    private static int state;
    private static double mark;
    private static bool managing;

    public static string Info { get; private set; } = "";
    public static int Left => jobs.Count + (job != null ? 1 : 0);

    public static void Queue(params Job[] list)
    {
        jobs.AddRange(list);

        if (!managing)
        {
            EditorApplication.update += Manage;
            managing = true;
        }
    }

    /// <summary>Corta todo: la toma que se está grabando y lo que quedaba en la cola.</summary>
    public static void Stop()
    {
        jobs.Clear();
        TrailerRecorder.Abort();
    }

    private static void Manage()
    {
        try
        {
            if (!EditorApplication.isPlaying)
            {
                EditorApplication.update -= Manage;
                managing = false;
                job = null;
                state = 0;
                jobs.Clear();
                return;
            }

            string scene = SceneManager.GetActiveScene().name;
            double now = EditorApplication.timeSinceStartup;

            if (state == 0)
            {
                if (TrailerRecorder.Busy)
                    return;

                if (jobs.Count == 0)
                {
                    if (job != null)
                    {
                        job = null;
                        Info = "cola vacía";
                    }

                    return;
                }

                job = jobs[0];
                jobs.RemoveAt(0);
                TrailerRecorder.ReplayEnd();
                Info = job.Kind + job.Suffix + ": al menú (quedan " + jobs.Count + ")";

                if (scene == "Inicio")
                {
                    mark = now;
                }
                else
                {
                    GameSession.Instance.LeaveToMenu();
                    mark = double.MaxValue;
                }

                state = 1;
                return;
            }

            if (state == 1)
            {
                if (scene != "Inicio" || LoadingScreen.IsShowing || GameSession.Instance == null)
                    return;

                if (mark > now)
                    mark = now;

                if (now - mark < 2.5)
                    return;

                if (job.Kind == JobKind.Viewer)
                    StartViewer(job.Weather, job.Time, job.Fog);
                else if (job.Kind == JobKind.Taller)
                    StartTaller();
                else
                    StartHudRace();

                Info = job.Kind + job.Suffix + ": cargando (quedan " + jobs.Count + ")";
                mark = double.MaxValue;
                state = 2;
                return;
            }

            if (job.Kind == JobKind.Viewer)
            {
                if (scene != "Race" || LoadingScreen.IsShowing)
                    return;

                string setup = SetupViewer();

                if (!setup.StartsWith("ok"))
                    return;

                var shots = new List<TrailerRecorder.Shot>();

                foreach (string name in job.Shots)
                    shots.Add(Build(name, job.Suffix));

                TrailerRecorder.Preview = job.Preview;
                TrailerRecorder.Run(shots.ToArray());
                Info = job.Kind + job.Suffix + ": grabando " + shots.Count + " tomas, " + setup + " (quedan " + jobs.Count + ")";
                state = 0;
            }
            else if (job.Kind == JobKind.Taller)
            {
                if (scene != "Catalog" || LoadingScreen.IsShowing || UnityEngine.Object.FindFirstObjectByType<CatalogUIController>() == null)
                    return;

                if (mark > now)
                    mark = now;

                if (now - mark < 2.0)
                    return;

                TrailerRecorder.Preview = false;
                TrailerRecorder.Run(TallerShot());
                Info = "Taller: grabando (quedan " + jobs.Count + ")";
                state = 0;
            }
            else
            {
                RaceManager manager = scene == "Race" ? UnityEngine.Object.FindFirstObjectByType<RaceManager>() : null;

                if (manager == null || !manager.IsCountdown())
                    return;

                TrailerRecorder.Preview = false;
                TrailerRecorder.Run(HudShot(manager));
                Info = "Hud: grabando (quedan " + jobs.Count + ")";
                state = 0;
            }
        }
        catch (Exception exception)
        {
            Info = "ERROR " + exception;
            jobs.Clear();
            job = null;
            state = 0;
        }
    }
}
