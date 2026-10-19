
using UnityEngine;

public class DifficultyManager : MonoBehaviour
{
    [Header("Grupos completos de cada dificultad")]
    [SerializeField] private GameObject facil;
    [SerializeField] private GameObject medio;
    [SerializeField] private GameObject dificil;

    private void Awake()
    {
        // Ninguna carrera comienza automaticamente.
        if (facil != null) facil.SetActive(false);
        if (medio != null) medio.SetActive(false);
        if (dificil != null) dificil.SetActive(false);
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

    private void SelectDifficulty(GameObject grupo, int nivel)
    {
        if (grupo == null)
        {
            Debug.LogError("Falta asignar el grupo del nivel " + nivel, this);
            return;
        }

        if (BotLearning.Instance == null)
        {
            Debug.LogError(
                "No se encontro BotLearning activo en la escena.",
                this
            );
            return;
        }

        // 1. Cargar la Q-table correspondiente.
        BotLearning.Instance.SetDifficultyTable(nivel);

        // 2. Desactivar los otros grupos.
        if (facil != null) facil.SetActive(false);
        if (medio != null) medio.SetActive(false);
        if (dificil != null) dificil.SetActive(false);

        // 3. Activar la pista y los bots seleccionados.
        grupo.SetActive(true);

        // 4. Configurar los bots e iniciar la carrera.
        CustomIABots[] bots =
            grupo.GetComponentsInChildren<CustomIABots>(true);

        Debug.Log(
            "Nivel " + nivel + " | Bots encontrados: " + bots.Length,
            this
        );

        foreach (CustomIABots bot in bots)
        {
            bot.SetDifficulty(nivel);
            bot.StartRace();
        }

        Debug.Log("Carrera preparada: " + grupo.name, this);
    }
}
