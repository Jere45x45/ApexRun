using UnityEngine;

public class MenuButton : MonoBehaviour
{
    [SerializeField] private MenuManager menuManager;
    [SerializeField] private MenuPanel targetPanel;

    public void OpenPanel()
    {
        menuManager.Show(targetPanel);
    }
}