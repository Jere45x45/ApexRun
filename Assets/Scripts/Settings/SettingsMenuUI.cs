using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pantalla de Configuración (Panel_Settings de Inicio).
/// - Efectos de sonido y Música: barras de 0 a 100 %. Se aplican en el momento
///   y se guardan (GameSettings).
/// - Idioma: español o inglés, con &lt; y &gt; (GameLanguage). Se aplica en el
///   momento: los textos con LocalizedText cambian solos.
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

    private void Awake()
    {
        if (effectsSlider != null)
            effectsSlider.onValueChanged.AddListener(HandleEffectsChanged);

        if (musicSlider != null)
            musicSlider.onValueChanged.AddListener(HandleMusicChanged);

        // Hay dos idiomas: < y > hacen lo mismo.
        if (languagePrevious != null)
            languagePrevious.onClick.AddListener(ChangeLanguage);

        if (languageNext != null)
            languageNext.onClick.AddListener(ChangeLanguage);
    }

    private void OnEnable()
    {
        // Muestra lo guardado sin volver a disparar los cambios.
        Show(effectsSlider, effectsValue, GameSettings.EffectsVolume);
        Show(musicSlider, musicValue, GameSettings.MusicVolume);
        ShowLanguage();
    }

    private void OnDisable()
    {
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
