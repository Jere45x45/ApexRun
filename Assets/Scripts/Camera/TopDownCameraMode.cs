using UnityEngine;

/// <summary>
/// Cámara cenital (la 7 de BeamNG, "Top Down"): mira derecho hacia abajo,
/// desde más alto cuanto más rápido va el kart, y corrida hacia adelante para
/// ver lo que viene. Gira con el rumbo del kart, así "arriba" en la pantalla
/// es siempre "adelante".
/// </summary>
public class TopDownCameraMode : KartCameraMode
{
    [Header("Altura")]
    [SerializeField, Min(1f)] private float height = 14f;

    [Tooltip("Metros de altura extra por cada m/s de velocidad.")]
    [SerializeField, Min(0f)] private float heightPerSpeed = 0.3f;

    [Header("Seguimiento")]
    [Tooltip("La cámara se corre hacia donde va a estar el kart dentro de este tiempo (s).")]
    [SerializeField, Min(0f)] private float lookAheadTime = 0.6f;

    [Tooltip("Qué tan rápido gira la cámara con el rumbo del kart.")]
    [SerializeField, Min(0f)] private float yawSharpness = 3f;

    [Tooltip("Qué tan rápido se acomodan la altura y el corrimiento.")]
    [SerializeField, Min(0f)] private float positionSharpness = 4f;

    private float yaw;
    private Vector3 offset;
    private float currentHeight;

    private void Reset()
    {
        fieldOfView = 50f;
        nearClipPlane = 0.3f;
    }

    public override void Activate(KartCameraContext context)
    {
        yaw = FlatYaw(context.Target.forward);
        offset = DesiredOffset(context);
        currentHeight = DesiredHeight(context);
    }

    public override void UpdateCamera(
        KartCameraContext context,
        float deltaTime,
        out Vector3 position,
        out Quaternion rotation)
    {
        float k = Smoothing(positionSharpness, deltaTime);

        yaw = Mathf.LerpAngle(yaw, FlatYaw(context.Target.forward), Smoothing(yawSharpness, deltaTime));
        offset = Vector3.Lerp(offset, DesiredOffset(context), k);
        currentHeight = Mathf.Lerp(currentHeight, DesiredHeight(context), k);

        position = context.Target.position + offset + Vector3.up * currentHeight;
        rotation = Quaternion.Euler(90f, yaw, 0f);
    }

    private Vector3 DesiredOffset(KartCameraContext context)
    {
        return Vector3.ProjectOnPlane(context.Velocity, Vector3.up) * lookAheadTime;
    }

    private float DesiredHeight(KartCameraContext context)
    {
        return height + heightPerSpeed * context.Velocity.magnitude;
    }
}
