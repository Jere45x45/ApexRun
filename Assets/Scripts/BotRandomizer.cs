using System.Collections.Generic;
using UnityEngine;

public class BotRandomizer : MonoBehaviour
{
    [Header("Catalog")]
    [SerializeField] private PartCatalog catalog;

    [Header("Kart")]
    [SerializeField] private BotBehaviour bot;

    public void RandomizeKart()
    {
        if (catalog == null)
        {
            Debug.LogError("BotRandomizer: falta asignar el PartCatalog.", this);
            return;
        }

        if (bot == null)
        {
            Debug.LogError("BotRandomizer: falta asignar el BotBehaviour.", this);
            return;
        }

        List<KartPart> engines = GetParts(PartType.Engine);
        List<KartPart> chassis = GetParts(PartType.Chassis);
        List<KartPart> wheels = GetParts(PartType.Wheels);
        List<KartPart> aeroKits = GetParts(PartType.AeroKit);

        if (engines.Count == 0 || chassis.Count == 0 || wheels.Count == 0 || aeroKits.Count == 0)
        {
            Debug.LogError
            (
                "BotRandomizer: falta al menos una pieza de algún tipo.",
                this
            );

            return;
        }

        EngineData engine = engines[Random.Range(0, engines.Count)] as EngineData;

        ChassisData chassisPart = chassis[Random.Range(0, chassis.Count)] as ChassisData;

        WheelData wheelsPart = wheels[Random.Range(0, wheels.Count)] as WheelData;

        AeroKitData aeroKit = aeroKits[Random.Range(0, aeroKits.Count)] as AeroKitData;

        bot.SetRandomConfiguration(
            engine,
            chassisPart,
            wheelsPart,
            aeroKit
        );

        Debug.Log(
            $"Bot randomizado: " +
            $"Engine={engine.name}, " +
            $"Chassis={chassisPart.name}, " +
            $"Wheels={wheelsPart.name}, " +
            $"Aero={aeroKit.name}"
        );
    }

    private List<KartPart> GetParts(PartType type)
    {
        List<KartPart> result = new List<KartPart>();

        foreach (KartPart part in catalog.GetParts(type))
        {
            if (part != null)
            {
                result.Add(part);
            }
        }

        return result;
    }
}
