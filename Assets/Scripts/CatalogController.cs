using System.Collections.Generic;
using UnityEngine;

public class CatalogController : MonoBehaviour
{
    [Header("Catalog")]
    [SerializeField]
    private PartCatalog catalog;

    [Header("Kart Configuration")]
    [SerializeField]
    private KartConfigurationController configurationController;

    public PartCatalog Catalog => catalog;

    public KartConfigurationController ConfigurationController =>
        configurationController;

    public IEnumerable<KartPart> GetParts(PartType type)
    {
        if (catalog == null)
        {
            Debug.LogError(
                "CatalogController no tiene un PartCatalog asignado.",
                this
            );

            yield break;
        }

        foreach (KartPart part in catalog.GetParts(type))
        {
            yield return part;
        }
    }

    public void SelectPart(KartPart part)
    {
        if (configurationController == null)
        {
            Debug.LogError(
                "CatalogController no tiene un KartConfigurationController asignado.",
                this
            );

            return;
        }

        if (part == null)
        {
            Debug.LogWarning(
                "Se intentó seleccionar una pieza nula.",
                this
            );

            return;
        }

        configurationController.InstallPart(part);
    }

    public KartPart GetInstalledPart(PartType type)
    {
        if (configurationController == null)
        {
            Debug.LogError(
                "CatalogController no tiene un KartConfigurationController asignado.",
                this
            );

            return null;
        }

        return configurationController.GetInstalledPart(type);
    }
}