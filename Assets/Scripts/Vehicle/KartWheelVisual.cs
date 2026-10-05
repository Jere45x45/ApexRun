using System;
using UnityEngine;

/// <summary>
/// Pone el modelo de una rueda donde está la rueda física: en su centro
/// (incluido el movimiento de la mangueta), con su ángulo de dirección y
/// girando a la velocidad de su eje.
///
/// Va en LateUpdate y usa el Transform del kart (no el Rigidbody): con la
/// interpolación activada, el Transform tiene la pose suave que se dibuja.
/// </summary>
public class KartWheelVisual
{
    // Misma convención que los modelos de rueda del catálogo (WheelVisualController):
    // las ruedas izquierdas se giran 180° para que la llanta quede hacia afuera.
    private static readonly Quaternion LeftModelRotation = Quaternion.Euler(0f, 180f, 0f);

    private readonly KartWheel wheel;
    private readonly Quaternion modelRotation;

    private float spinAngle;

    public KartWheelVisual(KartWheel wheel, bool isLeftSide)
    {
        this.wheel = wheel ?? throw new ArgumentNullException(nameof(wheel));
        modelRotation = isLeftSide ? LeftModelRotation : Quaternion.identity;
    }

    /// <summary>Acomoda el modelo. Si no hay modelo, solo sigue contando el giro.</summary>
    public void UpdatePose(Transform kart, Transform visual, float deltaTime)
    {
        if (kart == null)
            throw new ArgumentNullException(nameof(kart));

        // Girando hacia adelante, el frente de la rueda baja: rotación positiva en x.
        spinAngle = Mathf.Repeat(spinAngle + wheel.AngularVelocity * Mathf.Rad2Deg * deltaTime, 360f);

        if (visual == null)
            return;

        Vector3 position = kart.position + kart.rotation * (wheel.LocalPosition + wheel.GeometryOffset);

        Quaternion rotation =
            kart.rotation *
            Quaternion.Euler(0f, wheel.SteerAngle, 0f) *
            Quaternion.Euler(spinAngle, 0f, 0f) *
            modelRotation;

        visual.SetPositionAndRotation(position, rotation);
    }
}
