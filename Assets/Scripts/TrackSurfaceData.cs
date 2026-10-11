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

    /// <summary>Agarre en mojado de una superficie sin datos (o sin TrackSurface).</summary>
    public const float DefaultWetGrip = 0.7f;

    public string SurfaceName => surfaceName;

    public bool IsValidForTrack => validForTrack;

    public float GripMultiplier => gripMultiplier;

    public float RollingResistanceMultiplier => rollingResistanceMultiplier;

    public float WetGripMultiplier => wetGripMultiplier;
}