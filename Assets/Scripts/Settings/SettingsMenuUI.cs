using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pantalla de Configuración (Panel_Settings de Inicio).
/// - Efectos de sonido y Música: barras de 0 a 100 %. Se aplican en el momento
///   y se guardan (GameSettings).
/// - Controles e Idioma: todavía no se pueden cambiar; se ven apagados.
/// </summary>
public class SettingsMenuUI : MonoBehaviour
{
    [Header("Volumen")]
    [SerializeField] private Slider effectsSlider;
    [SerializeField] private TMP_Text effectsValue;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private TMP_Text musicValue;

    private void Awake()
    {
        if (effectsSlider != null)
            effectsSlider.onValueChanged.AddListener(HandleEffectsChanged);

        if (musicSlider != null)
            musicSlider.onValueChanged.AddListener(HandleMusicChanged);
    }

    private void OnEnable()
    {
        // Muestra lo guardado sin volver a disparar los cambios.
        Show(effectsSlider, effectsValue, GameSettings.EffectsVolume);
        Show(musicSlider, musicValue, GameSettings.MusicVolume);
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
