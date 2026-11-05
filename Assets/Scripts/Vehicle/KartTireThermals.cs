using System;
using UnityEngine;

/// <summary>
/// Temperatura y desgaste de los cuatro neumáticos.
///
/// Temperatura (de la banda de rodadura, una por rueda):
/// - Calienta lo que la goma desliza contra el piso (fuerza × velocidad de
///   deslizamiento, de costado y para adelante) y lo que se deforma al rodar
///   (la resistencia a la rodadura también termina en calor).
/// - Enfría el aire: más rápido cuanto más rápido va el kart y más si la
///   pista está mojada.
/// - Fría, la goma está dura y agarra menos; en su ventana agarra todo; si
///   se pasa de temperatura se ablanda de más y vuelve a perder agarre.
///
/// Desgaste: cada rueda pierde goma con la energía que gasta deslizando, y
/// mucho más si está recalentada. Una cubierta gastada agarra menos.
///
/// El resultado es un multiplicador de agarre por rueda
/// (KartWheel.ConditionGripMultiplier) que se aplica en el paso siguiente.
/// </summary>
public class KartTireThermals
{
    private readonly TireThermalSettings settings;
    private readonly float[] temperature;
    private readonly float[] wear;
    private readonly float[] slidingPower;

    public TireThermalSettings Settings => settings;

    public KartTireThermals(TireThermalSettings settings, int wheelCount)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));

        temperature = new float[wheelCount];
        wear = new float[wheelCount];
        slidingPower = new float[wheelCount];

        Reset();
    }

    /// <summary>Temperatura de la goma (°C) de una rueda (FL, FR, RL, RR).</summary>
    public float GetTemperature(int wheel) => temperature[wheel];

    /// <summary>Desgaste de una rueda, de 0 (nueva) a 1 (sin goma útil).</summary>
    public float GetWear(int wheel) => wear[wheel];

    /// <summary>Potencia que la rueda gasta deslizando en el último paso (W). Para calibrar.</summary>
    public float GetSlidingPower(int wheel) => slidingPower[wheel];

    /// <summary>Multiplicador de agarre de una rueda por temperatura y desgaste.</summary>
    public float GetGripMultiplier(int wheel) => settings.ThermalGrip(temperature[wheel]) * settings.WearGrip(wear[wheel]);

    /// <summary>Cubiertas nuevas a la temperatura de largada.</summary>
    public void Reset()
    {
        for (int i = 0; i < temperature.Length; i++)
        {
            temperature[i] = settings.startTemperature;
            wear[i] = 0f;
            slidingPower[i] = 0f;
        }
    }

    /// <summary>
    /// Avanza un paso de física con las fuerzas que acaban de hacer las ruedas
    /// y deja listo el agarre de cada una para el paso siguiente.
    /// </summary>
    public void Step(KartWheel[] wheels, TireSettings tire, float speed, float deltaTime)
    {
        if (deltaTime <= 0f)
            return;

        float wetness = Mathf.Clamp01(TrackConditions.Wetness);
        float ambient = settings.ambientTemperature - settings.wetAmbientDrop * wetness;
        float cooling = settings.coolingStill + settings.coolingPerSpeed * Mathf.Abs(speed);
        cooling *= 1f + settings.wetCoolingMultiplier * wetness;

        int count = Mathf.Min(wheels.Length, temperature.Length);

        for (int i = 0; i < count; i++)
        {
            KartWheel wheel = wheels[i];

            float sliding = 0f;
            float rolling = 0f;

            if (wheel.HasGrip)
            {
                float longitudinalSlip = wheel.LongitudinalVelocity - wheel.AngularVelocity * tire.radius;

                sliding =
                    Mathf.Abs(wheel.LateralForce * wheel.LateralVelocity) +
                    Mathf.Abs(wheel.LongitudinalForce * longitudinalSlip);

                rolling = tire.rollingResistance * wheel.VerticalLoad * Mathf.Abs(wheel.LongitudinalVelocity);
            }

            slidingPower[i] = sliding;

            float heat = settings.slidingHeatShare * sliding + settings.rollingHeatShare * rolling;
            float loss = cooling * (temperature[i] - ambient);

            temperature[i] += (heat - loss) / settings.heatCapacity * deltaTime;
            temperature[i] = Mathf.Clamp(temperature[i], ambient - 5f, 200f);

            float overheat = 1f + settings.overheatWearPerDegree * Mathf.Max(0f, temperature[i] - settings.optimalMax);
            wear[i] = Mathf.Min(1f, wear[i] + sliding * overheat * deltaTime / settings.wearEnergy);

            wheel.ConditionGripMultiplier = GetGripMultiplier(i);
        }
    }
}

