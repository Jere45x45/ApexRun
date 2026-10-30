using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cambia solamente la apariencia de cada bot.
/// No instala piezas en RuntimeKartConfiguration ni modifica sus estadísticas.
/// </summary>
public class BotRandomizer : MonoBehaviour
{
    [Header("Catálogo")]
    [SerializeField] private PartCatalog catalog;

    [Header("Referencias")]
    [SerializeField] private KartConfigurationController configuration;
    [SerializeField] private KartModelController modelController;

    private static readonly HashSet<string> usedCombinations =
        new HashSet<string>();

    private bool applied;

    private class VisualCombination
    {
        public KartPart engine;
        public KartPart wheels;
        public KartPart aero;
        public string key;
    }

    private void Awake()
    {
        if (configuration == null)
            configuration = GetComponent<KartConfigurationController>();

        if (modelController == null)
            modelController = GetComponentInChildren<KartModelController>();
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

        if (catalog == null || configuration == null || modelController == null)
        {
            Debug.LogError(
                "BotRandomizer: asigná Catalog, Configuration y Model Controller.",
                this
            );
            return;
        }

        if (configuration.Configuration == null)
        {
            Debug.LogError(
                "BotRandomizer: la configuración del kart todavía no está lista.",
                this
            );
            return;
        }

        List<KartPart> engines = GetVisualParts(PartType.Engine);
        List<KartPart> wheels = GetVisualParts(PartType.Wheels);
        List<KartPart> aeroKits = GetVisualParts(PartType.AeroKit);

        if (engines.Count == 0 || wheels.Count == 0 || aeroKits.Count == 0)
        {
            Debug.LogError(
                "BotRandomizer: el catálogo necesita motores, ruedas y kits aero " +
                "con modelPrefab asignado.",
                this
            );
            return;
        }

        List<VisualCombination> available = new List<VisualCombination>();

        foreach (KartPart engine in engines)
        {
            foreach (KartPart wheel in wheels)
            {
                foreach (KartPart aero in aeroKits)
                {
                    string key =
                        engine.partID + "|" +
                        wheel.partID + "|" +
                        aero.partID;

                    if (!usedCombinations.Contains(key))
                    {
                        available.Add(new VisualCombination
                        {
                            engine = engine,
                            wheels = wheel,
                            aero = aero,
                            key = key
                        });
                    }
                }
            }
        }

        if (available.Count == 0)
        {
            Debug.LogWarning(
                "BotRandomizer: se agotaron las combinaciones visuales únicas. " +
                "Agregá más modelos al catálogo para que todos los bots sean diferentes.",
                this
            );
            return;
        }

        VisualCombination chosen =
            available[Random.Range(0, available.Count)];

        usedCombinations.Add(chosen.key);

        modelController.SetVisualOverrides(
            chosen.engine,
            chosen.wheels,
            chosen.aero,
            configuration.Configuration
        );

        applied = true;

        Debug.Log(
            "Apariencia asignada a " + gameObject.name +
            " | Motor: " + chosen.engine.partName +
            " | Ruedas: " + chosen.wheels.partName +
            " | Aero: " + chosen.aero.partName,
            this
        );
    }

    private List<KartPart> GetVisualParts(PartType type)
    {
        List<KartPart> result = new List<KartPart>();

        foreach (KartPart part in catalog.GetParts(type))
        {
            if (part != null &&
                !string.IsNullOrWhiteSpace(part.partID) &&
                part.modelPrefab != null)
            {
                result.Add(part);
            }
        }

        return result;
    }
}