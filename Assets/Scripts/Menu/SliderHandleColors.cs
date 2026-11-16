using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Color de la manija de una barra (Slider) según su estado:
/// - Normal: gris oscuro.
/// - Con el mouse encima de la manija (no de toda la barra) o seleccionada
///   con teclado/joystick: rojo oscuro.
/// - Apretada o arrastrando: rojo.
/// Va en el objeto del Slider, que tiene que tener Transition = None.
/// </summary>
[RequireComponent(typeof(Slider))]
public class SliderHandleColors : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private Graphic handle;
    [SerializeField] private Color normalColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    [SerializeField] private Color hoverColor = new Color(142f / 255f, 1f / 255f, 1f / 255f, 1f);
    [SerializeField] private Color pressedColor = new Color(1f, 0f, 0f, 1f);

    private Slider slider;
    private Canvas canvas;
    private bool pressed;

    private void Awake()
    {
        slider = GetComponent<Slider>();
        canvas = GetComponentInParent<Canvas>();

        if (handle == null && slider.handleRect != null)
            handle = slider.handleRect.GetComponent<Graphic>();
    }

    private void OnDisable()
    {
        pressed = false;

        if (handle != null)
            handle.color = normalColor;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            pressed = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            pressed = false;
    }

    private void LateUpdate()
    {
        if (handle == null)
            return;

        Color color = normalColor;

        if (slider.IsInteractable())
        {
            if (pressed)
                color = pressedColor;
            else if (IsMouseOverHandle() || IsSelected())
                color = hoverColor;
        }

        if (handle.color != color)
            handle.color = color;
    }

    private bool IsSelected()
    {
        return EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
    }

    private bool IsMouseOverHandle()
    {
        if (Mouse.current == null)
            return false;

        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.rootCanvas.worldCamera
            : null;

        return RectTransformUtility.RectangleContainsScreenPoint(
            handle.rectTransform, Mouse.current.position.ReadValue(), eventCamera);
    }
}
