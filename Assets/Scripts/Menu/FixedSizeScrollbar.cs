using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Barra de scroll con la manija de tamaño fijo (como en el diseño del
/// Taller). La ScrollRect normal achica o agranda la manija según cuánto
/// contenido hay; esta la deja siempre igual y sincroniza las dos a mano.
/// Va en el objeto de la Scrollbar. La ScrollRect NO tiene que tener esta
/// barra asignada en Vertical Scrollbar (si no, le vuelve a cambiar el tamaño).
/// </summary>
[RequireComponent(typeof(Scrollbar))]
public class FixedSizeScrollbar : MonoBehaviour
{
    [SerializeField] private ScrollRect scrollRect;

    [Tooltip("Alto de la manija, en fracción del alto de la barra.")]
    [SerializeField, Range(0.02f, 1f)] private float handleSize = 0.1f;

    private Scrollbar bar;
    private bool syncing;

    private void Awake()
    {
        bar = GetComponent<Scrollbar>();
        bar.onValueChanged.AddListener(OnBarMoved);

        if (scrollRect != null)
            scrollRect.onValueChanged.AddListener(OnListScrolled);
    }

    private void OnDestroy()
    {
        if (bar != null)
            bar.onValueChanged.RemoveListener(OnBarMoved);

        if (scrollRect != null)
            scrollRect.onValueChanged.RemoveListener(OnListScrolled);
    }

    private void LateUpdate()
    {
        bar.size = handleSize;

        if (scrollRect == null || scrollRect.content == null || scrollRect.viewport == null)
            return;

        // Si todo entra en la lista, la barra no se puede mover.
        bool canScroll = scrollRect.content.rect.height > scrollRect.viewport.rect.height + 0.5f;
        bar.interactable = canScroll;

        if (!canScroll && !Mathf.Approximately(bar.value, 1f))
            SetBar(1f);
    }

    private void OnListScrolled(Vector2 position)
    {
        SetBar(scrollRect.verticalNormalizedPosition);
    }

    private void OnBarMoved(float value)
    {
        if (syncing || scrollRect == null)
            return;

        syncing = true;
        scrollRect.verticalNormalizedPosition = value;
        syncing = false;
    }

    private void SetBar(float value)
    {
        if (syncing)
            return;

        syncing = true;
        bar.value = Mathf.Clamp01(value);
        syncing = false;
    }
}
