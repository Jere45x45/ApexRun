using UnityEngine;

/// <summary>
/// Un nivel de bots (fácil, medio o difícil): cómo maneja y qué premia su
/// entrenamiento. Los tres usan el mismo kart y la misma velocidad máxima
/// (120 km/h); lo que cambia es la forma de seguir la pista:
/// - Trazada (laneWidth): cuánto del ancho de la pista puede usar. Con poco
///   va siempre por el medio y en las curvas tiene que frenar más. Con todo
///   puede abrirse antes de la curva y cerrarse en el vértice, y pasarla más
///   rápido.
/// - Recompensas: el fácil casi solo premia no caerse (aprende la forma más
///   lenta de completar la pista, la que le sale primero); el difícil premia
///   ir rápido, aunque tenga que arriesgar más.
/// - Defensa: el difícil, en carrera, tapa al que viene atrás.
/// Lo que aprende cada nivel queda en su Q-table (qTable).
/// </summary>
[CreateAssetMenu(menuName = "ApexRun/Bots/Nivel de bot", fileName = "BotNivel")]
public class BotLevelProfile : ScriptableObject
{
    public RaceDifficulty difficulty = RaceDifficulty.Medium;

    [Tooltip("Cómo se muestra el nivel en el menú y en la consola.")]
    public string displayName = "MEDIO";

    [Tooltip("Lo que aprendió este nivel (Q-table, JSON). Se usa también en el juego compilado. " +
             "Al entrenar en el editor se guarda en este mismo archivo.")]
    public TextAsset qTable;

    [Header("Trazada")]
    [Tooltip("Cuánto del ancho utilizable de la pista puede usar (0 a 1). Tiene 5 carriles repartidos en ese ancho.")]
    [Range(0.1f, 1f)] public float laneWidth = 0.65f;

    [Header("Recompensas del entrenamiento")]
    [Tooltip("Premio por cada metro que avanza sobre la pista.")]
    [Min(0f)] public float progressReward = 0.1f;

    [Tooltip("Castigo por cada decisión (cada 0,25 s): empuja a ir más rápido.")]
    [Min(0f)] public float timePenalty = 0.03f;

    [Tooltip("Castigo por decisión con las cuatro ruedas fuera del asfalto (proporcional a cuántas).")]
    [Min(0f)] public float offTrackPenalty = 0.6f;

    [Tooltip("Castigo por caerse, salirse de la pista, hacer un trompo o quedar trabado.")]
    [Min(0f)] public float incidentPenalty = 15f;

    [Tooltip("Castigo por decisión según cuánto se aleja del centro de la pista (el fácil prefiere el medio).")]
    [Min(0f)] public float centerPreference = 0f;

    [Header("Carrera")]
    [Tooltip("Tapa al que viene atrás: se pone en su línea para que no lo pase.")]
    public bool defendPosition;

    [Tooltip("Desde qué distancia atrás (m) empieza a defender.")]
    [Min(1f)] public float defendDistance = 12f;
}
