using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Mouse durante la carrera: bloqueado y oculto mientras se maneja, así no se
/// va de la ventana ni tapa la pista. Queda libre cuando hace falta usarlo:
/// - con el menú de pausa abierto (RacePauseMenu.IsOpen, también en red),
/// - con la tabla de resultados en pantalla,
/// - al salir de la carrera (al Inicio, al Catálogo o al reiniciar).
/// Al bloquearlo, Unity lo lleva al centro de la ventana. Por eso, al
/// liberarlo se lo devuelve a donde estaba antes de bloquearlo; si no,
/// aparecería siempre en el centro, entre los botones del menú.
/// </summary>
public class RaceCursorLock : MonoBehaviour
{
    [SerializeField] private RacePauseMenu pauseMenu;

    [Tooltip("Panel de la tabla de resultados (el de RaceResultsUI).")]
    [SerializeField] private GameObject resultsPanel;

    // Dónde estaba el mouse antes de bloquearlo (píxeles, desde abajo a la izquierda).
    private Vector2 positionBeforeLock;
    private bool hasPositionBeforeLock;

    // Al liberarlo, Unity puede volver a centrarlo al final del cuadro: se lo
    // vuelve a poner en su lugar también en el cuadro siguiente.
    private bool restoreNextFrame;

    private void Update()
    {
        if (restoreNextFrame)
        {
            restoreNextFrame = false;
            RestorePosition();
        }

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

    private void Apply(bool locked)
    {
        bool isLocked = Cursor.lockState == CursorLockMode.Locked;

        if (locked && !isLocked)
        {
            if (Mouse.current != null)
            {
                positionBeforeLock = Mouse.current.position.ReadValue();
                hasPositionBeforeLock = true;
            }

            Cursor.lockState = CursorLockMode.Locked;
        }
        else if (!locked && isLocked)
        {
            Cursor.lockState = CursorLockMode.None;

            RestorePosition();
            restoreNextFrame = true;
        }

        if (Cursor.visible == locked)
            Cursor.visible = !locked;
    }

    private void RestorePosition()
    {
        if (hasPositionBeforeLock && Mouse.current != null)
            Mouse.current.WarpCursorPosition(positionBeforeLock);
    }
}
