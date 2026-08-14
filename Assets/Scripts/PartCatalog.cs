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

    public IReadOnlyList<KartPart> Parts => parts;

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
        if (string.IsNullOrEmpty(partID))
            return null;

        foreach (KartPart part in parts)
        {
            if (part != null && part.partID == partID)
            {
                return part;
            }
        }

        return null;
    }
}