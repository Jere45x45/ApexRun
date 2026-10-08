using UnityEngine;

public abstract class KartPart : ScriptableObject
{
    [Header("Información")]
    public string partID;
    public string partName;

    [TextArea]
    public string description;

    [Header("Catálogo")]
    [Tooltip("Qué cambia en el manejo, uno por línea. Empezá con + (ventaja, en verde) o - (desventaja, en rojo). Sin signo se ve en gris. Ej.: \"+ Aceleración\", \"- Velocidad máxima\".")]
    public string[] effects = new string[0];

    [Header("Visual")]
    public Sprite icon;

    [Header("Modelo")]
    public GameObject modelPrefab;

    [Header("Rareza")]
    public PartRarity rarity = PartRarity.Common;

    public abstract PartType PartType { get; }

    public abstract void Install(RuntimeKartConfiguration configuration);
    public abstract void Apply(KartStats stats);
}
