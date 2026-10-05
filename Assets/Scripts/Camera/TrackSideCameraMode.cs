using UnityEngine;

/// <summary>
/// Cámara externa, de TV (la 4 de BeamNG, "External"; la F3 de Assetto Corsa):
/// se planta en el costado de la pista, adelante del kart, y lo sigue con la
/// mirada y el zoom mientras pasa. Cuando el kart se aleja, se planta otra vez
/// más adelante, alternando de lado.
/// </summary>
public class TrackSideCameraMode : KartCameraMode
{
    [Header("Ubicación")]
    [Tooltip("Dónde se planta la cámara: donde va a estar el kart dentro de este tiempo (s).")]
    [SerializeField, Min(0f)] private float leadTime = 2.5f;

    [Tooltip("Distancia mínima adelante del kart a la que se planta (m).")]
    [SerializeField, Min(1f)] private float minLeadDistance = 20f;

    [Tooltip("Distancia al costado del recorrido (m): se elige al azar entre estos dos valores.")]
    [SerializeField] private Vector2 sideDistance = new Vector2(5f, 9f);

    [Tooltip("Altura de la cámara sobre el piso (m).")]
    [SerializeField, Min(0.2f)] private float height = 1.6f;

    [Tooltip("Cuando el kart se aleja más que esto, la cámara se planta otra vez (m).")]
    [SerializeField, Min(5f)] private float maxDistance = 45f;

    [Header("Seguimiento")]
    [Tooltip("Tamaño de la toma alrededor del kart (m): el zoom se ajusta para mantenerlo.")]
    [SerializeField, Min(0.5f)] private float shotSize = 5f;

    [SerializeField, Range(1f, 120f)] private float minFieldOfView = 8f;

    [SerializeField, Range(1f, 120f)] private float maxFieldOfView = 55f;

    [Tooltip("Qué tan rápido gira la cámara para seguir al kart.")]
    [SerializeField, Min(0f)] private float lookSharpness = 10f;

    [Tooltip("Qué tan rápido ajusta el zoom.")]
    [SerializeField, Min(0f)] private float zoomSharpness = 5f;

    private Vector3 cameraPosition;
    private Quaternion cameraRotation;
    private float currentFieldOfView;
    private float side = 1f;
    private bool placed;

    public override float FieldOfView => placed ? currentFieldOfView : fieldOfView;

    private void Reset()
    {
        fieldOfView = 40f;
        nearClipPlane = 0.2f;
    }

    public override void Activate(KartCameraContext context)
    {
        Place(context);
        cameraRotation = LookAtKart(context);
        currentFieldOfView = DesiredFieldOfView(context);
    }

    public override void NextVariant(KartCameraContext context)
    {
        Activate(context);
    }

    public override void UpdateCamera(
        KartCameraContext context,
        float deltaTime,
        out Vector3 position,
        out Quaternion rotation)
    {
        Vector3 toKart = context.Target.position - cameraPosition;
        float distance = toKart.magnitude;
        bool movingAway = Vector3.Dot(context.Velocity, toKart) > 0f;

        if (!placed || (distance > maxDistance && movingAway) || distance > 3f * maxDistance)
        {
            Place(context);
            cameraRotation = LookAtKart(context);
            currentFieldOfView = DesiredFieldOfView(context);
        }

        cameraRotation = Quaternion.Slerp(cameraRotation, LookAtKart(context), Smoothing(lookSharpness, deltaTime));
        currentFieldOfView = Mathf.Lerp(currentFieldOfView, DesiredFieldOfView(context), Smoothing(zoomSharpness, deltaTime));

        position = cameraPosition;
        rotation = cameraRotation;
    }

    /// <summary>
    /// Elige un punto adelante, en el recorrido que lleva el kart, corrido hacia
    /// un costado y apoyado sobre el piso. Si desde ahí no se ve al kart, prueba
    /// el otro costado.
    /// </summary>
    private void Place(KartCameraContext context)
    {
        Transform kart = context.Target;

        Vector3 flatVelocity = Vector3.ProjectOnPlane(context.Velocity, Vector3.up);
        float speed = flatVelocity.magnitude;

        Vector3 direction = speed > 2f
            ? flatVelocity / speed
            : Vector3.ProjectOnPlane(kart.forward, Vector3.up).normalized;

        float lead = Mathf.Max(minLeadDistance, speed * leadTime);
        Vector3 ahead = kart.position + direction * lead;
        Vector3 lateral = Vector3.Cross(Vector3.up, direction);
        float offset = Random.Range(sideDistance.x, sideDistance.y);

        side = -side;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            Vector3 candidate = GroundPoint(ahead + lateral * (side * offset), kart);

            if (CanSee(candidate, kart) || attempt == 1)
            {
                cameraPosition = candidate;
                break;
            }

            side = -side;
        }

        placed = true;
    }

    private Vector3 GroundPoint(Vector3 point, Transform kart)
    {
        if (Physics.Raycast(point + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 100f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            return hit.point + Vector3.up * height;
        }

        return new Vector3(point.x, kart.position.y + height, point.z);
    }

    private static bool CanSee(Vector3 from, Transform kart)
    {
        Vector3 to = kart.position + Vector3.up * 0.5f;

        if (!Physics.Linecast(from, to, out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return true;

        return hit.collider.transform.IsChildOf(kart);
    }

    private Quaternion LookAtKart(KartCameraContext context)
    {
        Vector3 toKart = context.Target.position + Vector3.up * 0.3f - cameraPosition;

        return toKart.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(toKart, Vector3.up) : cameraRotation;
    }

    /// <summary>Zoom para que la toma mida shotSize a la distancia del kart.</summary>
    private float DesiredFieldOfView(KartCameraContext context)
    {
        float distance = Mathf.Max(0.1f, Vector3.Distance(cameraPosition, context.Target.position));
        float angle = 2f * Mathf.Atan(0.5f * shotSize / distance) * Mathf.Rad2Deg;

        return Mathf.Clamp(angle, minFieldOfView, maxFieldOfView);
    }
}
