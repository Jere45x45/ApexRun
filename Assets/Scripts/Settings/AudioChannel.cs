using UnityEngine;

/// <summary>
/// Dice a qué canal del volumen va una fuente de audio: Efectos o Música.
/// - Las fuentes sin este componente van solas a Efectos (GameAudioRouter).
/// - La música del juego lleva este componente en Música, así la controla el
///   volumen de la música de Configuración.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class AudioChannel : MonoBehaviour
{
    public enum Channel
    {
        Effects,
        Music
    }

    [SerializeField] private Channel channel = Channel.Music;

    public Channel Value => channel;

    private void OnEnable()
    {
        Assign(GetComponent<AudioSource>(), channel);
    }

    /// <summary>Manda la fuente al grupo del mezclador de ese canal.</summary>
    public static void Assign(AudioSource source, Channel channel)
    {
        if (source == null)
            return;

        source.outputAudioMixerGroup = GameSettings.GetGroup(channel);
    }
}
