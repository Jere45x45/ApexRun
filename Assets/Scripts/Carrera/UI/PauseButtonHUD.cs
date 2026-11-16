using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Botón de pausa del HUD (arriba a la izquierda): abre el mismo menú que Esc.
/// Se ve solo cuando se puede pausar (cuenta regresiva o carrera) y con el
/// menú cerrado. Para poder hacerle clic, RaceCursorLock muestra el mouse
/// cuando se lo mueve.
/// </summary>
[RequireComponent(typeof(Button))]
public class PauseButtonHUD : MonoBehaviour
{
    [SerializeField] private RacePauseMenu pauseMenu;

    private Button button;
    private CanvasGroup group;

    private void Awake()
    {
        button = GetComponent<Button>();
        group = GetComponent<CanvasGroup>();

        if (group == null)
            group = gameObject.AddComponent<CanvasGroup>();

        button.onClick.AddListener(OnClick);
        SetVisible(false);
    }

    private void Update()
    {
        SetVisible(pauseMenu != null && !pauseMenu.IsOpen && pauseMenu.CanPause());
    }

    private void OnClick()
    {
        // No queda seleccionado (rojo) después del clic.
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        if (pauseMenu != null && !pauseMenu.IsOpen && pauseMenu.CanPause())
            pauseMenu.Pause();
    }

    private void SetVisible(bool visible)
    {
        group.alpha = visible ? 1f : 0f;
        group.interactable = visible;
        group.blocksRaycasts = visible;
    }
}
