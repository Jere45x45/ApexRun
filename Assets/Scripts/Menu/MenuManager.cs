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

    /// <summary>Cierra el juego (botón Exit). En el editor, sale de Play.</summary>
    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
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