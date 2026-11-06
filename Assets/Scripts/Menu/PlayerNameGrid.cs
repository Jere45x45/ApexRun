using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Casilleros con los nombres de los jugadores. Se usa en la sala del Inicio
/// y arriba del Taller en red.
/// - Cada casillero es una imagen con un texto adentro (su primer TMP_Text).
/// - La imagen depende del estado: normal, listo (verde), no listo (rojo) o
///   vacío (apagado, para los lugares libres).
/// </summary>
public class PlayerNameGrid : MonoBehaviour
{
    public enum SlotState
    {
        Empty,
        Normal,
        Ready,
        NotReady
    }

    [Tooltip("Casilleros en orden: de izquierda a derecha y de arriba abajo.")]
    [SerializeField] private Image[] slots = new Image[0];

    [Header("Imágenes")]
    [SerializeField] private Sprite normalSprite;
    [SerializeField] private Sprite readySprite;
    [SerializeField] private Sprite notReadySprite;
    [SerializeField] private Sprite emptySprite;

    [Header("Textos")]
    [SerializeField] private Color textColor = Color.white;
    [SerializeField] private Color emptyTextColor = new Color(1f, 1f, 1f, 0.35f);

    [Tooltip("Texto de los lugares libres (puede quedar vacío).")]
    [SerializeField] private string emptyText = "";

    public int Capacity => slots.Length;

    /// <summary>
    /// Muestra los nombres. states puede ser null (todos normales).
    /// visibleSlots: cuántos casilleros se ven en total (los que sobran
    /// después de los nombres se ven vacíos; los de más allá, ocultos).
    /// </summary>
    public void Show(IList<string> names, IList<SlotState> states, int visibleSlots)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            Image slot = slots[i];

            if (slot == null)
                continue;

            bool used = names != null && i < names.Count;
            bool visible = used || i < visibleSlots;

            slot.gameObject.SetActive(visible);

            if (!visible)
                continue;

            SlotState state = SlotState.Empty;

            if (used)
                state = states != null && i < states.Count ? states[i] : SlotState.Normal;

            Sprite sprite = SpriteFor(state);

            if (sprite != null)
                slot.sprite = sprite;

            TMP_Text label = slot.GetComponentInChildren<TMP_Text>(true);

            if (label != null)
            {
                // Los nombres los escribe cada jugador: sin etiquetas de formato.
                label.richText = false;
                label.text = used ? names[i] : emptyText;
                label.color = used ? textColor : emptyTextColor;
            }
        }
    }

    private Sprite SpriteFor(SlotState state)
    {
        switch (state)
        {
            case SlotState.Ready: return readySprite != null ? readySprite : normalSprite;
            case SlotState.NotReady: return notReadySprite != null ? notReadySprite : normalSprite;
            case SlotState.Empty: return emptySprite != null ? emptySprite : normalSprite;
            default: return normalSprite;
        }
    }
}
