using UnityEngine;

/// <summary>
/// Lo que las cámaras necesitan saber en cada frame: el kart (pose, velocidad,
/// aceleración) y los mandos de cámara del jugador. Lo llena KartCameraController.
/// </summary>
public class KartCameraContext
{
    /// <summary>Transform del kart, con la pose interpolada (la que se ve en pantalla).</summary>
    public Transform Target { get; set; }

    public Rigidbody Body { get; set; }

    /// <summary>Velocidad del kart en el mundo (m/s).</summary>
    public Vector3 Velocity => Body != null ? Body.linearVelocity : Vector3.zero;

    /// <summary>Velocidad de giro del kart alrededor de su eje vertical (°/s, positivo = hacia la derecha).</summary>
    public float YawRate => Body != null && Target != null
        ? Vector3.Dot(Body.angularVelocity, Target.up) * Mathf.Rad2Deg
        : 0f;

    /// <summary>
    /// Aceleración del kart en sus propios ejes (m/s², x = derecha, y = arriba,
    /// z = adelante), suavizada y sin la gravedad.
    /// </summary>
    public Vector3 LocalAcceleration { get; set; }

    /// <summary>Giro pedido por el jugador en este frame (grados): x = derecha, y = arriba.</summary>
    public Vector2 LookDelta { get; set; }

    /// <summary>True mientras el jugador mueve la cámara a mano (botón derecho del mouse o stick derecho).</summary>
    public bool IsLooking { get; set; }

    /// <summary>Rueda del mouse en este frame: +1 acercar, −1 alejar, 0 nada.</summary>
    public float Zoom { get; set; }
}
