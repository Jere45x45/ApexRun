using UnityEngine;

/// <summary>
/// Una perspectiva de cámara (órbita, piloto, persecución...). Cada una es un
/// componente en el objeto de la cámara. KartCameraController activa una a la
/// vez y en cada frame le pide la pose.
/// </summary>
public abstract class KartCameraMode : MonoBehaviour
{
    private static readonly RaycastHit[] ObstacleHits = new RaycastHit[16];

    [Tooltip("Nombre que aparece en pantalla al elegir la cámara.")]
    [SerializeField] private string displayName = "Cámara";

    [Tooltip("Campo de visión vertical (grados).")]
    [SerializeField, Range(10f, 120f)] protected float fieldOfView = 60f;

    [Tooltip("Plano cercano de la cámara (m). Las cámaras pegadas al kart necesitan uno chico.")]
    [SerializeField, Min(0.01f)] protected float nearClipPlane = 0.1f;

    public virtual string DisplayName => displayName;

    public virtual float FieldOfView => fieldOfView;

    public float NearClipPlane => nearClipPlane;

    /// <summary>
    /// Se llama al elegir esta cámara y cuando el kart salta de lugar (respawn):
    /// la cámara se pone directo en su pose, sin suavizados.
    /// </summary>
    public virtual void Activate(KartCameraContext context) { }

    /// <summary>
    /// Se llama al apretar otra vez el número de esta cámara. Las que tienen
    /// variantes (cerca/lejos, distintas posiciones) pasan a la siguiente.
    /// </summary>
    public virtual void NextVariant(KartCameraContext context) { }

    /// <summary>Calcula la pose de la cámara para este frame.</summary>
    public abstract void UpdateCamera(
        KartCameraContext context,
        float deltaTime,
        out Vector3 position,
        out Quaternion rotation);

    /// <summary>
    /// Fracción de acercamiento para un suavizado exponencial: con "sharpness"
    /// más alto, la cámara alcanza antes a su objetivo. No depende de los FPS.
    /// </summary>
    protected static float Smoothing(float sharpness, float deltaTime)
    {
        return 1f - Mathf.Exp(-sharpness * deltaTime);
    }

    /// <summary>Rumbo horizontal de una dirección (grados, 0 = +z, 90 = +x).</summary>
    protected static float FlatYaw(Vector3 direction)
    {
        return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
    }

    /// <summary>
    /// Acerca la cámara al punto de apoyo si algo se interpone (la pista, una
    /// pared), para que no quede del otro lado. Ignora el propio kart y los triggers.
    /// </summary>
    protected static Vector3 AvoidObstacles(Vector3 pivot, Vector3 desired, Transform ignore, float radius)
    {
        Vector3 toCamera = desired - pivot;
        float distance = toCamera.magnitude;

        if (distance < 1e-4f)
            return desired;

        Vector3 direction = toCamera / distance;

        int count = Physics.SphereCastNonAlloc(
            pivot,
            radius,
            direction,
            ObstacleHits,
            distance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore
        );

        float closest = distance;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = ObstacleHits[i];

            if (hit.distance <= 0f)
                continue;

            if (ignore != null && hit.collider.transform.IsChildOf(ignore))
                continue;

            closest = Mathf.Min(closest, hit.distance);
        }

        return pivot + direction * closest;
    }
}
