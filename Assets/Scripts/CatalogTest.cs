using UnityEngine;

public class CatalogTest : MonoBehaviour
{
    [SerializeField]
    private PartCatalog catalog;

    [SerializeField]
    private KartConfigurationController configurationController;

    private void Start()
    {
        KartPart selectedPart = catalog.GetPartByID("engine_sport");

        if (selectedPart == null)
        {
            Debug.LogError("No se encontró la pieza engine_sport.");
            return;
        }

        Debug.Log(
            $"Pieza seleccionada: {selectedPart.partID} - {selectedPart.partName}"
        );

        configurationController.InstallPart(selectedPart);

        KartPart installedPart =
            configurationController.GetInstalledPart(PartType.Engine);

        Debug.Log(
            $"Pieza instalada: {installedPart.partID} - {installedPart.partName}"
        );
    }
}