using System;
using UnityEngine;

/// <summary>
/// Parámetros del motor, el embrague centrífugo y la transmisión de un kart.
/// Los valores por defecto corresponden a un Rotax 125 MAX evo (categoría Senior):
/// 22 kW (30 HP) a 11.500 rpm y 21 N·m a 9.000 rpm según la hoja de datos de
/// Rotax, con una sola marcha por cadena.
/// Más adelante (paso 0.8) van a salir de EngineData a través de KartStats.
/// </summary>
[Serializable]
public class EngineSettings
{
    private const float RadPerSecToRpm = 60f / (2f * Mathf.PI);

    [Header("Curva de torque")]
    [Tooltip("Torque del motor a fondo medido en el cigüeñal (N·m) según las rpm. " +
             "Es el que figura en las hojas de datos: ya tiene descontada la fricción interna.")]
    public AnimationCurve torqueCurve = CreateDefaultTorqueCurve();

    [Header("Régimen")]
    [Tooltip("Revoluciones de ralentí: el carburador mantiene el motor andando.")]
    [Min(0f)]
    public float idleRpm = 2000f;

    [Tooltip("Limitador: por encima de estas revoluciones se corta el encendido.")]
    [Min(100f)]
    public float maxRpm = 14000f;

    [Tooltip("Inercia de giro del cigüeñal, volante y campana del embrague (kg·m²).")]
    [Min(0.0001f)]
    public float inertia = 0.0025f;

    [Header("Freno motor")]
    [Tooltip("Fricción interna del motor (N·m), siempre presente.")]
    [Min(0f)]
    public float frictionTorque = 1f;

    [Tooltip("Fricción adicional por cada rpm (N·m/rpm). Sin acelerar, frena al kart.")]
    [Min(0f)]
    public float frictionTorquePerRpm = 0.0003f;

    [Header("Embrague centrífugo")]
    [Tooltip("Revoluciones a las que las zapatas empiezan a tocar la campana. " +
             "Por debajo, el motor está desacoplado (Rotax: 2.500 rpm).")]
    [Min(0f)]
    public float clutchEngageRpm = 2500f;

    [Tooltip("Revoluciones a las que el embrague queda completamente acoplado (Rotax: ~4.000 rpm).")]
    [Min(0f)]
    public float clutchFullRpm = 4000f;

    [Tooltip("Torque que transmite el embrague a clutchFullRpm (N·m). Por encima sigue " +
             "creciendo con el cuadrado de las rpm, así que en marcha no patina.")]
    [Min(0f)]
    public float clutchFullTorque = 12f;

    [Header("Transmisión")]
    [Tooltip("Relación piñón/corona (vueltas del motor por cada vuelta del eje). 7 = piñón 12 / corona 84.")]
    [Min(0.1f)]
    public float gearRatio = 7f;

    [Tooltip("Rendimiento de la cadena: parte del torque que llega al eje.")]
    [Range(0.5f, 1f)]
    public float drivetrainEfficiency = 0.95f;

    public float IdleAngularVelocity => idleRpm / RadPerSecToRpm;

    public static float ToRpm(float angularVelocity) => angularVelocity * RadPerSecToRpm;

    /// <summary>Torque a fondo en el cigüeñal para unas revoluciones dadas (N·m).</summary>
    public float EvaluateTorque(float rpm)
    {
        return Mathf.Max(0f, torqueCurve.Evaluate(Mathf.Max(0f, rpm)));
    }

    /// <summary>Fricción interna del motor (N·m). Es el freno motor.</summary>
    public float EvaluateFriction(float rpm)
    {
        return frictionTorque + frictionTorquePerRpm * Mathf.Max(0f, rpm);
    }

    /// <summary>
    /// Torque que puede transmitir el embrague centrífugo (N·m).
    /// Las zapatas se abren por fuerza centrífuga (crece con rpm²) contra la
    /// precarga de sus resortes, que equivale a la fuerza a clutchEngageRpm.
    /// Por debajo de clutchEngageRpm no transmite nada.
    /// </summary>
    public float EvaluateClutchCapacity(float rpm)
    {
        if (rpm <= clutchEngageRpm)
            return 0f;

        float engageSquared = clutchEngageRpm * clutchEngageRpm;
        float fullSquared = Mathf.Max(clutchFullRpm * clutchFullRpm, engageSquared + 1f);

        return clutchFullTorque * (rpm * rpm - engageSquared) / (fullSquared - engageSquared);
    }

    private static AnimationCurve CreateDefaultTorqueCurve()
    {
        // Rotax 125 MAX evo: 21 N·m a 9.000 rpm y 18,3 N·m a 11.500 rpm (22 kW).
        // Por debajo de 6.000 rpm, con la válvula de escape cerrada, rinde poco.
        AnimationCurve curve = new AnimationCurve(
            new Keyframe(0f, 5f),
            new Keyframe(2000f, 7f),
            new Keyframe(3000f, 9f),
            new Keyframe(4000f, 11f),
            new Keyframe(6000f, 15f),
            new Keyframe(8000f, 19.5f),
            new Keyframe(9000f, 21f),
            new Keyframe(10000f, 20.5f),
            new Keyframe(11500f, 18.3f),
            new Keyframe(13000f, 14f),
            new Keyframe(14000f, 10f)
        );

        for (int i = 0; i < curve.length; i++)
        {
            curve.SmoothTangents(i, 0f);
        }

        return curve;
    }
}
