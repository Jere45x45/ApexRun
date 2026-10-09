using System;
using UnityEngine;

/// <summary>
/// Dirección del kart: reparte el ángulo de volante entre las dos ruedas
/// delanteras (Ackermann) y mueve cada rueda según la geometría de su
/// mangueta (caster e inclinación del pivote).
///
/// El ángulo de entrada es el de una rueda imaginaria en el centro del eje
/// delantero: con él, el kart doblaría alrededor de un punto a la altura del
/// eje trasero, a una distancia de batalla / tan(ángulo).
/// </summary>
public class KartSteering
{
    private readonly SteeringSettings settings;
    private readonly KartWheel leftWheel;
    private readonly KartWheel rightWheel;
    private readonly float wheelbase;

    /// <summary>Ángulo de la rueda delantera izquierda (grados).</summary>
    public float LeftAngle { get; private set; }

    /// <summary>Ángulo de la rueda delantera derecha (grados).</summary>
    public float RightAngle { get; private set; }

    public float Wheelbase => wheelbase;

    /// <summary>
    /// Parte del movimiento cruzado de las manguetas (una rueda sube y la otra
    /// baja) que llega a las cargas de las ruedas, de 0 a 1. El chasis es un
    /// cuerpo rígido en la física, pero un bastidor de kart real se tuerce: la
    /// torsión absorbe el resto. 1 = bastidor infinitamente rígido.
    /// </summary>
    public float ChassisTwistFactor { get; set; } = 1f;

    public KartSteering(SteeringSettings settings, KartWheel frontLeft, KartWheel frontRight, float rearAxleZ)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        leftWheel = frontLeft ?? throw new ArgumentNullException(nameof(frontLeft));
        rightWheel = frontRight ?? throw new ArgumentNullException(nameof(frontRight));

        float frontAxleZ = (frontLeft.LocalPosition.z + frontRight.LocalPosition.z) * 0.5f;
        wheelbase = frontAxleZ - rearAxleZ;

        if (wheelbase <= 0f)
            throw new ArgumentException("Las ruedas delanteras tienen que estar delante del eje trasero.");

        if (frontRight.LocalPosition.x <= frontLeft.LocalPosition.x)
            throw new ArgumentException("La rueda derecha tiene que estar a la derecha de la izquierda.");
    }

    /// <summary>
    /// Gira las ruedas delanteras. Positivo = hacia la derecha (grados).
    /// Va antes de calcular los contactos, porque mueve el centro de cada rueda.
    /// </summary>
    public void Apply(float steerAngle)
    {
        // Ancho entre los ejes de las manguetas (no entre los centros de las ruedas).
        float wheelTrack = rightWheel.LocalPosition.x - leftWheel.LocalPosition.x;
        float kingpinTrack = Mathf.Max(0.01f, wheelTrack - 2f * settings.spindleLength);

        ComputeWheelAngles(steerAngle, kingpinTrack, out float leftAngle, out float rightAngle);

        LeftAngle = leftAngle;
        RightAngle = rightAngle;

        leftWheel.SteerAngle = leftAngle;
        rightWheel.SteerAngle = rightAngle;

        Vector3 leftOffset = SpindleOffset(-1f, leftAngle);
        Vector3 rightOffset = SpindleOffset(1f, rightAngle);

        // Lo que suben o bajan las dos ruedas juntas solo inclina el chasis.
        // Lo cruzado (una sube, la otra baja) es lo que pasa carga en diagonal,
        // y una parte se pierde en la torsión del bastidor.
        float together = (leftOffset.y + rightOffset.y) * 0.5f;
        float crossed = (leftOffset.y - rightOffset.y) * 0.5f * Mathf.Clamp01(ChassisTwistFactor);

        leftOffset.y = together + crossed;
        rightOffset.y = together - crossed;

        leftWheel.GeometryOffset = leftOffset;
        rightWheel.GeometryOffset = rightOffset;
    }

    /// <summary>
    /// Ackermann: cada rueda apunta perpendicular a la línea que la une con el
    /// centro de giro. La interior queda con más ángulo que la exterior.
    /// </summary>
    private void ComputeWheelAngles(float steerAngle, float kingpinTrack, out float leftAngle, out float rightAngle)
    {
        float absAngle = Mathf.Abs(steerAngle);

        if (absAngle < 0.001f)
        {
            leftAngle = steerAngle;
            rightAngle = steerAngle;
            return;
        }

        // Distancia del centro del eje trasero al centro de giro.
        float turnRadius = wheelbase / Mathf.Tan(absAngle * Mathf.Deg2Rad);

        float idealInner = Mathf.Atan2(wheelbase, turnRadius - kingpinTrack * 0.5f) * Mathf.Rad2Deg;
        float idealOuter = Mathf.Atan2(wheelbase, turnRadius + kingpinTrack * 0.5f) * Mathf.Rad2Deg;

        float inner = Mathf.Lerp(absAngle, idealInner, settings.ackermann);
        float outer = Mathf.Lerp(absAngle, idealOuter, settings.ackermann);

        // Doblando a la derecha, la rueda interior es la derecha.
        if (steerAngle > 0f)
        {
            rightAngle = inner;
            leftAngle = outer;
        }
        else
        {
            leftAngle = -inner;
            rightAngle = -outer;
        }
    }

    /// <summary>
    /// Cuánto se mueve el centro de la rueda (en coordenadas del kart) al girar
    /// la mangueta sobre su eje inclinado.
    /// side: −1 = izquierda, +1 = derecha.
    /// </summary>
    private Vector3 SpindleOffset(float side, float wheelAngle)
    {
        if (settings.spindleLength <= 0f || Mathf.Abs(wheelAngle) < 0.001f)
            return Vector3.zero;

        float caster = settings.casterAngle * Mathf.Deg2Rad;
        float kingpinInclination = settings.kingpinInclination * Mathf.Deg2Rad;

        // Eje de la mangueta: la parte de arriba va hacia atrás (caster)
        // y hacia el centro del kart (KPI).
        Vector3 axis = new Vector3(-side * Mathf.Tan(kingpinInclination), 1f, -Mathf.Tan(caster)).normalized;

        // La punta de eje sale del pivote hacia afuera, perpendicular al eje.
        Vector3 spindle = Vector3.ProjectOnPlane(new Vector3(side, 0f, 0f), axis).normalized * settings.spindleLength;

        // Girar la mangueta es rotar la punta de eje alrededor del eje inclinado.
        Vector3 rotated = Quaternion.AngleAxis(wheelAngle, axis) * spindle;

        return rotated - spindle;
    }
}
