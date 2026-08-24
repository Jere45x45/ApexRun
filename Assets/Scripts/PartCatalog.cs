using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "New Part Catalog",
    menuName = "Kart/Catalog/Part Catalog"
)]
public class PartCatalog : ScriptableObject
{
    [SerializeField]
    private List<KartPart> parts = new List<KartPart>();

    private Dictionary<string, KartPart> partsByID;

    public IReadOnlyList<KartPart> Parts => parts;

    private void OnEnable()
    {
        RebuildIndex();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        RebuildIndex();
        ValidateCatalog();
    }
#endif

    public IEnumerable<KartPart> GetParts(PartType type)
    {
        foreach (KartPart part in parts)
        {
            if (part != null && part.PartType == type)
            {
                yield return part;
            }
        }
    }

    public KartPart GetPartByID(string partID)
    {
        if (string.IsNullOrWhiteSpace(partID))
            return null;

        if (partsByID == null)
            RebuildIndex();

        partsByID.TryGetValue(partID, out KartPart part);

        return part;
    }

    public bool ContainsPart(KartPart part)
    {
        if (part == null)
            return false;

        return parts.Contains(part);
    }

    private void RebuildIndex()
    {
        if (partsByID == null)
        {
            partsByID = new Dictionary<string, KartPart>();
        }
        else
        {
            partsByID.Clear();
        }

        foreach (KartPart part in parts)
        {
            if (part == null)
                continue;

            if (string.IsNullOrWhiteSpace(part.partID))
                continue;

            if (!partsByID.ContainsKey(part.partID))
            {
                partsByID.Add(part.partID, part);
            }
        }
    }

#if UNITY_EDITOR
    private void ValidateCatalog()
    {
        HashSet<string> usedIDs = new HashSet<string>();

        foreach (KartPart part in parts)
        {
            if (part == null)
            {
                Debug.LogWarning(
                    $"El catálogo '{name}' contiene una referencia nula.",
                    this
                );

                continue;
            }

            if (string.IsNullOrWhiteSpace(part.partID))
            {
                Debug.LogWarning(
                    $"La pieza '{part.name}' no tiene un partID válido.",
                    part
                );

                continue;
            }

            if (!usedIDs.Add(part.partID))
            {
                Debug.LogError(
                    $"El partID '{part.partID}' está duplicado en el catálogo '{name}'.",
                    this
                );
            }
        }
    }
#endif
}