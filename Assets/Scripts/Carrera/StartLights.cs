using UnityEngine;

/// <summary>
/// Semáforo de largada (las 5 luces del arco), como en F1:
/// - Esperando: apagadas.
/// - Cuenta regresiva: se prende una luz roja por segundo. Con todas
///   prendidas, se apagan en un momento al azar (lo decide RaceManager).
/// - Luces apagadas = largada.
/// Usa el reloj de RaceManager, así que en red se ve igual en todas las
/// computadoras.
/// </summary>
public class StartLights : MonoBehaviour
{
    [SerializeField] private RaceManager raceManager;

    [Tooltip("Las luces, en el orden en que se prenden.")]
    [SerializeField] private Renderer[] lamps = new Renderer[0];

    [Tooltip("Luz opcional que ilumina alrededor del semáforo.")]
    [SerializeField] private Light glow;

    [SerializeField] private Color offColor = new Color(0.07f, 0.01f, 0.01f, 1f);
    [SerializeField] private Color redColor = new Color(1f, 0.05f, 0.03f, 1f);

    [Tooltip("Intensidad del brillo de las luces prendidas.")]
    [SerializeField, Min(0f)] private float emission = 4f;

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    private MaterialPropertyBlock block;
    private int lastLit = -1;

    /// <summary>Cuántas luces están prendidas ahora.</summary>
    public int LitCount => Mathf.Max(0, lastLit);

    private void Awake()
    {
        block = new MaterialPropertyBlock();

        if (raceManager == null)
            raceManager = FindFirstObjectByType<RaceManager>();

        Show(0);
    }

    private void Update()
    {
        if (raceManager == null || !raceManager.IsCountdown())
        {
            Show(0);
            return;
        }

        // La primera se prende al segundo, la segunda a los dos, etc.
        int lit = Mathf.FloorToInt(raceManager.CountdownElapsed / raceManager.StartLightInterval);
        Show(Mathf.Clamp(lit, 0, lamps.Length));
    }

    private void Show(int lit)
    {
        if (lit == lastLit)
            return;

        lastLit = lit;

        for (int i = 0; i < lamps.Length; i++)
        {
            if (lamps[i] == null)
                continue;

            bool isOn = i < lit;
            lamps[i].GetPropertyBlock(block);
            block.SetColor(ColorId, isOn ? redColor : offColor);
            block.SetColor(EmissionId, isOn ? redColor * emission : Color.black);
            lamps[i].SetPropertyBlock(block);
        }

        if (glow != null)
        {
            glow.enabled = lit > 0;
            glow.color = redColor;
            glow.intensity = 0.5f + 0.4f * lit;
        }
    }
}
