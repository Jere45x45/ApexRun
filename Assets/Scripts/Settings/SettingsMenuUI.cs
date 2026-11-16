using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Pantalla de Configuración (Panel_Settings de Inicio).
/// - Efectos de sonido y Música: barras de 0 a 100 %. Se aplican en el momento
///   y se guardan (GameSettings).
/// - Idioma: español o inglés, con &lt; y &gt; (GameLanguage). Se aplica en el
///   momento: los textos con LocalizedText cambian solos.
/// - Ayudas de manejo (DrivingAssists): ABS, Tracción y Volante, cada una
///   Activado o Desactivado con &lt; y &gt;. Las filas se arman solas al abrir
///   la escena, copiando la fila de Idioma, en una segunda columna a la derecha
///   (las opciones de antes pasan a la columna izquierda). Así no hace falta
///   tocar la escena.
/// </summary>
public class SettingsMenuUI : MonoBehaviour
{
    [Header("Volumen")]
    [SerializeField] private Slider effectsSlider;
    [SerializeField] private TMP_Text effectsValue;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private TMP_Text musicValue;

    [Header("Idioma")]
    [SerializeField] private TMP_Text languageValue;
    [SerializeField] private Button languagePrevious;
    [SerializeField] private Button languageNext;

    [Header("Ayudas de manejo")]
    [Tooltip("Distancia (px del lienzo) del centro de cada columna al centro de la pantalla.")]
    [SerializeField, Min(0f)] private float columnOffset = 185f;

    private static readonly DrivingAssists.Assist[] AssistOrder =
    {
        DrivingAssists.Assist.Abs,
        DrivingAssists.Assist.TractionControl,
        DrivingAssists.Assist.Steering
    };

    private TMP_Text[] assistValues = new TMP_Text[0];
    private LocalizedText[] assistLabels = new LocalizedText[0];

    private void Awake()
    {
        if (effectsSlider != null)
            effectsSlider.onValueChanged.AddListener(HandleEffectsChanged);

        if (musicSlider != null)
            musicSlider.onValueChanged.AddListener(HandleMusicChanged);

        // Al soltar la barra, que no quede marcada (en rojo oscuro).
        DeselectOnRelease(effectsSlider);
        DeselectOnRelease(musicSlider);

        // Hay dos idiomas: < y > hacen lo mismo.
        if (languagePrevious != null)
            languagePrevious.onClick.AddListener(ChangeLanguage);

        if (languageNext != null)
            languageNext.onClick.AddListener(ChangeLanguage);

        BuildAssistRows();
    }

    private void OnEnable()
    {
        // Muestra lo guardado sin volver a disparar los cambios.
        Show(effectsSlider, effectsValue, GameSettings.EffectsVolume);
        Show(musicSlider, musicValue, GameSettings.MusicVolume);
        ShowLanguage();
        ShowAssists();

        GameLanguage.Changed += ShowAssists;
    }

    private void OnDisable()
    {
        GameLanguage.Changed -= ShowAssists;

        GameSettings.Save();
    }

    private void HandleEffectsChanged(float value)
    {
        GameSettings.EffectsVolume = value / 100f;
        ShowValue(effectsValue, value);
    }

    private void HandleMusicChanged(float value)
    {
        GameSettings.MusicVolume = value / 100f;
        ShowValue(musicValue, value);
    }

