using UnityEngine;

[CreateAssetMenu(
    fileName = "TrackSurfaceData",
    menuName = "ApexRun/Race/Track Surface"
)]
public class TrackSurfaceData : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string surfaceName;

    [Header("Track Rules")]
    [SerializeField] private bool validForTrack = true;

    [Header("Physics")]
    [SerializeField] private float gripMultiplier = 1f;

    [Tooltip("Multiplica la resistencia a la rodadura del neumático. Asfalto = 1. " +
             "En pasto la goma se hunde y arrastra: el coeficiente pasa de ~0,015 a ~0,075 (x5).")]
    [SerializeField, Min(0f)] private float rollingResistanceMultiplier = 1f;

    [Tooltip("Rodadura con el kart casi quieto (el multiplicador de arriba vale desde fullRollingSpeed). " +
             "Así en la grava se puede salir despacio (también marcha atrás), aunque a velocidad frene fuerte. -1 = igual que arriba.")]
    [SerializeField, Min(-1f)] private float lowSpeedRollingMultiplier = -1f;

    [Tooltip("Velocidad desde la que la rodadura es la de rollingResistanceMultiplier (m/s).")]
    [SerializeField, Min(0.1f)] private float fullRollingSpeed = 6f;

    [Header("Relieve")]
    [Tooltip("Altura de las piedras y pozos que hacen saltar a las ruedas (m). 0 = lisa.")]
    [SerializeField, Min(0f)] private float bumpHeight;

    [Tooltip("Distancia entre un pozo y otro (m). Más chico = vibra más rápido.")]
    [SerializeField, Min(0.1f)] private float bumpLength = 1f;

    [Tooltip("Agarre con la superficie empapada, como fracción del agarre en seco (1 = no cambia). " +
             "Se aplica de a poco según qué tan mojada esté la pista (ver RaceWeather).")]
    [SerializeField, Range(0.1f, 1f)] private float wetGripMultiplier = DefaultWetGrip;

    /// <summary>Qué levantan las ruedas al pisar esta superficie (ver KartEffects).</summary>
    public enum SurfaceEffect
    {
        /// <summary>Asfalto si es válida para la pista; tierra si no (pasto, grava).</summary>
        Auto,
        /// <summary>Humo al derrapar y agua con lluvia.</summary>
        Smoke,
        /// <summary>Terrones y polvo del color de la superficie.</summary>
        Dirt,
        None,
        /// <summary>Piedritas que saltan y un poco de polvo claro (grava).</summary>
        Gravel
    }

    [Header("Efectos")]
    [Tooltip("Qué levantan las ruedas: humo al derrapar y agua con lluvia (asfalto), tierra (pasto) o piedritas (grava). " +
             "Auto = asfalto si es válida para la pista, tierra si no.")]
    [SerializeField] private SurfaceEffect effect = SurfaceEffect.Auto;

    [Tooltip("Color de la tierra (o de las piedritas) que levantan las ruedas.")]
    [SerializeField] private Color dirtColor = DefaultDirtColor;

    /// <summary>Agarre en mojado de una superficie sin datos (o sin TrackSurface).</summary>
    public const float DefaultWetGrip = 0.7f;

    private static readonly Color DefaultDirtColor = new Color(0.36f, 0.29f, 0.2f);

    public string SurfaceName => surfaceName;

    public bool IsValidForTrack => validForTrack;

    public float GripMultiplier => gripMultiplier;

    public float RollingResistanceMultiplier => rollingResistanceMultiplier;

    /// <summary>Multiplicador de rodadura a esta velocidad (m/s): más bajo casi quieto, si la superficie lo pide.</summary>
    public float RollingMultiplierAt(float speed)
    {
        if (lowSpeedRollingMultiplier < 0f)
            return rollingResistanceMultiplier;

        return Mathf.Lerp(lowSpeedRollingMultiplier, rollingResistanceMultiplier, Mathf.Abs(speed) / fullRollingSpeed);
    }

    public bool HasBumps => bumpHeight > 0f;

    /// <summary>
    /// Cuánto sobresale el piso en este punto por el relieve (m, de 0 a
    /// bumpHeight). Depende del lugar, así cada piedra está siempre en el
    /// mismo sitio.
    /// </summary>
    public float BumpAt(Vector3 point)
    {
        if (bumpHeight <= 0f)
            return 0f;

        float large = Mathf.PerlinNoise(point.x / bumpLength + 31.7f, point.z / bumpLength + 12.3f);
        float small = Mathf.PerlinNoise(point.x / (bumpLength * 0.45f) + 5.1f, point.z / (bumpLength * 0.45f) + 77.9f);

        return bumpHeight * Mathf.Clamp01(0.65f * large + 0.35f * small);
    }

    public float WetGripMultiplier => wetGripMultiplier;

    public Color DirtColor => dirtColor;

    /// <summary>Efecto de una superficie, ya resuelto. Sin datos (null) cuenta como asfalto.</summary>
    public static SurfaceEffect EffectOf(TrackSurfaceData surface)
    {
        if (surface == null)
            return SurfaceEffect.Smoke;

        if (surface.effect != SurfaceEffect.Auto)
            return surface.effect;

        return surface.validForTrack ? SurfaceEffect.Smoke : SurfaceEffect.Dirt;
    }

    public static Color DirtColorOf(TrackSurfaceData surface)
    {
        return surface != null ? surface.dirtColor : DefaultDirtColor;
    }
}
