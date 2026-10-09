using System;
using UnityEngine;

/// <summary>
/// Geometría de la dirección delantera del kart.
///
/// - Ackermann: en una curva la rueda interior recorre un círculo más chico,
///   así que tiene que girar más que la exterior para no arrastrarse.
/// - Caster (avance) e inclinación del pivote (KPI): el eje sobre el que gira
///   cada mangueta está inclinado. Al girar el volante, la rueda delantera
///   interior baja y la exterior sube respecto del chasis. Como el kart no
///   tiene suspensión, eso carga en diagonal la delantera interior y la trasera exterior,
///   y descarga la trasera interior. Es lo que permite doblar a un kart sin
///   diferencial: la trasera interior se levanta y el eje rígido deja de frenar.
///   El bastidor de un kart se tuerce bastante, así que solo una parte de ese
///   movimiento llega a las ruedas (ver KartSteering.ChassisTwistFactor).
///
/// Valores por defecto: estimaciones típicas de un kart; cada chasis los ajusta.
/// </summary>
[Serializable]
public class SteeringSettings
{
    [Tooltip("Ángulo máximo de dirección (grados) de la rueda imaginaria en el centro del eje delantero. " +
             "Con Ackermann, la rueda interior gira algo más.")]
    [Range(5f, 40f)]
    public float maxSteerAngle = 25f;

    [Tooltip("Ackermann: 0 = las dos ruedas giran igual (paralelas); 1 = la interior gira " +
             "exactamente lo necesario para seguir su círculo sin arrastrarse.")]
    [Range(0f, 1f)]
    public float ackermann = 1f;

    [Tooltip("Caster (avance): inclinación hacia atrás del eje de la mangueta, en grados. " +
             "Más caster = más se levanta la trasera interior al girar.")]
    [Range(0f, 30f)]
    public float casterAngle = 15f;

    [Tooltip("Inclinación del eje de la mangueta hacia adentro (KPI), en grados. " +
             "Al girar, baja las dos delanteras: el frente sube y el volante tiende a volver al centro.")]
    [Range(0f, 25f)]
    public float kingpinInclination = 12f;

    [Tooltip("Distancia del eje de la mangueta al centro de la rueda (m). " +
             "Junto con el caster, define cuánto sube o baja cada rueda al girar.")]
    [Min(0f)]
    public float spindleLength = 0.12f;
}
