using System.Collections.Generic;
using UnityEngine;

public class MenuManager : MonoBehaviour
{
    [SerializeField]
    private List<UIPanel> panels = new List<UIPanel>();

    [SerializeField]
    private MenuPanel startPanel = MenuPanel.Main;

    private void Start()
    {
        Show(startPanel);
    }

    public void Show(MenuPanel panelType)
    {
        foreach (UIPanel panel in panels)
        {
            bool active = panel.type == panelType;
            panel.panel.SetActive(active);
        }
    }
}