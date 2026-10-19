
using UnityEngine;

public class DifficultyManager : MonoBehaviour
{
    [Header("Grupos completos de cada dificultad")]
    [SerializeField] private GameObject facil;
    [SerializeField] private GameObject medio;
    [SerializeField] private GameObject dificil;

    private void Awake()
    {
        // Al iniciar, ninguna carrera está activa.
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

    private void SelectDifficulty(GameObject grupoSeleccionado, int nivel)
    {
        Debug.Log("Se presionó un botón de dificultad: " + nivel);

        if (grupoSeleccionado == null)
        {
            Debug.LogError("Falta asignar el Empty de la dificultad " + nivel, this);
            return;
        }

        // Desactivar todos los grupos.
        if (facil != null) facil.SetActive(false);
        if (medio != null) medio.SetActive(false);
        if (dificil != null) dificil.SetActive(false);

        // Activar el Empty elegido: incluye pista y bots.
        grupoSeleccionado.SetActive(true);

        // Configurar los bots y ordenarles que comiencen.
        CustomIABots[] bots =
            grupoSeleccionado.GetComponentsInChildren<CustomIABots>(true);

        Debug.Log("Bots encontrados en el grupo: " + bots.Length);

        foreach (CustomIABots bot in bots)
        {
            bot.SetDifficulty(nivel);
            bot.StartRace();
        }

        Debug.Log("Dificultad activada: " + grupoSeleccionado.name);
    }
}
