using UnityEngine;

public abstract class KartPart : ScriptableObject
{
    [Header("Información")]
    public string partID;
    public string partName;

    [TextArea]
    public string description;

    [Header("Visual")]
    public Sprite icon;

    [Header("Modelo")]
    public GameObject modelPrefab;

    [Header("Rareza")]
    public PartRarity rarity = PartRarity.Common;

    public virtual void Apply(KartStats stats)
    {
    }
}