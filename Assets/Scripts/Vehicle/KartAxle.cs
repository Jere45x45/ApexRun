using System;
using UnityEngine;

/// <summary>
/// Un eje que gira: una o más ruedas que comparten la misma velocidad de giro.
///
/// - Las traseras de un kart van montadas en un eje rígido, sin diferencial:
///   las dos giran siempre juntas. En curva, la de adentro recorre menos
///   camino y tiene que patinar un poco. Eso es lo que hace que un kart
///   "empuje de trompa" si no se levanta la rueda interior.
/// - Cada delantera gira suelta en su mangueta: es un eje de una sola rueda.
///
/// El eje recibe torque de motor y de freno, y el piso lo frena o lo empuja
/// a través de la fuerza longitudinal de cada neumático.
/// </summary>
public class KartAxle
{
    private readonly KartWheel[] wheels;

    public string Name { get; }

    /// <summary>Inercia de giro del eje con sus ruedas, disco y corona (kg·m²).</summary>
    public float Inertia { get; }

    /// <summary>
    /// Inercia extra de algo que gira solidario con el eje (kg·m²). Por ejemplo,
    /// el motor con el embrague trabado: su inercia vista desde el eje se
    /// multiplica por la relación de transmisión al cuadrado.
    /// </summary>
    public float AdditionalInertia { get; set; }

    /// <summary>Inercia total que se opone a los cambios de giro (kg·m²).</summary>
    public float TotalInertia => Inertia + Mathf.Max(0f, AdditionalInertia);

    /// <summary>Velocidad de giro (rad/s). Positiva = rodando hacia adelante.</summary>
    public float AngularVelocity { get; private set; }

    /// <summary>Torque del motor sobre el eje (N·m). Negativo = freno motor.</summary>
    public float DriveTorque { get; set; }

    /// <summary>Torque máximo de freno (N·m). Siempre se opone al giro.</summary>
    public float BrakeTorque { get; set; }

    /// <summary>Límite de seguridad para el giro (rad/s).</summary>
    public float MaxAngularVelocity { get; set; } = 400f;

    /// <summary>True si el freno tiene el eje bloqueado.</summary>
    public bool IsLocked { get; private set; }

    public KartAxle(string name, float inertia, params KartWheel[] wheels)
    {
        if (inertia <= 0f)
            throw new ArgumentOutOfRangeException(nameof(inertia));

        if (wheels == null || wheels.Length == 0)
            throw new ArgumentException("Un eje necesita al menos una rueda.", nameof(wheels));

        Name = name;
        Inertia = inertia;
        this.wheels = wheels;
    }

    /// <summary>Fija el giro (por ejemplo, para que coincida con la velocidad al reubicar el kart).</summary>
    public void SetAngularVelocity(float angularVelocity)
    {
        AngularVelocity = angularVelocity;
    }

    /// <summary>
    /// Avanza un subpaso: deforma la goma de cada rueda, actualiza el giro del
    /// eje y calcula la fuerza longitudinal final de cada rueda.
    /// </summary>
    public void Step(float substepTime, TireSettings tire)
    {
        float radius = tire.radius;
        float inertia = TotalInertia;

        float springTorque = 0f;
        float dampingSum = 0f;
        float dampingVelocitySum = 0f;
        float rollingTorque = 0f;

        foreach (KartWheel wheel in wheels)
        {
            wheel.UpdateDeflections(AngularVelocity, substepTime, tire);

            if (!wheel.HasGrip)
                continue;

            // El piso empuja a la goma; la reacción frena (o arrastra) el giro.
            springTorque += wheel.SubstepSpringForce * radius;

            float damping = wheel.SubstepDampingCoefficient;
            dampingSum += damping * radius * radius;
            dampingVelocitySum += damping * radius * wheel.LongitudinalVelocity;

            // En pasto o tierra la rueda se hunde y arrastra más que en asfalto.
            float surfaceRolling = wheel.CurrentSurface != null
                ? Mathf.Max(0f, wheel.CurrentSurface.RollingResistanceMultiplier)
                : 1f;

            rollingTorque += tire.rollingResistance * surfaceRolling * wheel.VerticalLoad * radius;
        }

        // Integración del giro. La amortiguación de la goma se trata en forma
        // implícita: es muy rígida comparada con la inercia de una rueda y, en
        // forma explícita, haría oscilar el giro.
        float omega =
            (AngularVelocity + substepTime / inertia * (DriveTorque - springTorque + dampingVelocitySum)) /
            (1f + substepTime * dampingSum / inertia);

        // Freno y rodadura: se oponen al giro pero nunca lo invierten.
        // Si alcanzan para frenarlo del todo en este subpaso, el eje queda bloqueado.
        float resistance = (Mathf.Max(0f, BrakeTorque) + rollingTorque) * substepTime / inertia;

        if (Mathf.Abs(omega) <= resistance)
        {
            omega = 0f;
            IsLocked = BrakeTorque > 0f;
        }
        else
        {
            omega -= Mathf.Sign(omega) * resistance;
            IsLocked = false;
        }

        AngularVelocity = Mathf.Clamp(omega, -MaxAngularVelocity, MaxAngularVelocity);

        foreach (KartWheel wheel in wheels)
        {
            wheel.FinishSubstep(AngularVelocity, substepTime, tire);
        }
    }
}
