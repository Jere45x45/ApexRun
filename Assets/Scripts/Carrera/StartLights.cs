using UnityEngine;

/// <summary>
/// Semáforo de largada (las 5 luces del arco).
/// - Esperando: apagadas.
/// - Cuenta regresiva: se van prendiendo en rojo de a una, repartidas en
///   el tiempo de la cuenta (con 3 s, una cada 0,6 s).
/// - Largada: las 5 en verde un rato y después se apagan.
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
    [SerializeField] private Color greenColor = new Color(0.1f, 1f, 0.2f, 1f);

    [Tooltip("Intensidad del brillo de las luces prendidas.")]
    [SerializeField, Min(0f)] private float emission = 4f;

    [Tooltip("Cuánto quedan en verde después de la largada (s).")]
    [SerializeField, Min(0f)] private float greenDuration = 2f;

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    private MaterialPropertyBlock block;
    private float countdownLength;
    private float greenUntil = -1f;
    private RaceManager.RaceState lastState;
    private int lastLit = -1;
    private bool lastGreen;

    private void Awake()
    {
        block = new MaterialPropertyBlock();

        if (raceManager == null)
            raceManager = FindFirstObjectByType<RaceManager>();

        Show(0, false);
    }

    private void Update()
    {
        if (raceManager == null)
            return;

        RaceManager.RaceState state = raceManager.CurrentState;

        if (state != lastState)
        {
            if (state == RaceManager.RaceState.Racing)
                greenUntil = Time.time + greenDuration;

            if (state == RaceManager.RaceState.Countdown)
                countdownLength = 0f;

            lastState = state;
        }

        switch (state)
        {
            case RaceManager.RaceState.Countdown:
                float left = Mathf.Max(0f, raceManager.CountdownTimer);
                countdownLength = Mathf.Max(countdownLength, left);

                int lit = lamps.Length;

                if (countdownLength > 0f)
                {
                    float step = countdownLength / lamps.Length;
                    lit = Mathf.Clamp(Mathf.FloorToInt((countdownLength - left) / step) + 1, 1, lamps.Length);
                }

                Show(lit, false);
                break;

            case RaceManager.RaceState.Racing:
                if (Time.time < greenUntil)
                    Show(lamps.Length, true);
                else
                    Show(0, false);
                break;

            default:
                Show(0, false);
                break;
        }
    }

    private void Show(int lit, bool green)
    {
        if (lit == lastLit && green == lastGreen)
            return;

        lastLit = lit;
        lastGreen = green;

        Color on = green ? greenColor : redColor;

        for (int i = 0; i < lamps.Length; i++)
        {
            if (lamps[i] == null)
                continue;

            bool isOn = i < lit;
            lamps[i].GetPropertyBlock(block);
            block.SetColor(ColorId, isOn ? on : offColor);
            block.SetColor(EmissionId, isOn ? on * emission : Color.black);
            lamps[i].SetPropertyBlock(block);
        }

        if (glow != null)
        {
            glow.enabled = lit > 0;
            glow.color = on;
        }
    }
}
