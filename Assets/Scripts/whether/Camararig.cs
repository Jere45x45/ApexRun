using UnityEngine;

/// <summary>
/// Rig de cámara: sigue a un target (el kart) con suavizado de posición y
/// rotación, en vez de ser hijo directo de su transform. Evita que la cámara
/// herede sacudidas bruscas (derrapes, pozos, choques).
/// Colgar este script en un GameObject vacío llamado "CameraRig",
/// ubicado en la raíz de la escena (NO hijo del kart).
/// </summary>
public class CameraRig : MonoBehaviour
{
    [Header("Target a seguir")]
    [SerializeField] private Transform target; // el Kart

    [Header("Offset relativo al target")]
    [SerializeField] private Vector3 positionOffset = new Vector3(0f, 2.5f, -6f);

    [Header("Suavizado")]
    [Tooltip("Más alto = el rig reacciona más rápido (menos 'delay')")]
    [SerializeField] private float positionSmoothSpeed = 8f;
    [SerializeField] private float rotationSmoothSpeed = 6f;

    [Header("Mirar al target")]
    [SerializeField] private bool lookAtTarget = true;
    [SerializeField] private Vector3 lookAtOffset = new Vector3(0f, 1f, 0f);

    private void LateUpdate()
    {
        if (target == null) return;

        // --- Posición deseada: offset rotado SOLO en Y (yaw) ---
        // Sacamos el yaw proyectando el vector "forward" del kart sobre el
        // plano horizontal, en vez de leer target.eulerAngles.y directamente.
        // eulerAngles.y se "contamina" cuando el objeto también tiene
        // rotación en X/Z (roll/pitch por la física de suspensión), dando
        // valores de yaw incorrectos justo cuando el auto se inclina al
        // acelerar/frenar/doblar. Proyectar el forward evita ese problema.
        Vector3 flatForward = Vector3.ProjectOnPlane(target.forward, Vector3.up);
        if (flatForward.sqrMagnitude < 0.0001f)
        {
            // Caso borde: el kart está casi vertical (forward ~ paralelo a Y).
            // No debería pasar en una carrera normal, pero por las dudas
            // evitamos un LookRotation inválido.
            flatForward = transform.forward;
        }
        Quaternion flatRotation = Quaternion.LookRotation(flatForward.normalized, Vector3.up);
        Vector3 desiredPosition = target.position + flatRotation * positionOffset;
        transform.position = Vector3.Lerp(
            transform.position,
            desiredPosition,
            1f - Mathf.Exp(-positionSmoothSpeed * Time.deltaTime)
        );

        // --- Rotación ---
        if (lookAtTarget)
        {
            Vector3 lookPoint = target.position + lookAtOffset;
            Quaternion desiredRotation = Quaternion.LookRotation(lookPoint - transform.position);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                desiredRotation,
                1f - Mathf.Exp(-rotationSmoothSpeed * Time.deltaTime)
            );
        }
        else
        {
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                target.rotation,
                1f - Mathf.Exp(-rotationSmoothSpeed * Time.deltaTime)
            );
        }
    }

    // Útil para asignar el target por código si instanciás el kart en runtime
    // (por ejemplo, en multijugador, cuando el jugador local spawnea su auto).
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
}