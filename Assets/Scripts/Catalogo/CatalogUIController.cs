using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Panel de piezas del Catálogo:
/// - Pestañas por categoría (Motor, Aero, Volante, Ruedas). La pestaña
///   activa usa su sprite de "estado actual".
/// - La lista de piezas de la categoría elegida, ordenadas por rareza: de
///   Común a Legendaria (dentro de la misma rareza, en el orden del catálogo).
/// - El panel Data: nombre, rareza, descripción y efectos de la pieza que
///   está debajo del mouse o, si no hay ninguna, de la que está puesta.
/// </summary>
public class CatalogUIController : MonoBehaviour
{
    /// <summary>Botón de una categoría (los clics ya están conectados en la escena).</summary>
    [Serializable]
    private struct CategoryTab
    {
        public PartType type;
        public Image background;
        public TMP_Text label;

        [Tooltip("Sprite de la pestaña cuando no está elegida. Vacío: se usan los colores.")]
        public Sprite normalSprite;

        [Tooltip("Sprite de la pestaña elegida (estado actual).")]
        public Sprite currentSprite;
    }

    [Header("Catalog")]
    [SerializeField]
    private CatalogController catalogController;

    [Header("UI")]
    [SerializeField]
    private Transform content;

    [SerializeField]
    private CatalogPartItem partItemPrefab;

    [SerializeField]
    private ScrollRect scrollRect;

    [Tooltip("Taller: mientras el kart va de una estación a otra (o la cámara la usa el Easter Egg de la radio) no se puede cambiar de pestaña.")]
    [SerializeField]
    private CatalogWorkshop workshop;

    [Header("Pestañas")]
    [SerializeField]
    private CategoryTab[] tabs = new CategoryTab[0];

    [SerializeField] private Color tabNormal = new Color(0.16f, 0.18f, 0.23f, 1f);
    [SerializeField] private Color tabSelected = new Color(0.86f, 0.10f, 0.12f, 1f);
    [SerializeField] private Color tabTextNormal = new Color(0.70f, 0.73f, 0.80f, 1f);
    [SerializeField] private Color tabTextSelected = Color.white;

    [Header("Panel Data")]
    [Tooltip("Nombre de la pieza.")]
    [SerializeField] private TMP_Text dataName;

    [Tooltip("Rareza, descripción y efectos.")]
    [SerializeField] private TMP_Text dataBody;

    [Tooltip("Colores de los efectos sobre el fondo claro del panel.")]
    [SerializeField] private Color dataAdvantage = new Color(0.10f, 0.50f, 0.20f, 1f);
    [SerializeField] private Color dataDisadvantage = new Color(0.72f, 0.07f, 0.09f, 1f);
    [SerializeField] private Color dataNeutral = new Color(0.30f, 0.32f, 0.38f, 1f);

    [Tooltip("Color del nombre de cada rareza en el panel: Común, Poco común, Rara, Épica, Legendaria.")]
    [SerializeField] private Color[] dataRarityColors =
    {
        new Color(0.36f, 0.37f, 0.40f),
        new Color(0.20f, 0.45f, 0.24f),
        new Color(0.20f, 0.36f, 0.52f),
        new Color(0.50f, 0.33f, 0.31f),
        new Color(0.44f, 0.42f, 0.18f)
    };

    private readonly List<CatalogPartItem> activeItems =
        new List<CatalogPartItem>();

    private readonly StringBuilder builder = new StringBuilder();

    // La pieza que está debajo del mouse (null: ninguna).
    private KartPart hoveredPart;

    public PartType CurrentCategory { get; private set; }

    private void OnEnable()
    {
        if (catalogController != null)
            catalogController.LoadoutChanged += RefreshEquipped;
    }

    private void OnDisable()
    {
        if (catalogController != null)
            catalogController.LoadoutChanged -= RefreshEquipped;
    }

    private void Start()
    {
        ShowCategory(PartType.Engine);
    }

    public void ShowCategory(PartType type)
    {
        // Mientras el kart viaja a una estación, las pestañas no responden:
        // hay que esperar a que llegue. Tampoco mientras dura el Easter Egg
        // de la radio.
        if (workshop != null && workshop.IsBusy)
            return;

        CurrentCategory = type;
        hoveredPart = null;

        ClearItems();
        RefreshTabs();

        if (catalogController == null)
        {
            Debug.LogError(
                "CatalogUIController no tiene un CatalogController asignado.",
                this
            );

            return;
        }

        if (content == null)
        {
            Debug.LogError(
                "CatalogUIController no tiene un Content asignado.",
                this
            );

            return;
        }

        if (partItemPrefab == null)
        {
            Debug.LogError(
                "CatalogUIController no tiene un Part Item Prefab asignado.",
                this
            );

            return;
        }

        // De la más común a la legendaria. OrderBy respeta el orden del
        // catálogo entre las piezas de la misma rareza.
        IEnumerable<KartPart> sorted = catalogController.GetParts(type)
            .OrderBy(part => (int)part.rarity);

        foreach (KartPart part in sorted)
        {
            CatalogPartItem item =
                Instantiate(
                    partItemPrefab,
                    content
                );

            item.Setup(
                part,
                catalogController
            );

            item.Hovered += OnItemHovered;

            activeItems.Add(item);
        }

        RefreshEquipped();

        // Cada categoría arranca mostrando el principio de la lista.
        if (scrollRect != null)
            scrollRect.verticalNormalizedPosition = 1f;
    }

