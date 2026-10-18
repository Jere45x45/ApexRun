using UnityEngine;

/// <summary>
/// Mouse durante la carrera: bloqueado en el centro y oculto mientras se
/// maneja, así no se va de la ventana ni tapa la pista. Queda libre cuando
/// hace falta usarlo:
/// - con el menú de pausa abierto (RacePauseMenu.IsOpen, también en red),
/// - con la tabla de resultados en pantalla,
/// - al salir de la carrera (al Inicio, al Catálogo o al reiniciar).
/// </summary>
public class RaceCursorLock : MonoBehaviour
{
    [SerializeField] private RacePauseMenu pauseMenu;

    [Tooltip("Panel de la tabla de resultados (el de RaceResultsUI).")]
    [SerializeField] private GameObject resultsPanel;

    private void Update()
    {
        Apply(ShouldLock());
    }

    private void OnDisable()
    {
        // Se va de la carrera: el menú necesita el mouse.
        Apply(false);
    }

    private bool ShouldLock()
    {
        if (pauseMenu != null && pauseMenu.IsOpen)
            return false;

        if (resultsPanel != null && resultsPanel.activeInHierarchy)
            return false;

        return true;
    }

    private static void Apply(bool locked)
    {
        CursorLockMode mode = locked ? CursorLockMode.Locked : CursorLockMode.None;

        if (Cursor.lockState != mode)
            Cursor.lockState = mode;

        if (Cursor.visible == locked)
            Cursor.visible = !locked;
    }
}
