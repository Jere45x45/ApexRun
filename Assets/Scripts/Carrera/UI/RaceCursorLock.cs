using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Mouse durante la carrera: bloqueado y oculto mientras se maneja, así no se
/// va de la ventana ni tapa la pista. Queda libre cuando hace falta usarlo:
/// - con el menú de pausa abierto (RacePauseMenu.IsOpen, también en red),
/// - con la tabla de resultados en pantalla,
/// - al salir de la carrera (al Inicio, al Catálogo o al reiniciar),
/// - un rato después de mover el mouse (showOnMouseMove), para poder hacer
///   clic en el botón de pausa del HUD. Con el botón derecho apretado no,
///   porque eso es mirar alrededor con la cámara.
/// Al bloquearlo, Unity lo lleva al centro de la ventana. Por eso, al
/// liberarlo se lo devuelve a donde estaba antes de bloquearlo; si no,
/// aparecería siempre en el centro, entre los botones del menú.
/// </summary>
public class RaceCursorLock : MonoBehaviour
{
    [SerializeField] private RacePauseMenu pauseMenu;

    [Tooltip("Panel de la tabla de resultados (el de RaceResultsUI).")]
    [SerializeField] private GameObject resultsPanel;

    [Tooltip("Al mover el mouse aparece (para el botón de pausa) y se vuelve a esconder después de hideAfter segundos quieto.")]
    [SerializeField] private bool showOnMouseMove = true;

    [Tooltip("Segundos con el mouse quieto (y fuera de la interfaz) hasta que se vuelve a esconder.")]
    [SerializeField, Min(0.2f)] private float hideAfter = 2f;

    // Píxeles que tiene que moverse el mouse en un cuadro para que aparezca.
    private const float MoveThreshold = 2f;

    // Hasta cuándo (tiempo real) se deja el mouse a la vista.
    private float showUntil = -1f;

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

        if (showOnMouseMove && MouseWantsToBeSeen())
            return false;

        return true;
    }

    /// <summary>
    /// True un rato después de mover el mouse (sin el botón derecho, que es
    /// para mirar con la cámara), y mientras esté sobre la interfaz (el botón
    /// de pausa), así no desaparece justo antes del clic.
    /// </summary>
    private bool MouseWantsToBeSeen()
    {
        Mouse mouse = Mouse.current;

        if (mouse == null)
            return false;

        if (mouse.rightButton.isPressed)
        {
            showUntil = -1f;
            return false;
        }

        float now = Time.unscaledTime;

        if (mouse.delta.ReadValue().sqrMagnitude > MoveThreshold * MoveThreshold)
            showUntil = now + hideAfter;

        bool visible = showUntil > now;

        if (visible && UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
        {
            showUntil = now + hideAfter;
        }

        return visible;
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
