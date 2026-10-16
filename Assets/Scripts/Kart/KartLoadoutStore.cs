using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Las piezas que eligió el jugador en el Catálogo:
/// - Pasan del Catálogo a la carrera (en un jugador y en red: en la carrera
///   las instala NetworkKart en el kart propio).
/// - Quedan guardadas para la próxima vez que se abra el juego (PlayerPrefs).
/// Solo guarda lo que se elige en el Catálogo: el chasis es fijo.
/// </summary>
public static class KartLoadoutStore
{
    private static readonly PartType[] SelectableTypes =
    {
        PartType.Engine,
        PartType.Wheels,
        PartType.AeroKit,
        PartType.SteeringWheel
    };

    private const string KeyPrefix = "ApexRun.Pieza.";

    // En memoria: cada computadora (y cada jugador virtual del editor, que
    // comparten PlayerPrefs) tiene la suya. PlayerPrefs se lee solo la
    // primera vez.
    private static Dictionary<PartType, string> selection;

    /// <summary>Guarda las piezas instaladas en el kart del Catálogo.</summary>
    public static void Save(KartConfigurationController controller)
    {
        if (controller == null || controller.Configuration == null)
            return;

        Load();

        foreach (PartType type in SelectableTypes)
        {
            KartPart part = controller.GetInstalledPart(type);

            if (part == null || string.IsNullOrWhiteSpace(part.partID))
                continue;

            selection[type] = part.partID;
            PlayerPrefs.SetString(KeyPrefix + type, part.partID);
        }

        PlayerPrefs.Save();
    }

    /// <summary>
    /// Instala en el kart las piezas elegidas. Las que no se eligieron, o que
    /// ya no están en el catálogo, quedan como vienen.
    /// </summary>
    public static void Apply(KartConfigurationController controller, PartCatalog catalog)
    {
        if (controller == null || catalog == null || controller.Configuration == null)
            return;

        Load();

        foreach (PartType type in SelectableTypes)
        {
            if (!selection.TryGetValue(type, out string partID))
                continue;

            KartPart part = catalog.GetPartByID(partID);

            if (part == null || part.PartType != type)
                continue;

            if (controller.GetInstalledPart(type) == part)
                continue;

            controller.InstallPart(part);
        }
    }

    private static void Load()
    {
        if (selection != null)
            return;

        selection = new Dictionary<PartType, string>();

        foreach (PartType type in SelectableTypes)
        {
            string partID = PlayerPrefs.GetString(KeyPrefix + type, "");

            if (!string.IsNullOrWhiteSpace(partID))
                selection[type] = partID;
        }
    }
}
