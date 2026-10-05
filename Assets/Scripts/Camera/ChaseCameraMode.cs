using System;
using UnityEngine;

/// <summary>
/// Cámara de persecución (la 6 de BeamNG; las "chase" de Assetto Corsa): atrás
/// del kart, a una distancia fija.
/// - Gira siguiendo una mezcla entre hacia dónde apunta el kart y hacia dónde
///   va de verdad. Así, cuando la cola se va, se ve al kart cruzado en vez de
///   que la pista gire de golpe.
/// - Ese giro llega con un poco de demora, que da la sensación de que el kart dobla.
/// - Se aleja un poco al acelerar y se acerca al frenar.
/// - Apretar otra vez su número cambia entre cerca y lejos.
/// </summary>
public class ChaseCameraMode : KartCameraMode
{
    [Serializable]
    public class ChasePreset
    {
        public string name = "Cerca";

        [Tooltip("Distancia atrás del kart (m).")]
        [Min(0.5f)] public float distance = 3.2f;

        [Tooltip("Altura sobre el kart (m).")]
        public float height = 1.1f;

        [Tooltip("Altura del punto que mira la cámara, sobre el kart (m).")]
        public float lookHeight = 0.6f;

        [Range(10f, 120f)] public float fieldOfView = 62f;
    }

    [Header("Distancias")]
    [SerializeField]
    private ChasePreset[] presets =
    {
        new ChasePreset(),
        new ChasePreset { name = "Lejos", distance = 5.5f, height = 1.8f, lookHeight = 0.7f, fieldOfView = 58f }
    };

    [Header("Giro")]
    [Tooltip("0 = sigue hacia dónde apunta el kart; 1 = sigue hacia dónde va (deriva incluida).")]
    [SerializeField, Range(0f, 1f)] private float velocityFollow = 0.6f;

    [Tooltip("Qué tan rápido gira la cámara detrás del kart.")]
    [SerializeField, Min(0f)] private float yawSharpness = 5f;

    [Tooltip("Qué tan rápido sigue la altura del kart (pozos, lomas).")]
    [SerializeField, Min(0f)] private float heightSharpness = 8f;

    [Tooltip("Metros hacia adelante del kart a los que mira la cámara.")]
    [SerializeField, Min(0f)] private float lookAhead = 1.5f;

    [Header("Aceleración")]
    [Tooltip("Cuánto se aleja (m) por cada m/s² de aceleración (y se acerca al frenar).")]
    [SerializeField, Min(0f)] private float accelerationStretch = 0.03f;

    [SerializeField, Min(0f)] private float maxStretch = 0.4f;

    [Header("Velocidad")]
    [Tooltip("Grados que se abre el campo de visión a 120 km/h, para dar sensación de velocidad.")]
    [SerializeField, Range(0f, 20f)] private float speedFieldOfView = 4f;

    [Tooltip("Radio con el que la cámara evita atravesar la pista y las paredes (m).")]
    [SerializeField, Min(0f)] private float collisionRadius = 0.2f;

    private static readonly ChasePreset DefaultPreset = new ChasePreset();

    private int currentPreset;
    private float yaw;
    private float pivotY;
    private float speedRatio;

    private ChasePreset Preset =>
        presets != null && presets.Length > 0
            ? presets[Mathf.Clamp(currentPreset, 0, presets.Length - 1)]
            : DefaultPreset;

    public override string DisplayName =>
        presets != null && presets.Length > 1 ? base.DisplayName + ": " + Preset.name : base.DisplayName;

    public override float FieldOfView => Preset.fieldOfView + speedFieldOfView * speedRatio;

    private void Reset()
    {
        nearClipPlane = 0.1f;
    }

    public override void Activate(KartCameraContext context)
    {
        yaw = DesiredYaw(context);
        pivotY = context.Target.position.y;
    }

    public override void NextVariant(KartCameraContext context)
    {
        if (presets != null && presets.Length > 0)
        {
            currentPreset = (currentPreset + 1) % presets.Length;
        }
    }

    public override void UpdateCamera(
        KartCameraContext context,
        float deltaTime,
        out Vector3 position,
        out Quaternion rotation)
    {
        Transform kart = context.Target;
        ChasePreset preset = Preset;

        yaw = Mathf.LerpAngle(yaw, DesiredYaw(context), Smoothing(yawSharpness, deltaTime));
        pivotY = Mathf.Lerp(pivotY, kart.position.y, Smoothing(heightSharpness, deltaTime));

        speedRatio = Mathf.Clamp01(context.Velocity.magnitude / 33.3f);

        float stretch = Mathf.Clamp(context.LocalAcceleration.z * accelerationStretch, -maxStretch, maxStretch);

        Quaternion yawRotation = Quaternion.Euler(0f, yaw, 0f);
        Vector3 back = yawRotation * Vector3.back;
        Vector3 pivot = new Vector3(kart.position.x, pivotY, kart.position.z);

        Vector3 anchor = pivot + Vector3.up * preset.lookHeight;
        Vector3 desired = pivot + Vector3.up * preset.height + back * (preset.distance + stretch);

        position = AvoidObstacles(anchor, desired, kart, collisionRadius);

        Vector3 lookPoint = anchor - back * lookAhead;
        rotation = Quaternion.LookRotation(lookPoint - position, Vector3.up);
    }

    /// <summary>
    /// Rumbo al que apunta la cámara: mezcla del rumbo del kart con el de su
    /// velocidad. Despacio (o yendo para atrás) solo cuenta el rumbo del kart.
    /// </summary>
    private float DesiredYaw(KartCameraContext context)
    {
        float heading = FlatYaw(context.Target.forward);

        Vector3 flatVelocity = Vector3.ProjectOnPlane(context.Velocity, Vector3.up);
        float speed = flatVelocity.magnitude;

        if (speed < 0.5f || Vector3.Dot(flatVelocity, context.Target.forward) < 0f)
            return heading;

        float weight = velocityFollow * Mathf.InverseLerp(2f, 8f, speed);

        return Mathf.LerpAngle(heading, FlatYaw(flatVelocity), weight);
    }
}
