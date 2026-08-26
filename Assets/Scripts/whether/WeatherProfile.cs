using UnityEngine;

[CreateAssetMenu(fileName = "New Weather Profile", menuName = "Weather Profile")]
public class WeatherProfile : ScriptableObject
{
    [Header("Física")]
    [Tooltip("1.0 es normal. 0.5 es la mitad de agarre (resbaladizo).")]
    [Range(0.1f, 1f)] 
    public float gripMultiplier = 1f;

    [Header("Visibilidad (Niebla)")]
    public float fogDensity = 0.01f;
    public Color fogColor = new Color(0.5f, 0.5f, 0.5f);

    [Header("Partículas (Emisión por segundo)")]
    public float rainEmissionRate = 0f;
    public float snowEmissionRate = 0f;
}