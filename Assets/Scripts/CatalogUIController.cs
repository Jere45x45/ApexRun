using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Panel de piezas del Catálogo: pestañas por categoría y la lista de piezas
/// de la categoría elegida. La pestaña activa y la pieza equipada se marcan.
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

    [Header("Pestañas")]
    [SerializeField]
    private CategoryTab[] tabs = new CategoryTab[0];

    [SerializeField] private Color tabNormal = new Color(0.16f, 0.18f, 0.23f, 1f);
    [SerializeField] private Color tabSelected = new Color(0.86f, 0.10f, 0.12f, 1f);
    [SerializeField] private Color tabTextNormal = new Color(0.70f, 0.73f, 0.80f, 1f);
    [SerializeField] private Color tabTextSelected = Color.white;

    private readonly List<CatalogPartItem> activeItems =
        new List<CatalogPartItem>();

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
        CurrentCategory = type;

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

        foreach (KartPart part in catalogController.GetParts(type))
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
    }

    private void RefreshTabs()
    {
        foreach (CategoryTab tab in tabs)
        {
            bool selected = tab.type == CurrentCategory;

            if (tab.background != null)
                tab.background.color = selected ? tabSelected : tabNormal;

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
                Destroy(item.gameObject);
            }
        }

        activeItems.Clear();
    }
}
