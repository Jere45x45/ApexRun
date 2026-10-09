using UnityEngine;

/// <summary>
/// Cámara del piloto (la 2 de BeamNG; la de cabina de Assetto Corsa): a la
/// altura de los ojos, en el asiento.
/// - La cabeza se mueve con las fuerzas G: al frenar va hacia adelante, en
///   curva hacia afuera, en los pozos rebota. Como en Assetto Corsa.
/// - Mira un poco hacia adentro de la curva, como hace un piloto real.
/// - La cabeza compensa parte de la inclinación del kart (el horizonte se
///   mueve menos que el chasis), como hacen los reflejos del cuello.
/// - Con el botón derecho del mouse (o el stick derecho) se mira alrededor; al
///   soltar, la vista vuelve al frente.
/// </summary>
public class DriverCameraMode : KartCameraMode
{
    private const float Gravity = 9.81f;

    [Header("Ojos")]
    [Tooltip("Posición de los ojos del piloto en coordenadas del kart (m).")]
    [SerializeField] private Vector3 eyePosition = new Vector3(0f, 0.507f, -0.232f);

    [Tooltip("Inclinación hacia abajo de la mirada (grados).")]
    [SerializeField, Range(-20f, 30f)] private float eyePitch = 4.23f;

    [Header("Cabeza y fuerzas G")]
    [Tooltip("Cuánto se mueve la cabeza por cada g de aceleración (m), en x (costado), y (vertical) y z (adelante/atrás).")]
    [SerializeField] private Vector3 headMovementPerG = new Vector3(0.03f, 0.015f, 0.04f);

    [Tooltip("Movimiento máximo de la cabeza (m).")]
    [SerializeField, Min(0f)] private float maxHeadOffset = 0.08f;

    [Tooltip("Cuánto compensa la cabeza la inclinación del kart: 0 = gira con el chasis, 1 = horizonte siempre derecho.")]
    [SerializeField, Range(0f, 1f)] private float horizonLock = 0.5f;

    [Header("Mirar hacia la curva")]
    [Tooltip("Grados que gira la mirada por cada grado/s de giro del kart.")]
    [SerializeField, Min(0f)] private float lookIntoTurn = 0.15f;

    [SerializeField, Min(0f)] private float maxLookIntoTurn = 12f;

    [SerializeField, Min(0f)] private float lookIntoTurnSharpness = 4f;

    [Header("Mirar a mano")]
    [SerializeField, Range(0f, 180f)] private float maxLookYaw = 140f;

    [SerializeField, Range(0f, 90f)] private float maxLookPitch = 60f;

    [Tooltip("Qué tan rápido vuelve la vista al frente al soltar.")]
    [SerializeField, Min(0f)] private float lookReturnSharpness = 6f;

    private float turnLook;
    private float lookYaw;
    private float lookPitch;

    private void Reset()
    {
        fieldOfView = 60f;
        nearClipPlane = 0.03f;
    }

    public override void Activate(KartCameraContext context)
    {
        turnLook = 0f;
        lookYaw = 0f;
        lookPitch = 0f;
    }

    public override void UpdateCamera(
        KartCameraContext context,
        float deltaTime,
        out Vector3 position,
        out Quaternion rotation)
    {
        Transform kart = context.Target;

        // La cabeza va al revés de la aceleración (por inercia).
        Vector3 accelerationG = context.LocalAcceleration / Gravity;

        Vector3 headOffset = new Vector3(
            -accelerationG.x * headMovementPerG.x,
            -accelerationG.y * headMovementPerG.y,
            -accelerationG.z * headMovementPerG.z
        );

        headOffset = Vector3.ClampMagnitude(headOffset, maxHeadOffset);

        position = kart.TransformPoint(eyePosition + headOffset);

        // Mirar hacia adentro de la curva.
        float desiredTurnLook = Mathf.Clamp(context.YawRate * lookIntoTurn, -maxLookIntoTurn, maxLookIntoTurn);
        turnLook = Mathf.Lerp(turnLook, desiredTurnLook, Smoothing(lookIntoTurnSharpness, deltaTime));

        // Mirar a mano.
        if (context.IsLooking)
        {
            lookYaw = Mathf.Clamp(lookYaw + context.LookDelta.x, -maxLookYaw, maxLookYaw);
            lookPitch = Mathf.Clamp(lookPitch - context.LookDelta.y, -maxLookPitch, maxLookPitch);
        }
        else
        {
            float k = Smoothing(lookReturnSharpness, deltaTime);
            lookYaw = Mathf.Lerp(lookYaw, 0f, k);
            lookPitch = Mathf.Lerp(lookPitch, 0f, k);
        }

        // Parte de la inclinación del chasis no llega a la vista.
        Quaternion chassis = kart.rotation;
        Quaternion level = Quaternion.LookRotation(kart.forward, Vector3.up);
        Quaternion head = Quaternion.Slerp(chassis, level, horizonLock);

        rotation = head * Quaternion.Euler(eyePitch + lookPitch, turnLook + lookYaw, 0f);
    }
}
