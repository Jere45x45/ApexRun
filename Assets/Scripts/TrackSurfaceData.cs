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

    public string SurfaceName => surfaceName;

    public bool IsValidForTrack => validForTrack;

    public float GripMultiplier => gripMultiplier;
}