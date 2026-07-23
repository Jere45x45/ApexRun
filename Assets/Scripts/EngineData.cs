using UnityEngine;

[CreateAssetMenu(fileName = "New Engine", menuName = "Kart/Engine")]

public class EngineData : KartPart
{
    [Header("Motor")]
    public float motorTorque = 3000f;

    public float maxSpeed = 20f;

    public override void Apply(KartStats stats)
    {
        stats.motorTorque = motorTorque;
    }
}