using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Aplica el volumen guardado al arrancar el juego y manda al grupo Efectos del
/// mezclador toda fuente de audio que no tenga grupo. Así los motores, y
/// cualquier sonido que se agregue después, respetan el volumen de los efectos
/// sin tener que configurarlos uno por uno.
/// - Se crea solo al arrancar (no hace falta ponerlo en ninguna escena).
/// - Revisa al cargar cada escena y cada medio segundo, por los karts que
///   aparecen durante la carrera.
/// - Las fuentes que ya tienen grupo (por ejemplo, las que tienen AudioChannel
///   en Música) no se tocan.
/// </summary>
public class GameAudioRouter : MonoBehaviour
{
    private const float ScanInterval = 0.5f;

    private float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Create()
    {
        GameObject host = new GameObject("GameAudioRouter");
        host.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(host);
        host.AddComponent<GameAudioRouter>();
    }

    private void Awake()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void Start()
    {
        // El mezclador ignora SetFloat en Awake: se aplica recién acá.
        GameSettings.ApplyAudio();
        RouteAll();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextScan)
            return;

        nextScan = Time.unscaledTime + ScanInterval;
        RouteAll();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RouteAll();
    }

    private static void RouteAll()
    {
        AudioSource[] sources = FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (AudioSource source in sources)
        {
            if (source.outputAudioMixerGroup != null)
                continue;

            AudioChannel channel = source.GetComponent<AudioChannel>();
            AudioChannel.Assign(source, channel != null ? channel.Value : AudioChannel.Channel.Effects);
        }
    }
}
