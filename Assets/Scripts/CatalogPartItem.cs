using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Fila de la lista de piezas del Catálogo: nombre, rareza (con su color),
/// qué cambia en el manejo (ventajas en verde, desventajas en rojo) y el botón
/// para equiparla. La pieza que el kart ya tiene puesta se marca como
/// EQUIPADA y su botón queda deshabilitado.
/// </summary>
public class CatalogPartItem : MonoBehaviour
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

    [Tooltip("Fondo de la fila: cambia cuando la pieza está equipada.")]
    [SerializeField] private Image background;

    [Tooltip("Franja de la izquierda, del color de la rareza.")]
    [SerializeField] private Image rarityAccent;

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

    public void Setup(
        KartPart part,
        CatalogController catalogController)
    {
        this.part = part;
        this.catalogController = catalogController;

        UpdateVisuals();
        ConfigureButton();
    }

    /// <summary>Marca la fila como equipada (o no).</summary>
    public void SetEquipped(bool equipped)
    {
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

    private void UpdateVisuals()
    {
        if (partName != null)
            partName.text = part.partName;

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
            string text = BuildEffects(part.effects);
            effects.text = text;
            effects.gameObject.SetActive(text.Length > 0);
        }

        // Hoy ninguna pieza tiene ícono: sin ícono no se deja el hueco.
        if (icon != null)
        {
            icon.sprite = part.icon;
            icon.gameObject.SetActive(part.icon != null);
        }
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
    /// Arma la línea de efectos: "+ Aceleración" en verde, "- Velocidad
    /// máxima" en rojo y lo que no tiene signo en gris.
    /// </summary>
    private string BuildEffects(string[] list)
    {
        if (list == null || list.Length == 0)
            return string.Empty;

        StringBuilder builder = new StringBuilder();

        foreach (string raw in list)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            string effect = raw.Trim();
            Color color = neutralEffect;

            if (effect[0] == '+')
            {
                color = advantage;
                effect = "+ " + effect.Substring(1).TrimStart();
            }
            else if (effect[0] == '-' || effect[0] == '–' || effect[0] == '−')
            {
                // Raya corta: la fuente del juego no tiene el signo menos (−).
                color = disadvantage;
                effect = "– " + effect.Substring(1).TrimStart();
            }

            if (builder.Length > 0)
                builder.Append(EffectSeparator);

            // El texto de la pieza no puede meter etiquetas propias.
            builder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(color)).Append("><noparse>")
                .Append(effect).Append("</noparse></color>");
        }

        return builder.ToString();
    }

    private static string GetRarityName(PartRarity value)
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
