using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Una pieza en la lista del Catálogo. Hay dos formas de mostrarla:
/// - Tarjeta (el taller nuevo): si tiene los sprites de rareza cargados, el
///   fondo es el de su rareza (Común, Poco común, Rara, Épica o Legendaria),
///   adentro va el ícono de la pieza (KartPart.icon); sin ícono, el nombre. Se
///   equipa tocando la tarjeta; la que está puesta queda con el fondo de seleccionada.
/// - Fila (la lista vieja): nombre, rareza, efectos y botón EQUIPAR.
/// Al pasar el mouse por encima avisa (Hovered) para que el panel Data
/// muestre esa pieza.
/// </summary>
public class CatalogPartItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("UI")]
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text partName;
    [SerializeField] private TMP_Text rarity;
    [SerializeField] private Button selectButton;

    [Tooltip("Línea con los efectos de la pieza (KartPart.effects).")]
    [SerializeField] private TMP_Text effects;

    [Tooltip("Texto del botón: EQUIPAR o EQUIPADA.")]
    [SerializeField] private TMP_Text buttonLabel;

    [Tooltip("Fondo de la fila o de la tarjeta.")]
    [SerializeField] private Image background;

    [Tooltip("Franja de la izquierda, del color de la rareza.")]
    [SerializeField] private Image rarityAccent;

    [Header("Tarjeta")]
    [Tooltip("Fondo de la tarjeta según la rareza, en este orden: Común, Poco común, Rara, Épica, Legendaria. Si está vacío, la pieza se muestra como fila.")]
    [SerializeField] private Sprite[] raritySprites = new Sprite[0];

    [Tooltip("Fondo de la tarjeta de la pieza puesta (el de seleccionada). Vacío: queda el de la rareza.")]
    [SerializeField] private Sprite equippedSprite;

    [Tooltip("Opcional: algo que se muestra solo en la pieza puesta.")]
    [SerializeField] private GameObject equippedMark;

    [Tooltip("Color del nombre en la tarjeta.")]
    [SerializeField] private Color cardNameColor = new Color(0.11f, 0.11f, 0.13f, 1f);

    [Tooltip("Color del nombre en la tarjeta de la pieza puesta.")]
    [SerializeField] private Color equippedNameColor = Color.white;

    [Tooltip("Mostrar el nombre también cuando la pieza tiene ícono.")]
    [SerializeField] private bool showNameWithIcon;

    [Header("Colores")]
    [SerializeField] private Color normalBackground = new Color(0.14f, 0.15f, 0.19f, 1f);
    [SerializeField] private Color equippedBackground = new Color(0.17f, 0.25f, 0.21f, 1f);
    [SerializeField] private Color equipButton = new Color(0.26f, 0.28f, 0.35f, 1f);
    [SerializeField] private Color equippedButton = new Color(0.12f, 0.62f, 0.30f, 1f);
    [SerializeField] private Color advantage = new Color(0.36f, 0.80f, 0.42f, 1f);
    [SerializeField] private Color disadvantage = new Color(1.00f, 0.42f, 0.40f, 1f);
    [SerializeField] private Color neutralEffect = new Color(0.60f, 0.64f, 0.70f, 1f);

    // Separación entre un efecto y el siguiente.
    private const string EffectSeparator = "    ";

    private KartPart part;
    private CatalogController catalogController;

    public KartPart Part => part;

    /// <summary>El mouse entró (true) o salió (false) de la pieza.</summary>
    public event Action<CatalogPartItem, bool> Hovered;

    /// <summary>Tiene los sprites de rareza: se muestra como tarjeta.</summary>
    private bool IsCard => raritySprites != null && raritySprites.Length > 0;

    public void Setup(
        KartPart part,
        CatalogController catalogController)
    {
        this.part = part;
        this.catalogController = catalogController;

        UpdateVisuals();
        ConfigureButton();
    }

    /// <summary>Marca la pieza como equipada (o no).</summary>
    public void SetEquipped(bool equipped)
    {
        if (IsCard)
        {
            // La pieza puesta queda con el fondo de seleccionada y no se
            // puede volver a tocar (deshabilitado, el botón muestra el fondo
            // que tenga la tarjeta).
            if (background != null)
                background.sprite = equipped && equippedSprite != null ? equippedSprite : RaritySprite();

            if (selectButton != null)
                selectButton.interactable = !equipped;

            if (equippedMark != null)
                equippedMark.SetActive(equipped);

            // Sobre el fondo rojo el nombre va en blanco.
            if (partName != null)
                partName.color = equipped ? equippedNameColor : cardNameColor;

            return;
        }

        if (background != null)
            background.color = equipped ? equippedBackground : normalBackground;

        if (selectButton != null)
        {
            selectButton.interactable = !equipped;

            // El color del botón es el de su Image: con la pieza equipada el
            // botón queda verde (y no gris, que es como se ve deshabilitado).
            Image buttonImage = selectButton.targetGraphic as Image;

            if (buttonImage != null)
                buttonImage.color = equipped ? equippedButton : equipButton;

            ColorBlock colors = selectButton.colors;
            colors.disabledColor = Color.white;
            selectButton.colors = colors;
        }

        if (buttonLabel != null)
            buttonLabel.text = equipped ? "EQUIPADA" : "EQUIPAR";
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        Hovered?.Invoke(this, true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        Hovered?.Invoke(this, false);
    }

    private void UpdateVisuals()
    {
        if (partName != null)
            partName.text = part.partName;

        if (IsCard)
        {
            UpdateCard();
            return;
        }

        Color rarityColor = GetRarityColor(part.rarity);

        if (rarity != null)
        {
            rarity.text = GetRarityName(part.rarity);
            rarity.color = rarityColor;
        }

        if (rarityAccent != null)
            rarityAccent.color = rarityColor;

        if (effects != null)
        {
            string text = BuildEffects(part.effects, advantage, disadvantage, neutralEffect, EffectSeparator);
            effects.text = text;
            effects.gameObject.SetActive(text.Length > 0);
        }

        // Sin ícono no se deja el hueco.
        if (icon != null)
        {
            icon.sprite = part.icon;
            icon.gameObject.SetActive(part.icon != null);
        }
    }

    /// <summary>
    /// Tarjeta: fondo de la rareza, ícono arriba y nombre abajo. Si la pieza
    /// todavía no tiene ícono, el nombre ocupa toda la tarjeta.
    /// </summary>
    private void UpdateCard()
    {
        if (background != null)
            background.sprite = RaritySprite();

        bool hasIcon = part.icon != null;

        if (icon != null)
        {
            icon.sprite = part.icon;
            icon.preserveAspect = true;
            icon.gameObject.SetActive(hasIcon);
        }

        // Con ícono, la tarjeta muestra solo el ícono (el nombre está en el
        // panel Data). Sin ícono, el nombre va centrado.
        if (partName != null)
            partName.gameObject.SetActive(!hasIcon || showNameWithIcon);

        if (equippedMark != null)
            equippedMark.SetActive(false);
    }

    /// <summary>Fondo de la rareza de la pieza (si falta, el que tenga la tarjeta).</summary>
    private Sprite RaritySprite()
    {
        int index = Mathf.Clamp((int)part.rarity, 0, raritySprites.Length - 1);
        Sprite sprite = raritySprites[index];

        return sprite != null ? sprite : background.sprite;
    }

    private void ConfigureButton()
    {
        if (selectButton == null)
            return;

        selectButton.onClick.RemoveAllListeners();

        selectButton.onClick.AddListener(
            SelectPart
        );
    }

    private void SelectPart()
    {
        if (catalogController == null)
            return;

        if (part == null)
            return;

        catalogController.SelectPart(part);
    }

    /// <summary>
    /// Arma los efectos: "+ Aceleración" con el color de ventaja, "- Velocidad
    /// máxima" con el de desventaja y lo que no tiene signo con el neutro.
    /// </summary>
    public static string BuildEffects(string[] list, Color good, Color bad, Color neutral, string separator)
    {
        if (list == null || list.Length == 0)
            return string.Empty;

        StringBuilder builder = new StringBuilder();

        foreach (string raw in list)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            string effect = raw.Trim();
            Color color = neutral;

            if (effect[0] == '+')
            {
                color = good;
                effect = "+ " + effect.Substring(1).TrimStart();
            }
            else if (effect[0] == '-' || effect[0] == '–' || effect[0] == '−')
            {
                // Raya corta: la fuente del juego no tiene el signo menos (−).
                color = bad;
                effect = "– " + effect.Substring(1).TrimStart();
            }

            if (builder.Length > 0)
                builder.Append(separator);

            // El texto de la pieza no puede meter etiquetas propias.
            builder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(color)).Append("><noparse>")
                .Append(effect).Append("</noparse></color>");
        }

        return builder.ToString();
    }

    public static string GetRarityName(PartRarity value)
    {
        switch (value)
        {
            case PartRarity.Uncommon: return "POCO COMÚN";
            case PartRarity.Rare: return "RARA";
            case PartRarity.Epic: return "ÉPICA";
            case PartRarity.Legendary: return "LEGENDARIA";
            default: return "COMÚN";
        }
    }

    private static Color GetRarityColor(PartRarity value)
    {
        switch (value)
        {
            case PartRarity.Uncommon: return new Color(0.36f, 0.80f, 0.42f);
            case PartRarity.Rare: return new Color(0.30f, 0.60f, 1.00f);
            case PartRarity.Epic: return new Color(0.70f, 0.42f, 1.00f);
            case PartRarity.Legendary: return new Color(1.00f, 0.76f, 0.24f);
            default: return new Color(0.60f, 0.64f, 0.70f);
        }
    }
}
