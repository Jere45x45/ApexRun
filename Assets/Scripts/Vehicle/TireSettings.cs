using System;
using UnityEngine;

/// <summary>
/// Parámetros físicos de un neumático de kart.
/// Los comparten las cuatro ruedas. Más adelante (paso 0.8)
/// van a salir de WheelData a través de KartStats.
/// </summary>
[Serializable]
public class TireSettings
{
    [Header("Geometría")]
    [Min(0.01f)]
    public float radius = 0.165f;

    [Header("Rigidez vertical")]
    [Tooltip("Cuánto empuja el neumático por cada metro que se aplasta (N/m). " +
             "Un neumático de kart está entre 60.000 y 100.000 N/m.")]
    [Min(0f)]
    public float verticalStiffness = 70000f;

    [Tooltip("Amortiguación del neumático (N·s/m). Frena el rebote.")]
    [Min(0f)]
    public float verticalDamping = 1200f;

    [Header("Tope")]
    [Tooltip("Aplastamiento a partir del cual la goma ya no cede y empuja la llanta (m).")]
    [Min(0.001f)]
    public float maxDeflection = 0.05f;

    [Tooltip("Cuánto más duro se vuelve el neumático después del tope.")]
    [Min(1f)]
    public float bottomOutStiffnessMultiplier = 10f;

    [Header("Agarre lateral (Magic Formula de Pacejka)")]
    [Tooltip("Coeficiente de agarre máximo (μ). Un slick de kart sobre asfalto seco: 1,5 a 1,8.")]
    [Min(0f)]
    public float lateralFriction = 1.7f;

    [Tooltip("B: rigidez. Define en qué ángulo de deslizamiento se alcanza el agarre máximo.")]
    [Min(0.01f)]
    public float lateralB = 14f;

    [Tooltip("C: forma. Define cuánto agarre queda cuando el neumático ya patina (1,5 ≈ 70%).")]
    [Range(1f, 2f)]
    public float lateralC = 1.5f;

    [Tooltip("E: curvatura cerca del pico. 0 = pico redondeado.")]
    [Range(-2f, 1f)]
    public float lateralE = 0f;

    [Header("Agarre longitudinal (Magic Formula de Pacejka)")]
    [Tooltip("Coeficiente de agarre máximo al acelerar o frenar (μ).")]
    [Min(0f)]
    public float longitudinalFriction = 1.7f;

    [Tooltip("B: define con cuánto patinamiento se alcanza el agarre máximo (11,5 ≈ 12%).")]
    [Min(0.01f)]
    public float longitudinalB = 11.5f;

    [Tooltip("C: forma. 1,65 deja ~64% de agarre con la rueda bloqueada.")]
    [Range(1f, 2f)]
    public float longitudinalC = 1.65f;

    [Tooltip("E: curvatura cerca del pico.")]
    [Range(-2f, 1f)]
    public float longitudinalE = 0f;

    [Header("Deformación de la goma")]
    [Tooltip("Cuánto tiene que rodar la rueda (m) para que la fuerza lateral se arme. " +
             "Representa la goma que se deforma antes de patinar. Kart: 0,1 a 0,2 m.")]
    [Min(0.005f)]
    public float lateralRelaxationLength = 0.15f;

    [Tooltip("Ídem para la fuerza longitudinal (acelerar y frenar).")]
    [Min(0.005f)]
    public float longitudinalRelaxationLength = 0.08f;

    [Tooltip("Amortiguación de la goma a baja velocidad, como fracción del amortiguamiento crítico. " +
             "Evita que el kart quieto se balancee sobre la goma y que, despacio, se sacuda de lado a lado.")]
    [Range(0f, 1f)]
    public float carcassDampingRatio = 0.5f;

    [Tooltip("Por debajo de esta velocidad (m/s) actúa la amortiguación de la goma; se apaga de a poco hasta llegar a ella. " +
             "Despacio, la goma se relaja muy lento y queda como un resorte casi sin freno: con menos de ~4 m/s " +
             "el kart se sacudía de lado a lado (unas 5 veces por segundo) después de cualquier golpe de volante.")]
    [Min(0.01f)]
    public float lowSpeedDampingSpeed = 4f;

    [Header("Resistencia a la rodadura")]
    [Tooltip("Coeficiente de rodadura. Slick de kart sobre asfalto: ~0,015.")]
    [Min(0f)]
    public float rollingResistance = 0.015f;

