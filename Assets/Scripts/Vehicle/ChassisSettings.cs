using System;
using UnityEngine;

/// <summary>
/// Parámetros físicos del chasis para la física nueva: masa, inercia,
/// bastidor, eje trasero, frenos y geometría de dirección.
/// Los valores por defecto corresponden al chasis BasicCh con piloto.
/// </summary>
[Serializable]
public class ChassisSettings
{
    [Header("Masa (kart + piloto)")]
    [Tooltip("Masa total en marcha: kart, piloto y combustible (kg).")]
    [Min(1f)]
    public float mass = 170f;

    [Tooltip("Centro de masa local. Con los ejes de BasicCh (z = +0,4 y z = −1,197), " +
             "z = −0,56 reparte el peso 40 % adelante / 60 % atrás.")]
    public Vector3 centerOfMass = new Vector3(0f, 0.12f, -0.56f);

    [Tooltip("Caja aproximada que ocupa la masa, usada para calcular la inercia (m).")]
    public Vector3 inertiaBoxSize = new Vector3(1.35f, 0.6f, 2.0f);

    [Min(0f)]
    public float angularDamping = 0.05f;

    [Header("Bastidor")]
    [Tooltip("Rigidez a la torsión del bastidor entre el eje delantero y el trasero (N·m/°). " +
             "El caño solo ronda 190 N·m/° (cálculo por elementos finitos); asiento, soportes y motor lo rigidizan. " +
             "Más rígido = la dirección levanta más la trasera interior.")]
    [Min(1f)]
    public float torsionalStiffness = 400f;

    [Header("Eje trasero y frenos")]
    [Tooltip("Inercia de giro del eje trasero sin las ruedas: caño, disco, corona y mazas (kg·m²).")]
    [Min(0.001f)]
    public float rearAxleInertia = 0.12f;

    [Tooltip("Torque de freno en el eje trasero con el freno a fondo (N·m). Un kart frena solo atrás.")]
    [Min(0f)]
    public float rearBrakeTorque = 200f;

    [Tooltip("Torque de freno en cada delantera (N·m). 0 en karts sin freno delantero.")]
    [Min(0f)]
    public float frontBrakeTorque = 0f;

    [Header("Dirección")]
    public SteeringSettings steering = new SteeringSettings();
}
