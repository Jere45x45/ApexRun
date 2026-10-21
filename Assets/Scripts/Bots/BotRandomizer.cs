using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Piezas de los bots. Todos corren con las mismas piezas que cuentan para
/// manejar (motor, ruedas y kit aero), así los niveles se diferencian solo
/// por cómo manejan. El volante es solo estético y sale al azar. El chasis
/// es fijo, como en la carrera.
/// Las piezas se ponen en Start, o antes si alguien llama a RandomizeKart
/// (el spawner de la carrera lo hace antes de que el bot aparezca en red,
/// para que los demás lo vean con las mismas piezas).
/// </summary>
public class BotRandomizer : MonoBehaviour
{
    [Header("Catalog")]
    [SerializeField] private PartCatalog catalog;

    [Header("Kart")]
    [SerializeField] private KartConfigurationController configuration;

    [Header("Piezas de todos los bots (partID)")]
    [Tooltip("Motor eléctrico: llega a 120 km/h, la velocidad máxima de los bots.")]
    [SerializeField] private string engineId = "engine_electric";
    [SerializeField] private string wheelsId = "wheels_slick";
    [SerializeField] private string aeroKitId = "kitaero_basic";

    [Tooltip("El volante no cambia el manejo: sale al azar.")]
    [SerializeField] private bool randomSteeringWheel = true;

    private bool applied;

    private void Reset()
    {
        configuration = GetComponentInChildren<KartConfigurationController>();
    }

    private void Start()
    {
        if (!applied)
            RandomizeKart();
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

        Install(engineId);
        Install(wheelsId);
        Install(aeroKitId);

        if (randomSteeringWheel)
        {
            List<KartPart> wheels = GetParts(PartType.SteeringWheel);

            if (wheels.Count > 0)
                configuration.InstallPart(wheels[Random.Range(0, wheels.Count)]);
        }

        applied = true;
    }

    private void Install(string partId)
    {
        if (string.IsNullOrEmpty(partId))
            return;

        KartPart part = catalog.GetPartByID(partId);

        if (part == null)
        {
            Debug.LogWarning($"BotRandomizer: el catálogo no tiene la pieza '{partId}'.", this);
            return;
        }

        configuration.InstallPart(part);
    }

    private List<KartPart> GetParts(PartType type)
    {
        List<KartPart> result = new List<KartPart>();

        foreach (KartPart part in catalog.GetParts(type))
        {
            if (part != null)
                result.Add(part);
        }

        return result;
    }
}
