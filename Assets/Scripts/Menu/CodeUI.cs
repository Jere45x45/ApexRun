using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Piezas de interfaz armadas por código, con el estilo de las pantallas del
/// menú (fondo oscuro, filas grises, acento rojo). Las usan la tabla de la
/// contrarreloj (TimeTrialLeaderboardUI) y el panel de la cuenta (AccountPanelUI).
/// Las posiciones van en píxeles desde la esquina de arriba a la izquierda del padre (SetBox).
/// </summary>
public static class CodeUI
{
    public static readonly Color RowColor = new Color(0.16f, 0.16f, 0.18f, 1f);
    public static readonly Color BoxColor = new Color(0.11f, 0.11f, 0.13f, 0.96f);
    public static readonly Color PanelColor = new Color(0.09f, 0.09f, 0.11f, 1f);
    public static readonly Color AccentRed = new Color(0.86f, 0.1f, 0.12f, 1f);
    public static readonly Color MutedText = new Color(0.62f, 0.63f, 0.68f, 1f);
    public static readonly Color LightBlue = new Color(0.55f, 0.85f, 1f, 1f);
    public static readonly Color Warning = new Color(1f, 0.78f, 0.35f, 1f);

    public static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    /// <summary>Ubica un rectángulo desde la esquina de arriba a la izquierda del padre (px).</summary>
    public static void SetBox(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }

    /// <summary>Que ocupe todo el padre.</summary>
    public static void Fill(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    public static Image AddImage(RectTransform rect, Color color)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    public static TMP_Text NewText(string name, Transform parent, float size, TextAlignmentOptions alignment, TMP_FontAsset font)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);

        var text = go.GetComponent<TextMeshProUGUI>();

        if (font != null)
            text.font = font;

        text.fontSize = size;
        text.color = Color.white;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    public static Button NewButton(string name, RectTransform parent, string label, float x, float y, float width, float height,
                                   UnityEngine.Events.UnityAction action, TMP_FontAsset font)
    {
        RectTransform rect = NewRect(name, parent);
        SetBox(rect, x, y, width, height);
        Image image = AddImage(rect, RowColor);

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.3f, 1.3f, 1.35f, 1f);
        colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
        button.colors = colors;
        button.onClick.AddListener(action);

        TMP_Text text = NewText("Texto", rect, 10.5f, TextAlignmentOptions.Center, font);
        Fill(text.rectTransform);
        text.text = label;
        return button;
    }

    /// <summary>Habilita o no un botón (y apaga su texto).</summary>
    public static void SetUsable(Button button, bool usable)
    {
        if (button == null)
            return;

        button.interactable = usable;

        TMP_Text label = button.GetComponentInChildren<TMP_Text>();

        if (label != null)
            label.alpha = usable ? 1f : 0.35f;
    }

    /// <summary>
    /// Campo de texto (TMP_InputField necesita su área y sus textos). Con password,
    /// se ve con asteriscos y no deja copiar ni cortar (pegar sí).
    /// </summary>
    public static TMP_InputField NewInput(RectTransform parent, float x, float y, float width, float height, string hint,
                                          int characterLimit, bool password, UnityEngine.Events.UnityAction<string> onEndEdit,
                                          TMP_FontAsset font)
    {
        RectTransform rect = NewRect("Campo", parent);
        rect.gameObject.SetActive(false);
        SetBox(rect, x, y, width, height);
        Image background = AddImage(rect, RowColor);

        RectTransform area = NewRect("Area", rect);
        area.anchorMin = Vector2.zero;
        area.anchorMax = Vector2.one;
        area.offsetMin = new Vector2(7f, 1f);
        area.offsetMax = new Vector2(-7f, -1f);
        area.gameObject.AddComponent<RectMask2D>();

        TMP_Text text = NewText("Texto", area, 12f, TextAlignmentOptions.MidlineLeft, font);
        Fill(text.rectTransform);

        TMP_Text placeholder = NewText("Ayuda", area, 11f, TextAlignmentOptions.MidlineLeft, font);
        Fill(placeholder.rectTransform);
        placeholder.color = MutedText;
        placeholder.fontStyle = FontStyles.Italic;
        placeholder.text = hint;

        var input = rect.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = background;
        input.textViewport = area;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.characterLimit = characterLimit;
        input.lineType = TMP_InputField.LineType.SingleLine;

        if (password)
        {
            input.contentType = TMP_InputField.ContentType.Password;
            input.inputType = TMP_InputField.InputType.Password;
        }

        input.richText = false;
        input.caretColor = Color.white;
        input.customCaretColor = true;
        input.selectionColor = new Color(0.55f, 0.85f, 1f, 0.35f);

        ColorBlock colors = input.colors;
        colors.highlightedColor = new Color(1.2f, 1.2f, 1.25f, 1f);
        colors.selectedColor = new Color(1.3f, 1.3f, 1.4f, 1f);
        input.colors = colors;

        if (onEndEdit != null)
            input.onEndEdit.AddListener(onEndEdit);

        rect.gameObject.SetActive(true);
        return input;
    }
}
