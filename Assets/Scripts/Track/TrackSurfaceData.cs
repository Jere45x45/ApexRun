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
        None
    }

    [Header("Efectos")]
    [Tooltip("Qué levantan las ruedas: humo al derrapar y agua con lluvia (asfalto) o tierra (pasto, grava). " +
             "Auto = asfalto si es válida para la pista, tierra si no.")]
    [SerializeField] private SurfaceEffect effect = SurfaceEffect.Auto;

    [Tooltip("Color de la tierra que levantan las ruedas.")]
    [SerializeField] private Color dirtColor = DefaultDirtColor;

    /// <summary>Agarre en mojado de una superficie sin datos (o sin TrackSurface).</summary>
    public const float DefaultWetGrip = 0.7f;

    private static readonly Color DefaultDirtColor = new Color(0.36f, 0.29f, 0.2f);

    public string SurfaceName => surfaceName;

    public bool IsValidForTrack => validForTrack;

    public float GripMultiplier => gripMultiplier;

    public float RollingResistanceMultiplier => rollingResistanceMultiplier;

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