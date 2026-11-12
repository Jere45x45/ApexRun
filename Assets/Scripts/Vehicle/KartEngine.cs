using System;
using UnityEngine;

/// <summary>
/// Motor del kart con embrague centrífugo y una sola marcha.
///
/// El motor es un cuerpo que gira (cigüeñal + campana). El embrague lo une al
/// eje trasero a través de la cadena:
/// - Ralentí: las zapatas no tocan la campana; el kart no avanza.
/// - Al acelerar suben las revoluciones, las zapatas se abren y el embrague
///   empieza a transmitir torque patinando (el motor gira más rápido que el eje).
/// - Cuando las velocidades se igualan, el embrague queda trabado: motor y eje
///   giran juntos y el motor suma su inercia a la del eje.
/// - Sin acelerar, la fricción interna frena al kart (freno motor) hasta que las
///   revoluciones caen y el embrague se suelta.
/// - Salida desde parado: con el kart detenido queda armada la salida. Al
///   acelerar, larga con una relación más corta (launchRatio) y un embrague que
///   engancha arriba, con el motor cerca de su torque máximo: el empujón de la
///   largada. La relación se va alargando y, al llegar a gearRatio (a
///   launchEndRpm), la salida termina y el kart acelera normal. Si se suelta el
///   acelerador antes, también termina.
///
/// Se actualiza dentro de los subpasos de los neumáticos:
/// BeginSubstep (antes del paso del eje) y EndSubstep (después).
/// </summary>
public class KartEngine
{
    private readonly EngineSettings settings;
    private float previousSlip;

    /// <summary>Velocidad de giro del motor (rad/s).</summary>
    public float AngularVelocity { get; private set; }

    public float Rpm => EngineSettings.ToRpm(AngularVelocity);

    /// <summary>Acelerador, de 0 a 1.</summary>
    public float Throttle { get; set; }

    /// <summary>True si motor y eje giran juntos (el embrague no patina).</summary>
    public bool IsClutchLocked { get; private set; }

    /// <summary>Torque neto del motor (combustión menos fricción), en N·m.</summary>
    public float EngineTorque { get; private set; }

    /// <summary>Torque que pasa por el embrague hacia la cadena (N·m, del lado del motor).</summary>
    public float ClutchTorque { get; private set; }

    /// <summary>True si el limitador está cortando el encendido.</summary>
    public bool IsLimiterActive { get; private set; }

    /// <summary>True durante la salida desde parado (relación corta y embrague de salida).</summary>
    public bool IsLaunching { get; private set; }

    /// <summary>Relación motor/eje de ahora: launchRatio a gearRatio en la salida, si no gearRatio.</summary>
    public float CurrentRatio { get; private set; }

    // Por debajo de esto (rpm que tendría el motor con gearRatio, ~1,5 km/h con
    // ruedas de 0,18 m) el kart cuenta como detenido y la salida queda armada.
    private const float LaunchArmRpm = 150f;

