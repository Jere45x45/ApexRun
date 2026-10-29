using System;
using UnityEngine;

/// <summary>
/// Idioma del juego: español o inglés. Se elige en Configuración (fila
/// Idioma) y queda guardado en PlayerPrefs (ApexRun.Idioma).
/// - Textos fijos de la escena: componente LocalizedText (texto en español
///   y en inglés), que se actualiza solo al cambiar el idioma.
/// - Textos que arma el código: GameLanguage.Text("en español", "in English").
/// - Al cambiar el idioma se avisa con Changed.
/// </summary>
public static class GameLanguage
{
    public enum Language
    {
        Spanish,
        English
    }

    private const string Key = "ApexRun.Idioma";

    /// <summary>Cambió el idioma.</summary>
    public static event Action Changed;

    private static bool loaded;
    private static Language current;

    public static Language Current
    {
        get
        {
            if (!loaded)
            {
                current = (Language)Mathf.Clamp(PlayerPrefs.GetInt(Key, (int)Language.Spanish), 0, 1);
                loaded = true;
            }

            return current;
        }
        set
        {
            if (loaded && current == value)
                return;

            current = value;
            loaded = true;
            PlayerPrefs.SetInt(Key, (int)value);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }

    public static bool IsEnglish => Current == Language.English;

    /// <summary>El texto en el idioma actual.</summary>
    public static string Text(string spanish, string english)
    {
        return IsEnglish ? english : spanish;
    }

    /// <summary>Nombre del idioma en ese mismo idioma (para la fila de Configuración).</summary>
    public static string NameOf(Language language)
    {
        return language == Language.English ? "ENGLISH" : "ESPAÑOL";
    }

    /// <summary>Pasa al otro idioma (hay dos).</summary>
    public static void Next()
    {
        Current = IsEnglish ? Language.Spanish : Language.English;
    }

#if UNITY_EDITOR
    // Con "Enter Play Mode" sin recargar el dominio, vuelve a leer lo guardado.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        loaded = false;
        Changed = null;
    }
#endif
}
