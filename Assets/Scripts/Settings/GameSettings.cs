using System;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Opciones que elige el jugador en Configuración. Se guardan en PlayerPrefs
/// y el volumen se aplica al mezclador de audio (Resources/Audio/ApexRunMixer):
/// - Efectos: todo lo que suena en el juego (motores y demás). Toda fuente de
///   audio que no diga otra cosa va a este grupo (lo hace GameAudioRouter).
/// - Música: la música del juego. Sus fuentes llevan AudioChannel en Música.
/// Los volúmenes van de 0 a 1 y el mezclador los recibe en decibeles.
/// </summary>
public static class GameSettings
{
    public const string EffectsVolumeKey = "ApexRun.VolumenEfectos";
    public const string MusicVolumeKey = "ApexRun.VolumenMusica";

    public const float DefaultEffectsVolume = 1f;
    public const float DefaultMusicVolume = 0.8f;

    private const string MixerResource = "Audio/ApexRunMixer";
    private const string EffectsParameter = "VolumenEfectos";
    private const string MusicParameter = "VolumenMusica";
    private const string EffectsGroup = "Efectos";
    private const string MusicGroup = "Musica";

    private static AudioMixer mixer;
    private static bool mixerLoaded;

    /// <summary>Se llama cuando cambia alguna opción.</summary>
    public static event Action Changed;

    public static AudioMixer Mixer
    {
        get
        {
            if (!mixerLoaded)
            {
                mixer = Resources.Load<AudioMixer>(MixerResource);
                mixerLoaded = true;

                if (mixer == null)
                    Debug.LogWarning($"GameSettings: no se encontró el mezclador Resources/{MixerResource}. El volumen no se va a poder cambiar.");
            }

            return mixer;
        }
    }

    /// <summary>Volumen de los efectos de sonido, de 0 a 1.</summary>
    public static float EffectsVolume
    {
        get => PlayerPrefs.GetFloat(EffectsVolumeKey, DefaultEffectsVolume);
        set => SetVolume(EffectsVolumeKey, value);
    }

    /// <summary>Volumen de la música, de 0 a 1.</summary>
    public static float MusicVolume
    {
        get => PlayerPrefs.GetFloat(MusicVolumeKey, DefaultMusicVolume);
        set => SetVolume(MusicVolumeKey, value);
    }

    /// <summary>Lleva los volúmenes guardados al mezclador.</summary>
    public static void ApplyAudio()
    {
        AudioMixer target = Mixer;

        if (target == null)
            return;

        target.SetFloat(EffectsParameter, ToDecibels(EffectsVolume));
        target.SetFloat(MusicParameter, ToDecibels(MusicVolume));
    }

    /// <summary>Grupo del mezclador para ese canal (null si no hay mezclador).</summary>
    public static AudioMixerGroup GetGroup(AudioChannel.Channel channel)
    {
        AudioMixer target = Mixer;

        if (target == null)
            return null;

        AudioMixerGroup[] groups = target.FindMatchingGroups(channel == AudioChannel.Channel.Music ? MusicGroup : EffectsGroup);

        return groups.Length > 0 ? groups[0] : null;
    }

    /// <summary>Guarda en disco lo que se cambió (al cerrar Configuración).</summary>
    public static void Save()
    {
        PlayerPrefs.Save();
    }

    /// <summary>0 a 1 → decibeles. 0 es silencio (-80 dB).</summary>
    public static float ToDecibels(float volume)
    {
        return volume <= 0.0001f ? -80f : Mathf.Log10(volume) * 20f;
    }

    private static void SetVolume(string key, float volume)
    {
        PlayerPrefs.SetFloat(key, Mathf.Clamp01(volume));
        ApplyAudio();
        Changed?.Invoke();
    }
}
