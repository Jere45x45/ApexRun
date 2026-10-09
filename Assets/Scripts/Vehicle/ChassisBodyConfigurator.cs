using System;
using UnityEngine;

/// <summary>
/// Configura el Rigidbody del chasis con valores físicos explícitos.
/// No depende de los colliders para calcular la masa, el centro de masa
/// ni la inercia: los define a partir de datos del kart real.
/// </summary>
public static class ChassisBodyConfigurator
{
    public static void Configure(
        Rigidbody rigidbody,
        float mass,
        Vector3 centerOfMass,
        Vector3 inertiaBoxSize,
        float angularDamping)
    {
        if (rigidbody == null)
            throw new ArgumentNullException(nameof(rigidbody));

        if (mass <= 0f)
            throw new ArgumentOutOfRangeException(nameof(mass));

        if (inertiaBoxSize.x <= 0f ||
            inertiaBoxSize.y <= 0f ||
            inertiaBoxSize.z <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(inertiaBoxSize));
        }

        rigidbody.mass = mass;

        rigidbody.automaticCenterOfMass = false;
        rigidbody.centerOfMass = centerOfMass;

        rigidbody.automaticInertiaTensor = false;
        rigidbody.inertiaTensor = CalculateBoxInertia(mass, inertiaBoxSize);
        rigidbody.inertiaTensorRotation = Quaternion.identity;

        // La resistencia del aire se va a modelar explícitamente (paso 0.7).
        // Un damping lineal sería una fuerza falsa que frena siempre.
        rigidbody.linearDamping = 0f;
        rigidbody.angularDamping = Mathf.Max(0f, angularDamping);

        rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        // Discrete. Se probaron los tres modos:
        // - ContinuousDynamic frenaba el kart en seco contra un escalón sin que
        //   se inclinara (anulaba la respuesta de giro del choque).
        // - ContinuousSpeculative genera colisiones fantasma: el contacto
        //   especulativo toma bordes que el chasis no toca (por ejemplo, el
        //   comienzo de una rampa al ras del piso) y desvía el kart.
        // - Discrete responde bien a los choques y no tiene contactos fantasma.
        //   A 125 km/h el chasis avanza 0,35 m por paso (0,01 s), mucho menos que
        //   su largo (2 m) y su ancho (1,1 m), así que no puede saltar una pared
        //   de un paso al otro: se probó contra una de 10 cm de espesor.
        rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
    }

    /// <summary>
    /// Inercia de una caja sólida de masa uniforme:
    /// I = m/12 · (a² + b²) para cada eje.
    /// </summary>
    public static Vector3 CalculateBoxInertia(float mass, Vector3 size)
    {
        float x2 = size.x * size.x;
        float y2 = size.y * size.y;
        float z2 = size.z * size.z;

        return new Vector3(
            mass / 12f * (y2 + z2),
            mass / 12f * (x2 + z2),
            mass / 12f * (x2 + y2)
        );
    }
}