    /// <summary>Pendiente de la curva lateral en el origen, por Newton de carga (1/rad).</summary>
    public float LateralStiffnessPerLoad => lateralB * lateralC * lateralFriction;

    /// <summary>Pendiente de la curva longitudinal en el origen, por Newton de carga.</summary>
    public float LongitudinalStiffnessPerLoad => longitudinalB * longitudinalC * longitudinalFriction;

    /// <summary>
    /// Ángulo de deslizamiento (rad) donde la curva lateral llega a su máximo.
    /// Fórmula exacta para E = 0: B·α = tan(π / 2C). Con E distinto de 0 es una aproximación.
    /// </summary>
    public float LateralPeakSlipAngle => Mathf.Tan(Mathf.PI / (2f * lateralC)) / lateralB;

    /// <summary>Patinamiento donde la curva longitudinal llega a su máximo (misma fórmula).</summary>
    public float LongitudinalPeakSlipRatio => Mathf.Tan(Mathf.PI / (2f * longitudinalC)) / longitudinalB;

    /// <summary>
    /// Coeficiente de fuerza lateral para un ángulo de deslizamiento (en radianes).
    /// Multiplicado por la carga vertical da la fuerza lateral en Newtons.
    /// </summary>
    public float EvaluateLateralCoefficient(float slipAngle)
    {
        return MagicFormula(slipAngle, lateralB, lateralC, lateralE, lateralFriction);
    }

    /// <summary>
    /// Coeficiente de fuerza longitudinal para un patinamiento (slip ratio).
    /// 0 = rueda rodando libre, ±1 = rueda bloqueada o girando el doble de rápido.
    /// </summary>
    public float EvaluateLongitudinalCoefficient(float slipRatio)
    {
        return MagicFormula(slipRatio, longitudinalB, longitudinalC, longitudinalE, longitudinalFriction);
    }

    /// <summary>
    /// Deslizamiento combinado (círculo de fricción).
    /// El neumático tiene un solo "presupuesto" de agarre: lo que usa para
    /// acelerar o frenar ya no lo tiene para doblar, y viceversa.
    /// Cada deslizamiento se divide por el valor donde da su máximo; los dos
    /// se combinan en un deslizamiento total, y la fuerza de ese total se
    /// reparte entre las dos direcciones en la misma proporción.
    /// Con uno solo de los dos deslizamientos da exactamente las curvas puras.
    /// </summary>
    public void EvaluateCombined(
        float slipRatio,
        float slipAngle,
        out float longitudinalCoefficient,
        out float lateralCoefficient)
    {
        float normalizedX = slipRatio / LongitudinalPeakSlipRatio;
        float normalizedY = slipAngle / LateralPeakSlipAngle;
        float combined = Mathf.Sqrt(normalizedX * normalizedX + normalizedY * normalizedY);

        if (combined < 1e-6f)
        {
            longitudinalCoefficient = 0f;
            lateralCoefficient = 0f;
            return;
        }

        longitudinalCoefficient =
            EvaluateLongitudinalCoefficient(combined * LongitudinalPeakSlipRatio) * normalizedX / combined;

        lateralCoefficient =
            EvaluateLateralCoefficient(combined * LateralPeakSlipAngle) * normalizedY / combined;
    }

    /// <summary>
    /// Amortiguación de la goma (N·s/m) para una carga dada, calculada para que
    /// el sistema masa–goma quede con el amortiguamiento pedido:
    /// k = rigidez·carga/σ, m = carga/g, c = 2ζ·√(k·m).
    /// </summary>
    public float CarcassDamping(float load, float stiffnessPerLoad, float relaxationLength)
    {
        return 2f * carcassDampingRatio * load *
               Mathf.Sqrt(stiffnessPerLoad / (relaxationLength * Physics.gravity.magnitude));
    }

    /// <summary>
    /// Magic Formula de Pacejka: D · sin(C · atan(Bx − E·(Bx − atan(Bx)))).
    /// Es simétrica: un deslizamiento negativo da un coeficiente negativo.
    /// </summary>
    private static float MagicFormula(float x, float b, float c, float e, float d)
    {
        float bx = b * x;
        return d * Mathf.Sin(c * Mathf.Atan(bx - e * (bx - Mathf.Atan(bx))));
    }
}
