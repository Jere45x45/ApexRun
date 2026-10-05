using System;
using UnityEngine;

/// <summary>
/// Fuerzas del aire sobre el kart:
/// - Resistencia: opuesta a la velocidad y proporcional a v². A baja velocidad
///   casi no existe; a fondo en recta es la fuerza que limita la velocidad máxima.
/// - Carga aerodinámica: chica, hacia el piso del kart, también proporcional a v².
///
/// Las dos se aplican en el centro de presión. Como la resistencia actúa alto,
/// además pasa peso de las ruedas delanteras a las traseras.
/// No hay viento: la velocidad respecto del aire es la del kart en ese punto.
/// </summary>
public class KartAerodynamics
{
    private const float MinAirSpeed = 0.01f;

    private readonly AeroSettings settings;

    /// <summary>Resistencia del aire en el último paso (N).</summary>
    public float DragForce { get; private set; }

    /// <summary>Carga aerodinámica en el último paso (N). Positiva = hacia el piso.</summary>
    public float Downforce { get; private set; }

    public KartAerodynamics(AeroSettings settings)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>Calcula y aplica las fuerzas del aire. Va en FixedUpdate.</summary>
    public void Apply(Rigidbody body)
    {
        if (body == null)
            throw new ArgumentNullException(nameof(body));

        Vector3 point = body.position + body.rotation * settings.centerOfPressure;
        Vector3 airVelocity = body.GetPointVelocity(point);
        float airSpeed = airVelocity.magnitude;

        if (airSpeed < MinAirSpeed)
        {
            DragForce = 0f;
            Downforce = 0f;
            return;
        }

        float dynamicPressure = settings.DynamicPressure(airSpeed);

        DragForce = dynamicPressure * settings.dragArea;
        Downforce = dynamicPressure * settings.downforceArea;

        Vector3 kartDown = body.rotation * Vector3.down;
        Vector3 force = -airVelocity / airSpeed * DragForce + kartDown * Downforce;

        body.AddForceAtPosition(force, point, ForceMode.Force);
    }
}
