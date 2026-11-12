using TMPro;
using UnityEngine;

/// <summary>
/// Texto fijo con versión en español y en inglés. Va en el mismo objeto que
/// el TextMeshPro: al activarse y cada vez que cambia el idioma
/// (GameLanguage), pone el texto que corresponde.
/// Para textos que arma el código no hace falta: el código usa
/// GameLanguage.Text(...).
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class LocalizedText : MonoBehaviour
{
    [TextArea(1, 6)]
    [SerializeField] private string spanish;

    [TextArea(1, 6)]
    [SerializeField] private string english;

    private TMP_Text text;

    public string Spanish => spanish;
    public string English => english;

    /// <summary>Cambia los dos textos desde código (y muestra el que corresponde).</summary>
    public void Set(string spanishText, string englishText)
    {
        spanish = spanishText;
        english = englishText;
        Apply();
    }

    private void OnEnable()
    {
        GameLanguage.Changed += Apply;
        Apply();
    }

    private void OnDisable()
    {
        GameLanguage.Changed -= Apply;
    }

    private void Apply()
    {
        if (text == null)
            text = GetComponent<TMP_Text>();

        string value = GameLanguage.IsEnglish ? english : spanish;

        // Si falta la traducción, queda el otro idioma.
        if (string.IsNullOrEmpty(value))
            value = GameLanguage.IsEnglish ? spanish : english;

        if (text != null && value != null && text.text != value)
            text.text = value;
    }
}
