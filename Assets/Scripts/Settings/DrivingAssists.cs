using System;
using UnityEngine;

/// <summary>
/// Ayudas de manejo que elige el jugador en Configuración. Se guardan en
/// PlayerPrefs y solo cambian el kart del jugador (KartBehaviour con
/// UsesPlayerAssists): los bots manejan siempre con las tres.
/// - ABS: no deja que las ruedas se bloqueen al frenar.
/// - Control de tracción: arrancando o muy despacio, baja el acelerador si
///   las traseras patinan (así no hace trompo al salir con el volante girado).
/// - Ayuda de volante: no deja girar las ruedas más allá del máximo agarre.
///   Sin ella, girar de más arrastra las delanteras y el kart sigue de largo.
/// Todas vienen activadas.
/// </summary>
public static class DrivingAssists
{
    public enum Assist
    {
        Abs,
        TractionControl,
        Steering
    }

    private const string AbsKey = "ApexRun.AyudaABS";
    private const string TractionKey = "ApexRun.AyudaTraccion";
    private const string SteeringKey = "ApexRun.AyudaVolante";

    /// <summary>Cambió alguna ayuda.</summary>
    public static event Action Changed;

    public static bool Abs
    {
        get => Get(Assist.Abs);
        set => Set(Assist.Abs, value);
    }

    public static bool TractionControl
    {
        get => Get(Assist.TractionControl);
        set => Set(Assist.TractionControl, value);
    }

    public static bool SteeringAssist
    {
        get => Get(Assist.Steering);
        set => Set(Assist.Steering, value);
    }

    public static bool Get(Assist assist)
    {
        return PlayerPrefs.GetInt(KeyOf(assist), 1) != 0;
    }

    public static void Set(Assist assist, bool enabled)
    {
        if (Get(assist) == enabled && PlayerPrefs.HasKey(KeyOf(assist)))
            return;

        PlayerPrefs.SetInt(KeyOf(assist), enabled ? 1 : 0);
        Changed?.Invoke();
    }

    /// <summary>"Activado" / "Desactivado" en el idioma actual.</summary>
    public static string StateText(bool enabled)
    {
        return enabled
            ? GameLanguage.Text("Activado", "On")
            : GameLanguage.Text("Desactivado", "Off");
    }

    private static string KeyOf(Assist assist)
    {
        switch (assist)
        {
            case Assist.Abs:
                return AbsKey;
            case Assist.TractionControl:
                return TractionKey;
            default:
                return SteeringKey;
        }
    }

#if UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Changed = null;
    }
#endif
}
