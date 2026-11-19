using UnityEngine;

/// <summary>Qué sonido hace un botón al tocarlo (lo toca ButtonClickSound).</summary>
public enum ButtonSoundKind
{
    /// <summary>Lo elige ButtonClickSound según el nombre y el texto del botón.</summary>
    Auto,

    /// <summary>El de casi todos: un cambio secuencial con un toque de motor.</summary>
    Normal,

    /// <summary>Volver, cancelar o cerrar: el mismo golpe, más grave y sin motor.</summary>
    Volver,

    /// <summary>Largar una carrera (Empezar, Reiniciar): una acelerada del motor.</summary>
    Empezar,

    /// <summary>Flechas, + y −, pestañas y cartas del taller: un tic corto de llave criquet.</summary>
    Selector,

    /// <summary>Sin sonido.</summary>
    Ninguno
}

/// <summary>
/// Para un botón al que la regla automática de ButtonClickSound no le pone el
/// sonido que corresponde: se le agrega este componente y se elige cuál.
/// </summary>
[DisallowMultipleComponent]
public class ButtonSound : MonoBehaviour
{
    [SerializeField] private ButtonSoundKind kind = ButtonSoundKind.Normal;

    public ButtonSoundKind Kind
    {
        get => kind;
        set => kind = value;
    }
}
