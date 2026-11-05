using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Contrarreloj con fantasma: en una carrera de un solo kart (un jugador sin
/// bots), un kart transparente repite la mejor vuelta.
/// - Graba la vuelta del jugador (posición y rotación, sampleRate por segundo).
/// - Al cruzar la meta, si fue la mejor vuelta y no tuvo penalizaciones ni
///   reaparición, pasa a ser el fantasma y se guarda en disco
///   (persistentDataPath/Fantasmas): la próxima vez está de nuevo.
/// - En cada vuelta el fantasma sale de la meta junto con el jugador.
/// - Cerca del jugador se vuelve casi invisible, para que no moleste.
/// - La tecla G lo muestra u oculta (queda guardado).
/// - En el minimapa es un punto celeste translúcido con un halo que late
///   (TrackMinimap.AddGhost).
/// - En el HUD, abajo de los tiempos: el tiempo del fantasma y, al cruzar la
///   meta, cuánto más rápido o más lento fue la vuelta.
/// La hora es la del juego (Time.time): en pausa se queda quieta, igual que el
/// fantasma. Se arma solo al cargar una escena con RaceManager.
/// </summary>
public class KartGhost : MonoBehaviour
{
    private const string VisibleKey = "ApexRun.Fantasma";
    private const int FileVersion = 1;
    private const string MaterialPath = "Efectos/Fantasma";

    [Tooltip("Tecla para mostrar u ocultar el fantasma.")]
    [SerializeField] private Key toggleKey = Key.G;

    [Tooltip("Posiciones grabadas por segundo.")]
    [SerializeField, Min(5f)] private float sampleRate = 25f;

    [SerializeField] private Color ghostColor = new Color(0.55f, 0.85f, 1f, 0.35f);

    [Tooltip("A esta distancia del jugador (m) o menos, el fantasma casi no se ve.")]
    [SerializeField, Min(0f)] private float fadeNear = 2f;

    [Tooltip("Desde esta distancia (m) se ve con ghostColor.")]
    [SerializeField, Min(0f)] private float fadeFar = 7f;

    [SerializeField, Range(0f, 1f)] private float nearAlpha = 0.06f;

    [Tooltip("Cuánto tiempo (s) se ve la diferencia con el fantasma al cruzar la meta.")]
    [SerializeField, Min(0f)] private float deltaShowTime = 4f;

    [SerializeField] private Color fasterColor = new Color(0.24f, 0.86f, 0.52f);
    [SerializeField] private Color slowerColor = new Color(1f, 0.36f, 0.31f);
    [SerializeField] private Color invalidColor = new Color(1f, 0.7f, 0.2f);

    private struct Sample
    {
        public float Time;
        public Vector3 Position;
        public Quaternion Rotation;
    }

    private class GhostLap
    {
        public float LapTime;
        public Sample[] Samples;

        public float Duration => Samples.Length > 0 ? Samples[Samples.Length - 1].Time : 0f;
    }

    private RaceManager raceManager;
    private RaceLapManager lapManager;
    private RacePenaltyManager penaltyManager;
    private RaceTimingManager timingManager;
    private RaceCheckpointManager checkpointManager;
    private RaceHUD hud;

    private KartBehaviour player;
    private Rigidbody playerBody;
    private bool active;
    private bool visible = true;

    // Vuelta en curso (hora del juego).
    private double lapStart;
    private float nextSample;
    private bool lapValid;
    private readonly List<Sample> recording = new List<Sample>();

    private GhostLap best;

    private Transform ghostRoot;
    private Material ghostMaterial;

    private RectTransform infoBox;
    private TMP_Text infoText;
    private TMP_Text deltaText;
    private float deltaUntil;

    /// <summary>Tiempo de la vuelta del fantasma (s), o -1 si todavía no hay.</summary>
    public float GhostLapTime => best != null ? best.LapTime : -1f;

    private void Start()
    {
        visible = PlayerPrefs.GetInt(VisibleKey, 1) == 1;

        lapManager = FindFirstObjectByType<RaceLapManager>();
        penaltyManager = FindFirstObjectByType<RacePenaltyManager>();
        timingManager = FindFirstObjectByType<RaceTimingManager>();
        checkpointManager = FindFirstObjectByType<RaceCheckpointManager>();
        hud = FindFirstObjectByType<RaceHUD>(FindObjectsInactive.Include);

        if (raceManager != null)
        {
            raceManager.RaceStarted += HandleRaceStarted;

            // Ya largó (por ejemplo, se armó tarde).
            if (raceManager.IsRacing())
                HandleRaceStarted();
        }

        if (lapManager != null)
            lapManager.LapCompleted += HandleLapCompleted;

        if (penaltyManager != null)
            penaltyManager.PenaltyApplied += HandlePenalty;
    }

    private void OnDestroy()
    {
        if (raceManager != null)
            raceManager.RaceStarted -= HandleRaceStarted;

        if (lapManager != null)
            lapManager.LapCompleted -= HandleLapCompleted;

        if (penaltyManager != null)
            penaltyManager.PenaltyApplied -= HandlePenalty;

        if (ghostMaterial != null)
            Destroy(ghostMaterial);
    }

    // ------------------------------------------------------------------
    // Carrera
    // ------------------------------------------------------------------

    private void HandleRaceStarted()
    {
        player = FindPlayer();

        // Solo contrarreloj: un único kart en la carrera, el del jugador.
        active = player != null && raceManager.RaceKarts.Count == 1;

        if (!active)
        {
            if (infoBox != null)
                infoBox.gameObject.SetActive(false);

            return;
        }

        playerBody = player.GetComponent<Rigidbody>();

        if (best == null)
            best = Load();

        if (ghostRoot == null)
        {
            BuildVisual();

            // Un punto en el minimapa, con estilo de fantasma. Sigue al kart
            // transparente: se ve solo mientras está en pista.
            TrackMinimap minimap = FindFirstObjectByType<TrackMinimap>(FindObjectsInactive.Include);

            if (minimap != null && ghostRoot != null)
                minimap.AddGhost(ghostRoot);
        }

        if (infoBox == null)
            BuildTexts();

        StartLap();
        RefreshInfo();
    }

    private KartBehaviour FindPlayer()
    {
        foreach (KartBehaviour kart in raceManager.RaceKarts)
        {
            if (kart != null && kart.IsSimulated && kart.UsesPlayerAssists)
                return kart;
        }

        return null;
    }

    private void StartLap()
    {
        lapStart = Time.timeAsDouble;
        recording.Clear();
        lapValid = true;
        nextSample = 0f;
        Record(0f);
    }

    private void HandlePenalty(Rigidbody kart, float seconds)
    {
        // Una vuelta con penalización (límites de pista, salida en falso) no vale de fantasma.
        if (active && kart == playerBody)
            lapValid = false;
    }

    private void HandleLapCompleted(Rigidbody kart)
    {
        if (!active || kart != playerBody)
            return;

        float duration = (float)(Time.timeAsDouble - lapStart);
        Record(duration);

        // El tiempo oficial de la vuelta (el del cronómetro); si no está, el propio.
        float lapTime = timingManager != null ? timingManager.GetLastLap(kart) : -1f;

        if (lapTime <= 0f)
            lapTime = duration;

        bool record = lapValid && recording.Count > 1 && (best == null || lapTime < best.LapTime);
        ShowDelta(lapTime, record);

        if (record)
        {
            best = new GhostLap { LapTime = lapTime, Samples = recording.ToArray() };
            Save(best);
        }

        StartLap();
        RefreshInfo();
    }

    // ------------------------------------------------------------------
    // Grabar y reproducir
    // ------------------------------------------------------------------

    private void FixedUpdate()
    {
        if (!active || playerBody == null || raceManager == null || !raceManager.IsRacing())
            return;

        float t = (float)(Time.timeAsDouble - lapStart);

        if (t < nextSample)
            return;

        Record(t);
        nextSample = t + 1f / sampleRate;
    }

    private void Record(float time)
    {
        if (playerBody == null)
            return;

        Vector3 position = playerBody.position;

        // Un salto grande es una reaparición: esa vuelta no vale de fantasma.
        if (recording.Count > 0 && (position - recording[recording.Count - 1].Position).sqrMagnitude > 15f * 15f)
            lapValid = false;

        recording.Add(new Sample { Time = time, Position = position, Rotation = playerBody.rotation });
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (active && keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
        {
            visible = !visible;
            PlayerPrefs.SetInt(VisibleKey, visible ? 1 : 0);
            PlayerPrefs.Save();
            RefreshInfo();
        }

        if (deltaText != null && deltaText.gameObject.activeSelf && Time.unscaledTime > deltaUntil)
            SetDeltaVisible(false);
    }

    private void LateUpdate()
    {
        if (ghostRoot == null)
            return;

        float t = (float)(Time.timeAsDouble - lapStart);

        bool show = active && visible && best != null && raceManager != null && raceManager.IsRacing() &&
                    t <= best.Duration;

        if (ghostRoot.gameObject.activeSelf != show)
            ghostRoot.gameObject.SetActive(show);

        if (!show)
            return;

        Evaluate(best.Samples, t, out Vector3 position, out Quaternion rotation);
        ghostRoot.SetPositionAndRotation(position, rotation);

        float distance = playerBody != null ? Vector3.Distance(position, playerBody.position) : fadeFar;
        Color color = ghostColor;
        color.a = Mathf.Lerp(nearAlpha, ghostColor.a, Mathf.InverseLerp(fadeNear, fadeFar, distance));
        ghostMaterial.color = color;
    }

    private static void Evaluate(Sample[] samples, float time, out Vector3 position, out Quaternion rotation)
    {
        int low = 0;
        int high = samples.Length - 1;

        if (time <= samples[0].Time)
        {
            position = samples[0].Position;
            rotation = samples[0].Rotation;
            return;
        }

        if (time >= samples[high].Time)
        {
            position = samples[high].Position;
            rotation = samples[high].Rotation;
            return;
        }

        // La muestra de antes y la de después de ese momento.
        while (high - low > 1)
        {
            int middle = (low + high) / 2;

            if (samples[middle].Time <= time)
                low = middle;
            else
                high = middle;
        }

        Sample a = samples[low];
        Sample b = samples[high];
        float k = Mathf.InverseLerp(a.Time, b.Time, time);

        position = Vector3.Lerp(a.Position, b.Position, k);
        rotation = Quaternion.Slerp(a.Rotation, b.Rotation, k);
    }

    // ------------------------------------------------------------------
    // Kart transparente: copia de los modelos del kart del jugador
    // ------------------------------------------------------------------

    private void BuildVisual()
    {
        Material source = Resources.Load<Material>(MaterialPath);

        if (source == null)
        {
            Debug.LogWarning("KartGhost: falta el material Resources/" + MaterialPath + ".");
            return;
        }

        ghostMaterial = new Material(source) { color = ghostColor };

        ghostRoot = new GameObject("FantasmaKart").transform;
        ghostRoot.gameObject.SetActive(false);

        Transform kart = player.transform;
        Matrix4x4 toKart = kart.worldToLocalMatrix;

        foreach (Renderer renderer in player.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;

            Mesh mesh = null;
            Matrix4x4 local;

            if (renderer is SkinnedMeshRenderer skinned)
            {
                // Con la pose de ahora (el piloto sentado). Sin la escala: el
                // modelo está en centímetros (escala 0,01) y con la escala
                // queda 100 veces más grande.
                mesh = new Mesh();
                skinned.BakeMesh(mesh, false);
                local = toKart * Matrix4x4.TRS(skinned.transform.position, skinned.transform.rotation, Vector3.one);
            }
            else if (renderer is MeshRenderer)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                mesh = filter != null ? filter.sharedMesh : null;
                local = toKart * renderer.transform.localToWorldMatrix;
            }
            else
            {
                continue;
            }

            if (mesh == null)
                continue;

            var part = new GameObject(renderer.name);
            part.transform.SetParent(ghostRoot, false);
            part.transform.localPosition = local.GetColumn(3);
            part.transform.localRotation = local.rotation;
            part.transform.localScale = local.lossyScale;

            part.AddComponent<MeshFilter>().sharedMesh = mesh;

            var copy = part.AddComponent<MeshRenderer>();
            var materials = new Material[Mathf.Max(1, mesh.subMeshCount)];

            for (int i = 0; i < materials.Length; i++)
                materials[i] = ghostMaterial;

            copy.sharedMaterials = materials;
            copy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            copy.receiveShadows = false;
        }
    }

    // ------------------------------------------------------------------
    // HUD
    // ------------------------------------------------------------------

    private void BuildTexts()
    {
        if (hud == null)
            return;

        FieldInfo field = typeof(RaceHUD).GetField("lapTimesText", BindingFlags.Instance | BindingFlags.NonPublic);
        TMP_Text lapTimes = field != null ? field.GetValue(hud) as TMP_Text : null;
        RectTransform panel = lapTimes != null ? lapTimes.transform.parent as RectTransform : null;

        if (panel == null)
            return;

        // Un recuadro igual al de los tiempos, justo debajo.
        var box = new GameObject("PanelFantasma", typeof(RectTransform), typeof(Image));
        box.layer = panel.gameObject.layer;
        infoBox = (RectTransform)box.transform;
        infoBox.SetParent(panel, false);
        infoBox.anchorMin = infoBox.anchorMax = new Vector2(1f, 0f);
        infoBox.pivot = new Vector2(1f, 1f);
        infoBox.anchoredPosition = new Vector2(0f, -6f);
        infoBox.sizeDelta = new Vector2(panel.rect.width, 34f);

        Image background = box.GetComponent<Image>();
        Image panelImage = panel.GetComponent<Image>();

        if (panelImage != null)
        {
            background.sprite = panelImage.sprite;
            background.type = panelImage.type;
            background.color = panelImage.color;
            background.pixelsPerUnitMultiplier = panelImage.pixelsPerUnitMultiplier;
        }
        else
        {
            background.color = new Color(0f, 0f, 0f, 0.45f);
        }

        background.raycastTarget = false;

        TMP_FontAsset font = RaceStandingsTower.FindHudFont(hud, bold: false);
        TMP_FontAsset bold = RaceStandingsTower.FindHudFont(hud, bold: true);

        infoText = NewText("Fantasma", infoBox, font, 18f, TextAlignmentOptions.Right);
        Place(infoText.rectTransform, new Vector2(0f, -4f), 26f);

        deltaText = NewText("Diferencia", infoBox, bold, 24f, TextAlignmentOptions.Right);
        Place(deltaText.rectTransform, new Vector2(0f, -30f), 30f);
        SetDeltaVisible(false);
    }

    private static TMP_Text NewText(string name, Transform parent, TMP_FontAsset font, float size, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);

        var text = go.GetComponent<TextMeshProUGUI>();

        if (font != null)
            text.font = font;

        text.fontSize = size;
        text.color = Color.white;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    /// <summary>Renglón de ancho completo, arriba del recuadro, con margen a los costados.</summary>
    private static void Place(RectTransform rect, Vector2 offset, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(14f, offset.y - height);
        rect.offsetMax = new Vector2(-14f, offset.y);
    }

    private void RefreshInfo()
    {
        if (infoText == null)
            return;

        string text = best != null
            ? GameLanguage.Text("FANTASMA ", "GHOST ") + FormatTime(best.LapTime)
            : GameLanguage.Text("FANTASMA: marcá una vuelta", "GHOST: set a lap");

        if (best != null && !visible)
            text += GameLanguage.Text("  (oculto, G)", "  (hidden, G)");

        infoText.text = text;
        infoBox.gameObject.SetActive(active);
    }

    private void ShowDelta(float lapTime, bool record)
    {
        if (deltaText == null)
            return;

        if (!lapValid)
            SetDelta(GameLanguage.Text("VUELTA INVÁLIDA", "INVALID LAP"), invalidColor);
        else if (best == null)
            SetDelta(GameLanguage.Text("¡PRIMER FANTASMA!", "FIRST GHOST!"), fasterColor);
        else
        {
            float delta = lapTime - best.LapTime;
            string sign = delta < 0f ? "-" : "+";
            string text = sign + FormatDelta(Mathf.Abs(delta));

            if (record)
                text += GameLanguage.Text("  RÉCORD", "  RECORD");

            SetDelta(text, delta < 0f ? fasterColor : slowerColor);
        }

        deltaUntil = Time.unscaledTime + deltaShowTime;
    }

    private void SetDelta(string text, Color color)
    {
        deltaText.text = text;
        deltaText.color = color;
        SetDeltaVisible(true);
    }

    private void SetDeltaVisible(bool show)
    {
        deltaText.gameObject.SetActive(show);

        if (infoBox != null)
            infoBox.sizeDelta = new Vector2(infoBox.sizeDelta.x, show ? 64f : 34f);
    }

    private static string FormatTime(float seconds)
    {
        int minutes = Mathf.FloorToInt(seconds / 60f);
        return minutes + ":" + (seconds - minutes * 60f).ToString("00.000", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string FormatDelta(float seconds)
    {
        return seconds >= 60f
            ? FormatTime(seconds)
            : seconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
    }

    // ------------------------------------------------------------------
    // Guardar en disco (una por pista)
    // ------------------------------------------------------------------

    private string FilePath()
    {
        // La pista: la escena y cuántos checkpoints tiene (si cambia el
        // trazado, el fantasma viejo no se usa).
        int checkpoints = checkpointManager != null ? checkpointManager.CheckpointCount : 0;
        string name = SceneManager.GetActiveScene().name + "_" + checkpoints + ".fantasma";
        return Path.Combine(Application.persistentDataPath, "Fantasmas", name);
    }

    private void Save(GhostLap lap)
    {
        try
        {
            string path = FilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(FileVersion);
                writer.Write(lap.LapTime);
                writer.Write(lap.Samples.Length);

                foreach (Sample sample in lap.Samples)
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
        }
        catch (Exception exception)
        {
            Debug.LogWarning("KartGhost: no se pudo guardar el fantasma: " + exception.Message);
        }
    }

    private GhostLap Load()
    {
        string path = FilePath();

        if (!File.Exists(path))
            return null;

        try
        {
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                if (reader.ReadInt32() != FileVersion)
                    return null;

                var lap = new GhostLap { LapTime = reader.ReadSingle() };
                int count = reader.ReadInt32();

                if (count < 2 || count > 100000)
                    return null;

                lap.Samples = new Sample[count];

                for (int i = 0; i < count; i++)
                {
                    lap.Samples[i] = new Sample
                    {
                        Time = reader.ReadSingle(),
                        Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                        Rotation = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle())
                    };
                }

                return lap;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("KartGhost: no se pudo leer el fantasma: " + exception.Message);
            return null;
        }
    }

    // ------------------------------------------------------------------
    // Armado automático (así no hace falta tocar la escena)
    // ------------------------------------------------------------------

    private static bool installerHooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void HookInstaller()
    {
        if (!installerHooked)
        {
            SceneManager.sceneLoaded += (scene, mode) => InstallIn(scene);
            installerHooked = true;
        }

        InstallIn(SceneManager.GetActiveScene());
    }

    private static void InstallIn(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            RaceManager manager = root.GetComponentInChildren<RaceManager>(true);

            if (manager == null)
                continue;

            if (FindFirstObjectByType<KartGhost>() != null)
                return;

            var go = new GameObject("Fantasma");
            SceneManager.MoveGameObjectToScene(go, scene);

            var ghost = go.AddComponent<KartGhost>();
            ghost.raceManager = manager;
            return;
        }
    }
}