/// <summary>Parámetros de temperatura y desgaste de los neumáticos (ver KartTireThermals).</summary>
[Serializable]
public class TireThermalSettings
{
    [Header("Temperatura")]
    [Tooltip("Temperatura del aire y de la pista (°C).")]
    public float ambientTemperature = 25f;

    [Tooltip("Temperatura de la goma al largar (°C). Un kart no tiene mantas: arranca algo tibio de la vuelta previa.")]
    public float startTemperature = 35f;

    [Tooltip("Cuánta energía hace falta para subir 1 °C la goma (J/°C). Más = calienta y enfría más lento.")]
    [Min(1f)]
    public float heatCapacity = 600f;

    [Tooltip("Parte de la potencia que se gasta deslizando que queda en la goma como calor.")]
    [Range(0f, 1f)]
    public float slidingHeatShare = 0.6f;

    [Tooltip("Parte de la resistencia a la rodadura que calienta la goma (se deforma al rodar).")]
    [Range(0f, 1f)]
    public float rollingHeatShare = 0.8f;

    [Tooltip("Enfriamiento con el kart quieto (W/°C).")]
    [Min(0f)]
    public float coolingStill = 2f;

    [Tooltip("Enfriamiento extra por cada m/s de velocidad (W/°C por m/s): el aire se lleva el calor.")]
    [Min(0f)]
    public float coolingPerSpeed = 0.35f;

    [Tooltip("Con la pista empapada, el aire y el agua están así de más fríos (°C).")]
    [Min(0f)]
    public float wetAmbientDrop = 8f;

    [Tooltip("Con la pista empapada, el agua enfría así de más (1 = el doble).")]
    [Min(0f)]
    public float wetCoolingMultiplier = 1.5f;

    [Header("Agarre según temperatura")]
    [Tooltip("A esta temperatura o menos (°C) la goma está fría del todo.")]
    public float coldTemperature = 20f;

    [Tooltip("Agarre con la goma fría del todo (1 = el normal).")]
    [Range(0.5f, 1f)]
    public float coldGrip = 0.88f;

    [Tooltip("Desde acá (°C) la goma agarra todo.")]
    public float optimalMin = 55f;

    [Tooltip("Hasta acá (°C) la goma agarra todo. Más caliente, se pasa.")]
    public float optimalMax = 80f;

    [Tooltip("A esta temperatura (°C) la goma está recalentada del todo.")]
    public float hotTemperature = 120f;

    [Tooltip("Agarre con la goma recalentada del todo.")]
    [Range(0.5f, 1f)]
    public float hotGrip = 0.86f;

    [Header("Desgaste")]
    [Tooltip("Energía gastada deslizando (J) que se lleva toda la goma útil de una cubierta.")]
    [Min(1f)]
    public float wearEnergy = 150000f;

    [Tooltip("Cuánto más gasta por cada °C arriba de la ventana (0,05 = 5 % más por grado).")]
    [Min(0f)]
    public float overheatWearPerDegree = 0.05f;

    [Tooltip("Agarre que se pierde con la cubierta gastada del todo (0,15 = 15 % menos).")]
    [Range(0f, 0.5f)]
    public float wornGripLoss = 0.15f;

    /// <summary>Agarre por temperatura: sube hasta la ventana y baja si se pasa.</summary>
    public float ThermalGrip(float temperature)
    {
        if (temperature < optimalMin)
        {
            float t = Mathf.InverseLerp(coldTemperature, optimalMin, temperature);
            return Mathf.Lerp(coldGrip, 1f, t * t * (3f - 2f * t));
        }

        if (temperature > optimalMax)
        {
            float t = Mathf.InverseLerp(optimalMax, hotTemperature, temperature);
            return Mathf.Lerp(1f, hotGrip, t);
        }

        return 1f;
    }

    /// <summary>Agarre por desgaste.</summary>
    public float WearGrip(float wear)
    {
        return 1f - wornGripLoss * Mathf.Clamp01(wear);
    }
}
