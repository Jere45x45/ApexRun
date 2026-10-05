using UnityEngine;

/// <summary>
/// Cámara de órbita (la 1 de BeamNG): gira alrededor del kart a una distancia
/// fija. Con el botón derecho del mouse (o el stick derecho) se la gira a mano;
/// la rueda acerca o aleja. Si no se la toca, sigue sola el rumbo del kart y
/// después de un momento vuelve atrás del kart.
/// </summary>
public class OrbitCameraMode : KartCameraMode
{
    [Header("Órbita")]
    [Tooltip("Altura del punto que mira la cámara, sobre el centro del kart (m).")]
    [SerializeField] private float pivotHeight = 0.6f;

    [SerializeField, Min(0.5f)] private float defaultDistance = 4.5f;

    [SerializeField, Min(0.5f)] private float minDistance = 1.8f;

    [SerializeField, Min(0.5f)] private float maxDistance = 15f;

    [Tooltip("Cuánto cambia la distancia por cada paso de la rueda del mouse (×).")]
    [SerializeField, Range(1.01f, 2f)] private float zoomStep = 1.15f;

    [Tooltip("Inclinación hacia abajo con la que arranca y a la que vuelve (grados).")]
    [SerializeField, Range(-10f, 80f)] private float defaultPitch = 12f;

    [SerializeField, Range(-30f, 0f)] private float minPitch = -10f;

    [SerializeField, Range(0f, 89f)] private float maxPitch = 80f;

    [Header("Seguimiento")]
    [Tooltip("Qué tan rápido la órbita sigue el rumbo del kart.")]
    [SerializeField, Min(0f)] private float followSharpness = 3f;

    [Tooltip("Segundos sin tocar la cámara antes de que vuelva atrás del kart.")]
    [SerializeField, Min(0f)] private float autoCenterDelay = 2f;

    [Tooltip("Qué tan rápido vuelve atrás del kart.")]
    [SerializeField, Min(0f)] private float autoCenterSharpness = 2f;

    [Tooltip("Radio con el que la cámara evita atravesar la pista y las paredes (m).")]
    [SerializeField, Min(0f)] private float collisionRadius = 0.2f;

    private float baseYaw;
    private float yawOffset;
    private float pitch;
    private float distance;
    private float timeSinceInput;

    private void Reset()
    {
        fieldOfView = 60f;
        nearClipPlane = 0.1f;
    }

    private void Awake()
    {
        distance = defaultDistance;
        pitch = defaultPitch;
    }

    public override void Activate(KartCameraContext context)
    {
        baseYaw = FlatYaw(context.Target.forward);
        yawOffset = 0f;
        pitch = defaultPitch;
        timeSinceInput = autoCenterDelay;
    }

    public override void UpdateCamera(
        KartCameraContext context,
        float deltaTime,
        out Vector3 position,
        out Quaternion rotation)
    {
        if (context.IsLooking)
        {
            yawOffset += context.LookDelta.x;
            pitch -= context.LookDelta.y;
            timeSinceInput = 0f;
        }
        else
        {
            timeSinceInput += deltaTime;
        }

        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        if (timeSinceInput >= autoCenterDelay)
        {
            float k = Smoothing(autoCenterSharpness, deltaTime);
            yawOffset = Mathf.LerpAngle(yawOffset, 0f, k);
            pitch = Mathf.Lerp(pitch, defaultPitch, k);
        }

        if (context.Zoom != 0f)
        {
            distance = Mathf.Clamp(distance * Mathf.Pow(zoomStep, -context.Zoom), minDistance, maxDistance);
        }

        baseYaw = Mathf.LerpAngle(baseYaw, FlatYaw(context.Target.forward), Smoothing(followSharpness, deltaTime));

        rotation = Quaternion.Euler(pitch, baseYaw + yawOffset, 0f);

        Vector3 pivot = context.Target.position + Vector3.up * pivotHeight;
        Vector3 desired = pivot - rotation * Vector3.forward * distance;

        position = AvoidObstacles(pivot, desired, context.Target, collisionRadius);
    }
}
