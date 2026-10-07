using System;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// NetworkTransform del kart: el de Netcode, que además avisa en el servidor
/// cada vez que llega una posición nueva del dueño, con el momento (tick de
/// red) en que el dueño la simuló.
/// El anti-trampa (KartMovementValidator) mide con eso y no con la copia
/// interpolada: después de una traba de red, la copia se queda quieta y se
/// pone al día de golpe, y parecería que el kart saltó.
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

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        ownerPosition = transform.position;
    }

    protected override void OnNetworkTransformStateUpdated(ref NetworkTransformState oldState, ref NetworkTransformState newState)
    {
        base.OnNetworkTransformStateUpdated(ref oldState, ref newState);

        if (!IsServer || CanCommitToTransform)
            return;

        bool teleport = newState.IsTeleportingNextFrame;

        if (!teleport && !newState.HasPositionChange)
            return;

        Vector3 received = newState.GetPosition();

        if (teleport || newState.HasPositionX)
            ownerPosition.x = received.x;

        if (teleport || newState.HasPositionY)
            ownerPosition.y = received.y;

        if (teleport || newState.HasPositionZ)
            ownerPosition.z = received.z;

        double ownerTime = newState.GetNetworkTick() / (double)NetworkManager.NetworkConfig.TickRate;

        OwnerStateReceived?.Invoke(this, ownerPosition, ownerTime, teleport);
    }
}
