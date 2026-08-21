using System.Collections.Generic;
using UnityEngine;

public class CatalogUIController : MonoBehaviour
{
    [Header("Catalog")]
    [SerializeField]
    private CatalogController catalogController;

    [Header("UI")]
    [SerializeField]
    private Transform content;

    [SerializeField]
    private CatalogPartItem partItemPrefab;

    private readonly List<CatalogPartItem> activeItems =
        new List<CatalogPartItem>();

    private void Start()
    {
        ShowCategory(PartType.Engine);
    }

    public void ShowCategory(PartType type)
    {
        ClearItems();

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
    }

    private void ClearItems()
    {
        foreach (CatalogPartItem item in activeItems)
        {
            if (item != null)
                Destroy(item.gameObject);
        }

        activeItems.Clear();
    }
}