    public KartEngine(EngineSettings settings)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        AngularVelocity = settings.IdleAngularVelocity;
        CurrentRatio = settings.gearRatio;
    }

    /// <summary>
    /// Pone el motor en un estado coherente con la velocidad del eje
    /// (por ejemplo, al reubicar el kart en movimiento).
    /// </summary>
    public void ResetState(float axleAngularVelocity)
    {
        IsLaunching = false;
        CurrentRatio = settings.gearRatio;

        float drivelineSpeed = axleAngularVelocity * settings.gearRatio;

        if (EngineSettings.ToRpm(drivelineSpeed) >= settings.clutchFullRpm)
        {
            AngularVelocity = drivelineSpeed;
            IsClutchLocked = true;
        }
        else
        {
            AngularVelocity = settings.IdleAngularVelocity;
            IsClutchLocked = false;
        }

        previousSlip = AngularVelocity - drivelineSpeed;
        ClutchTorque = 0f;
        EngineTorque = 0f;
        IsLimiterActive = false;
    }

    /// <summary>
    /// Antes del paso del eje: calcula el torque del motor y decide cuánto
    /// torque y cuánta inercia recibe el eje trasero.
    /// </summary>
    public void BeginSubstep(KartAxle axle)
    {
        if (axle == null)
            throw new ArgumentNullException(nameof(axle));

        float rpm = Rpm;
        UpdateLaunch(axle.AngularVelocity);
        float ratio = CurrentRatio;

        EngineTorque = ComputeEngineTorque(rpm);

        if (IsClutchLocked)
        {
            // Trabado: el motor gira con el eje. Su inercia, vista desde el eje,
            // se multiplica por la relación al cuadrado.
            axle.AdditionalInertia = settings.inertia * ratio * ratio;
            axle.DriveTorque = ToAxleTorque(EngineTorque);
            return;
        }

        // Patinando: el embrague transmite su capacidad, en el sentido que
        // tiende a igualar las velocidades de motor y eje.
        axle.AdditionalInertia = 0f;

        float slip = AngularVelocity - axle.AngularVelocity * ratio;

        ClutchTorque = Mathf.Abs(slip) < 0.0001f
            ? 0f
            : ClutchCapacity(rpm) * Mathf.Sign(slip);

        axle.DriveTorque = ToAxleTorque(ClutchTorque);
    }

    /// <summary>
    /// Después del paso del eje: actualiza el giro del motor y el estado
    /// del embrague (trabar o soltar).
    /// </summary>
    public void EndSubstep(KartAxle axle, float substepTime)
    {
        if (axle == null)
            throw new ArgumentNullException(nameof(axle));

        float drivelineSpeed = axle.AngularVelocity * CurrentRatio;

        if (IsClutchLocked)
        {
            float previous = AngularVelocity;
            AngularVelocity = drivelineSpeed;

            // Torque que el embrague tuvo que transmitir para que el motor
            // acompañe al eje. Si supera su capacidad, empieza a patinar.
            ClutchTorque = EngineTorque - settings.inertia * (AngularVelocity - previous) / substepTime;

            float capacity = ClutchCapacity(Rpm);

            if (Mathf.Abs(ClutchTorque) > capacity)
            {
                IsClutchLocked = false;
                ClutchTorque = capacity * Mathf.Sign(ClutchTorque);
            }
        }
        else
        {
            AngularVelocity += substepTime * (EngineTorque - ClutchTorque) / settings.inertia;

            // Si las velocidades se cruzaron en este subpaso, el embrague traba.
            // Justo después de soltarse el deslizamiento vale cero: ahí no cuenta
            // como cruce, si no el embrague trabaría y soltaría en cada subpaso.
            float slip = AngularVelocity - drivelineSpeed;
            bool crossed = previousSlip != 0f && slip * previousSlip <= 0f;

            if (crossed && ClutchCapacity(Rpm) > 0f)
            {
                AngularVelocity = drivelineSpeed;
                IsClutchLocked = true;
            }
        }

        // El motor no gira al revés.
        if (AngularVelocity < 0f)
        {
            AngularVelocity = 0f;
        }

        previousSlip = AngularVelocity - drivelineSpeed;
    }

    /// <summary>
    /// Pasa un torque del lado del motor al eje trasero. La cadena pierde una
    /// parte en los dos sentidos: al traccionar llega menos torque al eje, y
    /// con freno motor el eje tiene que vencer además esas pérdidas.
    /// </summary>
    private float ToAxleTorque(float engineSideTorque)
    {
        float ratio = CurrentRatio;
        float efficiency = settings.drivetrainEfficiency;

        return engineSideTorque >= 0f
            ? engineSideTorque * ratio * efficiency
            : engineSideTorque * ratio / efficiency;
    }

    /// <summary>
    /// Arma, sigue o termina la salida desde parado, y fija la relación de este subpaso.
    /// </summary>
    private void UpdateLaunch(float axleAngularVelocity)
    {
        if (!settings.HasLaunch)
        {
            IsLaunching = false;
            CurrentRatio = settings.gearRatio;
            return;
        }

        float rpmAtGear = settings.RpmAtGearRatio(axleAngularVelocity);

        if (rpmAtGear < LaunchArmRpm)
        {
            // Detenido: la salida queda armada.
            IsLaunching = true;
        }
        else if (IsLaunching && (rpmAtGear >= settings.launchEndRpm || Throttle < 0.1f))
        {
            // Ya salió (o soltó el acelerador): desde acá, aceleración normal.
            IsLaunching = false;
        }

        CurrentRatio = IsLaunching ? settings.EvaluateLaunchRatio(axleAngularVelocity) : settings.gearRatio;
    }

    /// <summary>Torque que puede transmitir el embrague ahora: el de la salida o el normal.</summary>
    private float ClutchCapacity(float rpm)
    {
        return IsLaunching ? settings.EvaluateLaunchClutchCapacity(rpm) : settings.EvaluateClutchCapacity(rpm);
    }

    private float ComputeEngineTorque(float rpm)
    {
        IsLimiterActive = rpm >= settings.maxRpm;

        // La curva es el torque a fondo ya descontada la fricción (lo que mide
        // un dinamómetro). La combustión a fondo es entonces curva + fricción,
        // y el acelerador la regula. Sin acelerar queda solo la fricción: freno motor.
        float friction = settings.EvaluateFriction(rpm);
        float fullCombustion = settings.EvaluateTorque(rpm) + friction;

        float combustion = IsLimiterActive
            ? 0f
            : fullCombustion * Mathf.Clamp01(Throttle);

        // Ralentí: por debajo del régimen de ralentí el carburador agrega
        // mezcla para que el motor no se pare.
        if (rpm < settings.idleRpm)
        {
            float idleBand = Mathf.Max(1f, settings.idleRpm * 0.2f);
            float idleOpening = Mathf.Clamp01((settings.idleRpm - rpm) / idleBand);

            combustion = Mathf.Max(combustion, fullCombustion * idleOpening);
        }

        return combustion - friction;
    }
}
