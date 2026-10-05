using System;
using UnityEngine;

/// <summary>
/// Parámetros aerodinámicos del kart con piloto.
/// Valores por defecto: coeficiente de resistencia 0,8 y área frontal 0,575 m²
/// (CdA ≈ 0,46 m²), dentro de lo medido para karts con piloto (Cx entre 0,6 y 0,9).
/// El piloto (casco, hombros y brazos) es la mayor parte de esa resistencia.
/// La carga aerodinámica es chica: unos 30 N a 90 km/h.
/// Más adelante (paso 0.8) van a salir de AeroKitData a través de KartStats.
/// </summary>
[Serializable]
public class AeroSettings
{
    [Tooltip("Densidad del aire (kg/m³). 1,225 a nivel del mar y 15 °C.")]
    [Min(0f)]
    public float airDensity = 1.225f;

    [Tooltip("Coeficiente de resistencia × área frontal (m²). Kart con piloto: 0,35 a 0,55.")]
    [Min(0f)]
    public float dragArea = 0.46f;

    [Tooltip("Coeficiente de carga aerodinámica × área (m²). Positivo = empuja el kart contra el piso.")]
    public float downforceArea = 0.08f;

    [Tooltip("Centro de presión en coordenadas locales del kart: donde actúan la resistencia y la carga. " +
             "Está alto (torso y casco del piloto), así que la resistencia pasa peso de adelante hacia atrás.")]
    public Vector3 centerOfPressure = new Vector3(0f, 0.3f, -0.5f);

    /// <summary>Presión dinámica: ½ · ρ · v² (Pa).</summary>
    public float DynamicPressure(float airSpeed)
    {
        return 0.5f * airDensity * airSpeed * airSpeed;
    }
}
