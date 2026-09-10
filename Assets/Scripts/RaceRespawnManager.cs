using UnityEngine;

public class RaceRespawnManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private RaceCheckpointManager checkpointManager;

    [SerializeField]
    private Transform startingRespawnPoint;

    [Header("Respawn")]
    [SerializeField]
    [Min(0f)]
    private float respawnHeight = 0.5f;

    [SerializeField]
    [Min(0f)]
    private float backwardOffset = 1.5f;

    public void Respawn(
        Rigidbody kart)
    {
        if (kart == null)
            return;

        Transform respawnPoint =
            GetRespawnPoint(kart);

        if (respawnPoint == null)
        {
            Debug.LogWarning(
                $"No se encontró un punto de respawn para {kart.name}.",
                kart
            );

            return;
        }

        kart.linearVelocity =
            Vector3.zero;

        kart.angularVelocity =
            Vector3.zero;

        Vector3 forward =
            respawnPoint.forward;

        Vector3 position =
            respawnPoint.position -
            forward * backwardOffset +
            Vector3.up * respawnHeight;

        Quaternion rotation =
            respawnPoint.rotation;

        kart.position =
            position;

        kart.rotation =
            rotation;
    }

    private Transform GetRespawnPoint(
        Rigidbody kart)
    {
        if (checkpointManager == null)
        {
            return startingRespawnPoint;
        }

        int lastCheckpoint =
            checkpointManager.GetLastCheckpoint(
                kart
            );

        if (lastCheckpoint < 0)
        {
            return startingRespawnPoint;
        }

        RaceCheckpoint checkpoint =
            checkpointManager.GetCheckpoint(
                lastCheckpoint
            );

        if (checkpoint == null)
        {
            return startingRespawnPoint;
        }

        return checkpoint.transform;
    }
}