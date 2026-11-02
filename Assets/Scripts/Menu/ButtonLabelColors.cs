using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Cambia el color del texto de un botón según su estado, para los botones
/// cuyo sprite cambia de color (gris → rojo): texto negro sobre el gris y
/// blanco sobre el rojo (con el mouse encima, seleccionado o apretado).
/// Va en el mismo objeto que el Button.
///
/// Con fadeWhenDisabled, si el botón no es interactuable se ve deshabilitado:
/// el sprite y el texto quedan semitransparentes (disabledAlpha).
/// </summary>
[RequireComponent(typeof(Selectable))]
public class ButtonLabelColors : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private Color normalColor = Color.black;
    [SerializeField] private Color activeColor = Color.white;

    [Header("Deshabilitado")]
    [Tooltip("Si el botón no es interactuable, el sprite y el texto se ven apagados.")]
    [SerializeField] private bool fadeWhenDisabled;
    [SerializeField, Range(0f, 1f)] private float disabledAlpha = 0.4f;

    private Graphic targetGraphic;
    private Color graphicColor = Color.white;

    private Selectable selectable;
    private bool hovered;
    private bool pressed;

    private void Awake()
    {
        selectable = GetComponent<Selectable>();

        if (label == null)
            label = GetComponentInChildren<TMP_Text>(true);

        targetGraphic = selectable != null ? selectable.targetGraphic : null;

        if (targetGraphic != null)
            graphicColor = targetGraphic.color;
    }

    private void OnDisable()
    {
        hovered = false;
        pressed = false;
        Apply();
    }

    public void OnPointerEnter(PointerEventData eventData) { hovered = true; }
    public void OnPointerExit(PointerEventData eventData) { hovered = false; }
    public void OnPointerDown(PointerEventData eventData) { pressed = true; }
    public void OnPointerUp(PointerEventData eventData) { pressed = false; }

    private void LateUpdate()
    {
        Apply();
    }

    private void Apply()
    {
        bool interactable = selectable == null || selectable.IsInteractable();
        bool faded = fadeWhenDisabled && !interactable;

        if (targetGraphic != null && fadeWhenDisabled)
        {
            Color graphic = graphicColor;

            if (faded)
                graphic.a *= disabledAlpha;

            if (targetGraphic.color != graphic)
                targetGraphic.color = graphic;
        }

        if (label == null)
            return;

        bool selected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
        bool active = interactable && (hovered || pressed || selected);
        Color color = active ? activeColor : normalColor;

        if (faded)
            color.a *= disabledAlpha;

        if (label.color != color)
            label.color = color;
    }
}
