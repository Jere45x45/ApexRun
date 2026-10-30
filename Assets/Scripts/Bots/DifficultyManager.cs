
using UnityEngine;

/// <summary>
/// Escena de entrenamiento de los bots (JuliVersionMINI): un grupo por nivel,
/// cada uno con su copia de la pista y sus bots.
/// - Con BotLearning en modo entrenamiento y trainAllOnStart, al darle Play
///   arrancan los tres niveles a la vez, cada uno en su pista. Cada nivel
///   aprende en su propia tabla.
/// - Los botones Fácil, Medio y Difícil dejan solo ese grupo.
/// </summary>
public class DifficultyManager : MonoBehaviour
{
    [Header("Grupos completos de cada dificultad")]
    [SerializeField] private GameObject facil;
    [SerializeField] private GameObject medio;
    [SerializeField] private GameObject dificil;

    [Header("Entrenamiento")]
    [Tooltip("Con BotLearning entrenando: al arrancar, los tres niveles entrenan a la vez.")]
    [SerializeField] private bool trainAllOnStart = true;

    private void Awake()
    {
        // Ninguna carrera comienza automaticamente.
        if (facil != null) facil.SetActive(false);
        if (medio != null) medio.SetActive(false);
        if (dificil != null) dificil.SetActive(false);
    }

    private void Start()
    {
        if (trainAllOnStart && BotLearning.Instance != null && BotLearning.Instance.IsTraining)
            TrainAll();
    }

    public void SelectEasy()
    {
        SelectDifficulty(facil, 1);
    }

    public void SelectMedium()
    {
        SelectDifficulty(medio, 2);
    }

    public void SelectHard()
    {
        SelectDifficulty(dificil, 3);
    }

    /// <summary>Los tres niveles a la vez, cada uno en su pista.</summary>
    public void TrainAll()
    {
        StartGroup(facil, 1);
        StartGroup(medio, 2);
        StartGroup(dificil, 3);
    }

    private void SelectDifficulty(GameObject grupo, int nivel)
    {
        if (grupo == null)
        {
            Debug.LogError("Falta asignar el grupo del nivel " + nivel, this);
            return;
        }

        if (facil != null) facil.SetActive(false);
        if (medio != null) medio.SetActive(false);
        if (dificil != null) dificil.SetActive(false);

        StartGroup(grupo, nivel);
    }

    /// <summary>Activa la pista y los bots del grupo, les pone el nivel y los larga.</summary>
    private void StartGroup(GameObject grupo, int nivel)
    {
        if (grupo == null)
            return;

        grupo.SetActive(true);

        CustomIABots[] bots = grupo.GetComponentsInChildren<CustomIABots>(true);

        foreach (CustomIABots bot in bots)
        {
            bot.SetDifficulty(nivel);
            bot.StartRace();
        }

        Debug.Log("Nivel " + nivel + " | Bots: " + bots.Length + " | " + grupo.name, this);
    }
}
