using System;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// NetworkTransform del kart: el de Netcode, más lo último que mandó el dueño
/// sin interpolar (posición, velocidad y cuándo llegó), en las computadoras
/// que ven una copia del kart.
/// - El anti-trampa (KartMovementValidator, en el servidor) mide con eso y no
///   con la copia interpolada: después de una traba de red, la copia se queda
///   quieta y se pone al día de golpe, y parecería que el kart saltó.
/// - Los choques en red (KartRemoteContacts) lo usan para saber dónde está el
///   kart ahora, y no dónde se lo ve (la copia va atrasada).
/// </summary>
public class KartNetworkTransform : NetworkTransform
{
    /// <summary>
    /// Solo en el servidor, para karts que mueve otra computadora: posición
    /// del dueño, tiempo de red en que la simuló (s) y si fue un teleport.
    /// </summary>
    public event Action<KartNetworkTransform, Vector3, double, bool> OwnerStateReceived;

    // Llegan solo los ejes que cambiaron: se arma la posición completa acá.
    private Vector3 ownerPosition;
    private double ownerTime;
    private float receivedAt;

    /// <summary>True si ya llegó algún estado del dueño (solo en las copias).</summary>
    public bool HasOwnerState { get; private set; }

    /// <summary>Última posición que mandó el dueño, sin interpolar.</summary>
    public Vector3 OwnerPosition => ownerPosition;

    /// <summary>Velocidad del kart según las dos últimas posiciones del dueño (m/s).</summary>
    public Vector3 OwnerVelocity { get; private set; }

    /// <summary>Hace cuánto llegó el último estado del dueño (s).</summary>
    public float OwnerStateAge => Time.time - receivedAt;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        ownerPosition = transform.position;
        HasOwnerState = false;
        OwnerVelocity = Vector3.zero;
    }

    protected override void OnNetworkTransformStateUpdated(ref NetworkTransformState oldState, ref NetworkTransformState newState)
    {
        base.OnNetworkTransformStateUpdated(ref oldState, ref newState);

        if (CanCommitToTransform)
            return;

        bool teleport = newState.IsTeleportingNextFrame;

        if (!teleport && !newState.HasPositionChange)
            return;

        Vector3 previous = ownerPosition;
        Vector3 received = newState.GetPosition();

        if (teleport || newState.HasPositionX)
            ownerPosition.x = received.x;

        if (teleport || newState.HasPositionY)
            ownerPosition.y = received.y;

        if (teleport || newState.HasPositionZ)
            ownerPosition.z = received.z;

        double time = newState.GetNetworkTick() / (double)NetworkManager.NetworkConfig.TickRate;
        double elapsed = time - ownerTime;

        if (teleport || !HasOwnerState)
            OwnerVelocity = Vector3.zero;
        else if (elapsed > 0.001)
            OwnerVelocity = (ownerPosition - previous) / (float)elapsed;

        ownerTime = time;
        receivedAt = Time.time;
        HasOwnerState = true;

        if (IsServer)
            OwnerStateReceived?.Invoke(this, ownerPosition, time, teleport);
    }
}
