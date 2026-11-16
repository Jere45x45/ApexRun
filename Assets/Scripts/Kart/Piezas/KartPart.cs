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

    [Header("Inglés (si queda vacío, se usa el texto en español)")]
    public string partNameEn;

    [TextArea]
    public string descriptionEn;

    [Tooltip("Los mismos efectos en inglés, en el mismo orden y con el mismo signo.")]
    public string[] effectsEn = new string[0];

    /// <summary>Nombre en el idioma del juego.</summary>
    public string DisplayName =>
        GameLanguage.IsEnglish && !string.IsNullOrWhiteSpace(partNameEn) ? partNameEn : partName;

    /// <summary>Descripción en el idioma del juego.</summary>
    public string DisplayDescription =>
        GameLanguage.IsEnglish && !string.IsNullOrWhiteSpace(descriptionEn) ? descriptionEn : description;

    /// <summary>Efectos en el idioma del juego.</summary>
    public string[] DisplayEffects =>
        GameLanguage.IsEnglish && effectsEn != null && effectsEn.Length > 0 ? effectsEn : effects;

    public abstract PartType PartType { get; }

    public abstract void Install(RuntimeKartConfiguration configuration);
    public abstract void Apply(KartStats stats);
}
