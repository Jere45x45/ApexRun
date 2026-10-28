using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Asigna combinaciones visuales diferentes a los bots.
/// Las piezas se eligen del catálogo, pero sus estadísticas
/// no se aplican a la configuración de conducción.
/// El chasis permanece fijo.
/// </summary>
public class BotRandomizer : MonoBehaviour
{
    [Header("Catálogo")]
    [SerializeField] private PartCatalog catalog;

    [Header("Kart")]
    [SerializeField] private KartConfigurationController configuration;

    [Header("Variedad visual")]
    [SerializeField] private bool randomEngine = true;
    [SerializeField] private bool randomWheels = true;
    [SerializeField] private bool randomAeroKit = true;
    [SerializeField] private bool randomSteeringWheel = true;

    private bool applied;

    private void Reset()
    {
        configuration =
            GetComponentInChildren<KartConfigurationController>();
    }

    private void Start()
    {
        if (!applied)
            RandomizeKart();
    }

    public void RandomizeKart()
    {
        if (applied)
            return;

        if (catalog == null || configuration == null)
        {
            Debug.LogError(
                "BotRandomizer: asigná el catálogo y la configuración.",
                this
            );
            return;
        }

        // Reinicia la configuración para no conservar piezas anteriores.
        configuration.ResetToBaseConfiguration();

        if (randomEngine)
            InstallRandomPart(PartType.Engine);

        if (randomWheels)
            InstallRandomPart(PartType.Wheels);

        if (randomAeroKit)
            InstallRandomPart(PartType.AeroKit);

        if (randomSteeringWheel)
            InstallRandomPart(PartType.SteeringWheel);

        applied = true;

        Debug.Log(
            "Bot " + gameObject.name +
            ": combinación visual asignada.",
            this
        );
    }

    private void InstallRandomPart(PartType type)
    {
        List<KartPart> parts = new List<KartPart>();

        foreach (KartPart part in catalog.GetParts(type))
        {
            if (part != null)
                parts.Add(part);
        }

        if (parts.Count == 0)
        {
            Debug.LogWarning(
                "No hay piezas disponibles para " + type,
                this
            );
            return;
        }

        KartPart selected = parts[Random.Range(0, parts.Count)];

        configuration.InstallPart(selected);

        Debug.Log(
            gameObject.name + " | " + type +
            " | Pieza elegida: " + selected.name,
            this
        );
    }
}