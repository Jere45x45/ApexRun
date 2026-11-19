using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Sonido de los botones: todos los Button del juego (los de las escenas y los
/// que se arman por código) suenan al tocarlos, con el volumen de Efectos.
/// - Se crea solo al arrancar (no hace falta ponerlo en ninguna escena).
/// - Revisa al cargar cada escena y cada medio segundo, por los botones que
///   aparecen después (paneles armados por código, filas de las tablas).
/// - Suena solo si el botón se puede usar (onClick no se dispara si no).
/// - Sigue sonando con la pausa (AudioListener.pause) y al cambiar de escena.
/// - Hay cuatro sonidos (ButtonSoundKind), en Resources/Audio. Cuál toca cada
///   botón se decide al tocarlo, por su nombre y su texto (en los dos idiomas):
///   Volver/Cancelar/Cerrar, Empezar/Reiniciar, flechas y pestañas, o el normal.
///   Para cambiarlo en un botón: el componente ButtonSound.
/// </summary>
public class ButtonClickSound : MonoBehaviour
{
    private const float ScanInterval = 0.5f;

    private static ButtonClickSound instance;

    // Los nombres de botones que son selectores (flechas, + y −, pestañas, categorías del taller).
    private static readonly HashSet<string> SelectorNames = new HashSet<string>
    {
        "anterior", "siguiente", "menos", "mas", "más", "previous", "next", "minus", "plus",
        "seco", "lluvia", "pestana", "enginebutton", "wheelsbutton", "kitaerobutton", "steeringwheelbutton"
    };

    private static readonly HashSet<string> SelectorLabels = new HashSet<string>
    {
        "<", ">", "-", "+", "−", "‹", "›", "<<", ">>"
    };

    private static readonly string[] BackWords = { "volver", "back", "cancelar", "cancel", "cerrar", "close" };
    private static readonly string[] StartWords = { "empezar", "start", "reiniciar", "restart", "reintentar", "retry", "a correr" };

    private readonly HashSet<int> hooked = new HashSet<int>();
    private readonly Dictionary<ButtonSoundKind, AudioClip> clips = new Dictionary<ButtonSoundKind, AudioClip>();
    private readonly Dictionary<ButtonSoundKind, float> volumes = new Dictionary<ButtonSoundKind, float>
    {
        { ButtonSoundKind.Normal, 0.7f },
        { ButtonSoundKind.Volver, 0.7f },
        { ButtonSoundKind.Empezar, 0.8f },
        { ButtonSoundKind.Selector, 0.55f }
    };

    private AudioSource source;
    private float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Create()
    {
        GameObject host = new GameObject("ButtonClickSound");
        host.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(host);
        instance = host.AddComponent<ButtonClickSound>();
    }

    /// <summary>Toca uno de los sonidos (por ejemplo, para algo que no es un Button).</summary>
    public static void Play(ButtonSoundKind kind = ButtonSoundKind.Normal)
    {
        if (instance != null)
            instance.PlayKind(kind);
    }

    private void Awake()
    {
        clips[ButtonSoundKind.Normal] = Resources.Load<AudioClip>("Audio/BotonClic");
        clips[ButtonSoundKind.Volver] = Resources.Load<AudioClip>("Audio/BotonVolver");
        clips[ButtonSoundKind.Empezar] = Resources.Load<AudioClip>("Audio/BotonEmpezar");
        clips[ButtonSoundKind.Selector] = Resources.Load<AudioClip>("Audio/BotonSelector");

        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.ignoreListenerPause = true;

        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;

        if (instance == this)
            instance = null;
    }

    private void Start()
    {
        AudioChannel.Assign(source, AudioChannel.Channel.Effects);
        HookAll();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextScan)
            return;

        nextScan = Time.unscaledTime + ScanInterval;
        HookAll();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        HookAll();
    }

    /// <summary>Le agrega el sonido a los botones que todavía no lo tienen.</summary>
    private void HookAll()
    {
        Button[] buttons = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (Button button in buttons)
        {
            if (!hooked.Add(button.GetInstanceID()))
                continue;

            Button target = button;
            button.onClick.AddListener(() => PlayKind(Classify(target)));
        }
    }

    /// <summary>Qué sonido le toca a ese botón (al tocarlo: el texto puede cambiar con el idioma).</summary>
    private static ButtonSoundKind Classify(Button button)
    {
        if (button == null)
            return ButtonSoundKind.Normal;

        ButtonSound custom = button.GetComponent<ButtonSound>();

        if (custom != null && custom.Kind != ButtonSoundKind.Auto)
            return custom.Kind;

        string name = button.name.Replace("(Clone)", "").Trim().ToLowerInvariant();
        string text = LabelOf(button);

        if (SelectorNames.Contains(name) || SelectorLabels.Contains(text) || name.Contains("partcard"))
            return ButtonSoundKind.Selector;

        if (Matches(name, text, StartWords) || name.Contains("correr"))
            return ButtonSoundKind.Empezar;

        if (Matches(name, text, BackWords))
            return ButtonSoundKind.Volver;

        return ButtonSoundKind.Normal;
    }

    /// <summary>El nombre contiene alguna de las palabras, o el texto empieza con alguna.</summary>
    private static bool Matches(string name, string text, string[] words)
    {
        foreach (string word in words)
        {
            if (name.Contains(word.Replace(" ", "")) || text.StartsWith(word, System.StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string LabelOf(Button button)
    {
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);

        if (label != null)
            return Clean(label.text);

        Text legacy = button.GetComponentInChildren<Text>(true);
        return legacy != null ? Clean(legacy.text) : "";
    }

    private static string Clean(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        // Sin etiquetas de formato (<b>, <size=...>) ni "¡": "¡A CORRER!" → "a correr!".
        text = System.Text.RegularExpressions.Regex.Replace(text, "<[^<>]{2,}>", "");
        return text.Replace("¡", "").Trim().ToLowerInvariant();
    }

    private void PlayKind(ButtonSoundKind kind)
    {
        if (kind == ButtonSoundKind.Ninguno || source == null)
            return;

        if (kind == ButtonSoundKind.Auto)
            kind = ButtonSoundKind.Normal;

        if (clips.TryGetValue(kind, out AudioClip clip) && clip != null)
            source.PlayOneShot(clip, volumes[kind]);
    }
}
