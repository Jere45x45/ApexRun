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

    public string SurfaceName => surfaceName;

    public bool IsValidForTrack => validForTrack;

    public float GripMultiplier => gripMultiplier;

    public float RollingResistanceMultiplier => rollingResistanceMultiplier;
}