    private void ChangeLanguage()
    {
        GameLanguage.Next();
        ShowLanguage();
        ShowAssists();

        // Que la flecha no quede marcada (en rojo) después del clic.
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    // ------------------------------------------------------------------
    // Ayudas de manejo
    // ------------------------------------------------------------------

    /// <summary>
    /// Arma una fila por ayuda copiando la fila de Idioma. Las filas que ya
    /// estaban pasan a la columna izquierda; las ayudas van a la derecha, a la
    /// altura de las tres primeras.
    /// </summary>
    private void BuildAssistRows()
    {
        RectTransform languageRow = FindRow(languageValue);

        if (languageRow == null || languageRow.parent == null)
            return;

        Transform options = languageRow.parent;

        // Filas que ya había, ordenadas de arriba hacia abajo.
        var rows = new System.Collections.Generic.List<RectTransform>();

        foreach (Transform child in options)
        {
            if (child is RectTransform rect && child.gameObject.activeSelf)
                rows.Add(rect);
        }

        rows.Sort((a, b) => b.anchoredPosition.y.CompareTo(a.anchoredPosition.y));

        // Las de arriba, a la izquierda; las que quedan debajo de la última
        // ayuda (Créditos), centradas.
        for (int i = 0; i < rows.Count; i++)
        {
            float x = i < AssistOrder.Length ? rows[i].anchoredPosition.x - columnOffset : 0f;
            rows[i].anchoredPosition = new Vector2(x, rows[i].anchoredPosition.y);
        }

        assistValues = new TMP_Text[AssistOrder.Length];
        assistLabels = new LocalizedText[AssistOrder.Length];

        for (int i = 0; i < AssistOrder.Length; i++)
        {
            DrivingAssists.Assist assist = AssistOrder[i];

            RectTransform copy = Instantiate(languageRow, options);
            copy.name = "Fila_AYUDA_" + assist.ToString().ToUpperInvariant();

            float y = i < rows.Count ? rows[i].anchoredPosition.y : languageRow.anchoredPosition.y - 54f * i;
            copy.anchoredPosition = new Vector2(columnOffset, y);

            assistLabels[i] = copy.GetComponentInChildren<LocalizedText>(true);
            assistValues[i] = FindValue(copy);

            Button previous = FindButton(copy, languagePrevious);
            Button next = FindButton(copy, languageNext);

            // < y > hacen lo mismo: hay dos estados.
            if (previous != null)
                previous.onClick.AddListener(() => ToggleAssist(assist));

            if (next != null)
                next.onClick.AddListener(() => ToggleAssist(assist));
        }

        ShowAssists();
    }

    private void ToggleAssist(DrivingAssists.Assist assist)
    {
        DrivingAssists.Set(assist, !DrivingAssists.Get(assist));
        ShowAssists();

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void ShowAssists()
    {
        for (int i = 0; i < assistValues.Length && i < AssistOrder.Length; i++)
        {
            DrivingAssists.Assist assist = AssistOrder[i];

            if (assistLabels[i] != null)
                assistLabels[i].Set(SpanishLabel(assist), EnglishLabel(assist));

            if (assistValues[i] != null)
                assistValues[i].text = DrivingAssists.StateText(DrivingAssists.Get(assist));
        }
    }

    private static string SpanishLabel(DrivingAssists.Assist assist)
    {
        return assist == DrivingAssists.Assist.Abs ? "ABS"
            : assist == DrivingAssists.Assist.TractionControl ? "Tracción" : "Volante";
    }

    private static string EnglishLabel(DrivingAssists.Assist assist)
    {
        return assist == DrivingAssists.Assist.Abs ? "ABS"
            : assist == DrivingAssists.Assist.TractionControl ? "Traction" : "Steering";
    }

    /// <summary>La fila (hija directa de Opciones) que contiene ese texto.</summary>
    private static RectTransform FindRow(Component inside)
    {
        if (inside == null)
            return null;

        Transform t = inside.transform;

        while (t.parent != null && t.parent.parent != null && t.parent.GetComponent<SettingsMenuUI>() == null &&
               t.parent.parent.GetComponent<SettingsMenuUI>() == null)
        {
            t = t.parent;
        }

        return t as RectTransform;
    }

    /// <summary>En la copia, el texto que ocupa el lugar de languageValue.</summary>
    private TMP_Text FindValue(Transform copy)
    {
        string path = RelativePath(FindRow(languageValue), languageValue.transform);
        Transform found = path != null ? copy.Find(path) : null;
        return found != null ? found.GetComponent<TMP_Text>() : null;
    }

    /// <summary>En la copia, el botón que ocupa el lugar de original.</summary>
    private Button FindButton(Transform copy, Button original)
    {
        if (original == null)
            return null;

        string path = RelativePath(FindRow(languageValue), original.transform);
        Transform found = path != null ? copy.Find(path) : null;
        return found != null ? found.GetComponent<Button>() : null;
    }

    private static string RelativePath(Transform root, Transform target)
    {
        if (root == null || target == null)
            return null;

        string path = target.name;
        Transform t = target.parent;

        while (t != null && t != root)
        {
            path = t.name + "/" + path;
            t = t.parent;
        }

        return t == root ? path : null;
    }

    private static void DeselectOnRelease(Slider slider)
    {
        if (slider == null)
            return;

        EventTrigger trigger = slider.GetComponent<EventTrigger>();

        if (trigger == null)
            trigger = slider.gameObject.AddComponent<EventTrigger>();

        EventTrigger.Entry release = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        release.callback.AddListener(_ =>
        {
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == slider.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
        });
        trigger.triggers.Add(release);
    }

    private void ShowLanguage()
    {
        if (languageValue != null)
            languageValue.text = GameLanguage.NameOf(GameLanguage.Current);
    }

    private static void Show(Slider slider, TMP_Text label, float volume)
    {
        float percent = Mathf.Round(volume * 100f);

        if (slider != null)
            slider.SetValueWithoutNotify(percent);

        ShowValue(label, percent);
    }

    private static void ShowValue(TMP_Text label, float percent)
    {
        if (label != null)
            label.text = $"{Mathf.RoundToInt(percent)} %";
    }
}
