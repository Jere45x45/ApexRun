using System;
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

    /// <summary>Cambiaron las piezas instaladas en el kart del Catálogo.</summary>
    public event Action LoadoutChanged;

    private void Start()
    {
        // El kart del Catálogo arranca con lo que el jugador eligió la última vez.
        KartLoadoutStore.Apply(configurationController, catalog);

        LoadoutChanged?.Invoke();
    }

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
        if (catalog == null)
        {
            Debug.LogError(
                "CatalogController no tiene un PartCatalog asignado.",
                this
            );

            return;
        }

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

        if (!catalog.ContainsPart(part))
        {
            Debug.LogWarning(
                $"La pieza '{part.partID}' no pertenece a este catálogo.",
                this
            );

            return;
        }

        configurationController.InstallPart(part);

        // Lo elegido pasa a la carrera y queda guardado para la próxima vez.
        KartLoadoutStore.Save(configurationController);

        LoadoutChanged?.Invoke();
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
