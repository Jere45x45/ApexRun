using UnityEngine;

/// <summary>
/// Idioma del juego. Por ahora el juego está en español: la fila Idioma de
/// Configuración todavía no se puede cambiar.
/// Para un texto que cambia con el idioma: GameLanguage.Text("Un jugador", "Single Player").
/// Queda guardado en PlayerPrefs (ApexRun.Idioma), así el día que se pueda
/// elegir, solo hay que cambiar Current.
/// </summary>
public static class GameLanguage
{
    public enum Language
    {
        Spanish,
        English
    }

    private const string Key = "ApexRun.Idioma";

    public static Language Current
    {
        get => (Language)PlayerPrefs.GetInt(Key, (int)Language.Spanish);
        set => PlayerPrefs.SetInt(Key, (int)value);
    }

    /// <summary>El texto en el idioma actual.</summary>
    public static string Text(string spanish, string english)
    {
        return Current == Language.English ? english : spanish;
    }
}
