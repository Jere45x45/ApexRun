using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Le pone al bot piezas al azar del catálogo (motor, ruedas, kit aero y
/// volante). El chasis es fijo, como en la carrera.
/// </summary>
public class BotRandomizer : MonoBehaviour
{
    [Header("Catalog")]
    [SerializeField] private PartCatalog catalog;

    [Header("Kart")]
    [SerializeField] private KartConfigurationController configuration;

    private static readonly PartType[] RandomTypes =
    {
        PartType.Engine,
        PartType.Wheels,
        PartType.AeroKit,
        PartType.SteeringWheel
    };

    private void Reset()
    {
        configuration = GetComponentInChildren<KartConfigurationController>();
    }

    public void RandomizeKart()
    {
        if (catalog == null)
        {
            Debug.LogError("BotRandomizer: falta asignar el PartCatalog.", this);
            return;
        }

        if (configuration == null)
        {
            Debug.LogError("BotRandomizer: falta asignar el KartConfigurationController.", this);
            return;
        }

        List<string> installed = new List<string>();

        foreach (PartType type in RandomTypes)
        {
            List<KartPart> parts = GetParts(type);

            if (parts.Count == 0)
                continue;

            KartPart part = parts[Random.Range(0, parts.Count)];
            configuration.InstallPart(part);
            installed.Add(type + "=" + part.name);
        }

        Debug.Log("Bot randomizado: " + string.Join(", ", installed));
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