    public void ShowEngines()
    {
        ShowCategory(PartType.Engine);
    }

    public void ShowChassis()
    {
        ShowCategory(PartType.Chassis);
    }

    public void ShowWheels()
    {
        ShowCategory(PartType.Wheels);
    }

    public void ShowAeroKits()
    {
        ShowCategory(PartType.AeroKit);
    }

    public void ShowSteeringWheels()
    {
        ShowCategory(PartType.SteeringWheel);
    }

    /// <summary>Marca la pieza que el kart tiene puesta en la categoría actual.</summary>
    private void RefreshEquipped()
    {
        if (catalogController == null)
            return;

        KartPart installed = catalogController.GetInstalledPart(CurrentCategory);

        foreach (CatalogPartItem item in activeItems)
        {
            if (item == null)
                continue;

            bool equipped = installed != null && item.Part != null && item.Part.partID == installed.partID;
            item.SetEquipped(equipped);
        }

        RefreshData();
    }

    private void OnItemHovered(CatalogPartItem item, bool inside)
    {
        if (inside)
            hoveredPart = item.Part;
        else if (hoveredPart == item.Part)
            hoveredPart = null;

        RefreshData();
    }

    /// <summary>Panel Data: la pieza debajo del mouse o, si no hay, la puesta.</summary>
    private void RefreshData()
    {
        if (dataName == null && dataBody == null)
            return;

        KartPart part = hoveredPart;

        if (part == null && catalogController != null)
            part = catalogController.GetInstalledPart(CurrentCategory);

        if (part == null)
        {
            if (dataName != null)
                dataName.text = "";

            if (dataBody != null)
                dataBody.text = "";

            return;
        }

        if (dataName != null)
        {
            dataName.text = "<noparse>" + part.partName + "</noparse>";
        }

        if (dataBody == null)
            return;

        builder.Clear();

        int rarityIndex = Mathf.Clamp((int)part.rarity, 0, dataRarityColors.Length - 1);
        Color rarityColor = dataRarityColors.Length > 0 ? dataRarityColors[rarityIndex] : dataNeutral;

        builder.Append("<b><color=#").Append(ColorUtility.ToHtmlStringRGB(rarityColor)).Append(">")
            .Append(CatalogPartItem.GetRarityName(part.rarity)).Append("</color></b>");

        if (!string.IsNullOrWhiteSpace(part.description))
        {
            builder.Append("\n<size=40%>\n</size><color=#").Append(ColorUtility.ToHtmlStringRGB(dataNeutral)).Append("><noparse>")
                .Append(part.description.Trim()).Append("</noparse></color>");
        }

        string effects = CatalogPartItem.BuildEffects(part.effects, dataAdvantage, dataDisadvantage, dataNeutral, "\n");

        if (effects.Length > 0)
            builder.Append("\n<size=40%>\n</size><b>").Append(effects).Append("</b>");

        dataBody.text = builder.ToString();
    }

    private void RefreshTabs()
    {
        foreach (CategoryTab tab in tabs)
        {
            bool selected = tab.type == CurrentCategory;

            if (tab.background != null)
            {
                if (tab.normalSprite != null)
                {
                    // Pestañas con sprites: la elegida usa el de estado actual.
                    tab.background.sprite = selected && tab.currentSprite != null ? tab.currentSprite : tab.normalSprite;
                    tab.background.color = Color.white;

                    // La pestaña elegida queda fija en su estado actual: no
                    // responde al mouse, así no se le pone encima el sprite de
                    // seleccionada.
                    Button button = tab.background.GetComponent<Button>();

                    if (button != null)
                        button.interactable = !selected;
                }
                else
                {
                    tab.background.color = selected ? tabSelected : tabNormal;
                }
            }

            if (tab.label != null)
                tab.label.color = selected ? tabTextSelected : tabTextNormal;
        }
    }

    private void ClearItems()
    {
        foreach (CatalogPartItem item in activeItems)
        {
            if (item != null)
            {
                item.Hovered -= OnItemHovered;
                Destroy(item.gameObject);
            }
        }

        activeItems.Clear();
    }
}
