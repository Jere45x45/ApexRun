using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Salida del Catálogo:
/// - El anfitrión (o el jugador solo) aprieta A CORRER y van todos a la carrera.
/// - Los demás ven "Esperando al anfitrión".
/// - Volver sale de la partida y vuelve al Inicio.
/// </summary>
public class CatalogFlowUI : MonoBehaviour
{
    [SerializeField] private Button raceButton;

    [SerializeField] private TMP_Text waitingText;

    [SerializeField] private Button backButton;

    [Header("Escenas (sin GameSession, abriendo el Catálogo suelto)")]
    [SerializeField] private string raceScene = "Race";

    [SerializeField] private string menuScene = "Inicio";

    private void Awake()
    {
        raceButton.onClick.AddListener(GoToRace);
        backButton.onClick.AddListener(GoToMenu);
    }

    private void Start()
    {
        bool canStart = NetworkRole.IsAuthority;

        raceButton.gameObject.SetActive(canStart);
        waitingText.gameObject.SetActive(!canStart);
    }

    private void GoToRace()
    {
        if (GameSession.Instance != null)
            GameSession.Instance.LoadSceneForAll(raceScene);
        else
            SceneManager.LoadScene(raceScene);
    }

    private void GoToMenu()
    {
        if (GameSession.Instance != null)
            GameSession.Instance.LeaveToMenu();
        else
            SceneManager.LoadScene(menuScene);
    }
}
