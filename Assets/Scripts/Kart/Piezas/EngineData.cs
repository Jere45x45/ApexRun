using UnityEngine;

[CreateAssetMenu(fileName = "New Engine", menuName = "Kart/Engine")]
public class EngineData : KartPart
{
    [Header("Motor")]
    public float motorTorque = 3000f;
    public float maxSpeed = 20f;

    [Header("Física nueva (Fase 0)")]
    [Tooltip("Motor, embrague y transmisión de la física nueva. " +
             "Cuando Race pase a la física nueva, reemplaza a motorTorque y maxSpeed.")]
    public EngineSettings engineSettings = new EngineSettings();

    public override PartType PartType => PartType.Engine;

    public override void Apply(KartStats stats)
    {
        stats.motorTorque = motorTorque;
        stats.maxSpeed = maxSpeed;

        // Copia: la física no tiene que poder modificar el asset.
        stats.engine = JsonUtility.FromJson<EngineSettings>(JsonUtility.ToJson(engineSettings));
    }

    public override void Install(RuntimeKartConfiguration configuration)
    {
        configuration.InstallEngine(this);
    }
}