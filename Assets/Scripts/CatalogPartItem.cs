using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CatalogPartItem : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text partName;
    [SerializeField] private TMP_Text rarity;
    [SerializeField] private Button selectButton;

    private KartPart part;
    private CatalogController catalogController;

    public void Setup(
        KartPart part,
        CatalogController catalogController)
    {
        this.part = part;
        this.catalogController = catalogController;

        UpdateVisuals();
        ConfigureButton();
    }

    private void UpdateVisuals()
    {
        if (partName != null)
            partName.text = part.partName;

        if (rarity != null)
            rarity.text = part.rarity.ToString();

        if (icon != null)
            icon.sprite = part.icon;
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
}