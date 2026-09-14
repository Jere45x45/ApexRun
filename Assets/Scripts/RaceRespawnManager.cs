using UnityEngine;

public class RaceRespawnManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private RaceCheckpointManager checkpointManager;

    [SerializeField]
    private Transform startRespawnPoint;

    [Header("Respawn")]
    [SerializeField]
    private float verticalOffset = 0.5f;

    public void RespawnKart(
        Rigidbody kart)
    {
        if (kart == null)
            return;

        if (checkpointManager == null)
        {
            Debug.LogError(
                "RaceRespawnManager necesita un RaceCheckpointManager.",
                this
            );

            return;
        }

        Transform respawnPoint =
            GetRespawnPoint(kart);

        if (respawnPoint == null)
            return;

        Vector3 respawnPosition =
            respawnPoint.position +
            respawnPoint.up * verticalOffset;

        kart.position =
            respawnPosition;

        kart.rotation =
            respawnPoint.rotation;

        kart.linearVelocity =
            Vector3.zero;

        kart.angularVelocity =
            Vector3.zero;
    }

    private Transform GetRespawnPoint(
        Rigidbody kart)
    {
        int lastCheckpoint =
            checkpointManager.GetLastCheckpoint(
                kart
            );

        if (lastCheckpoint < 0)
        {
            if (startRespawnPoint == null)
            {
                Debug.LogError(
                    $"El kart {kart.name} todavía no pasó " +
                    "ningún checkpoint y RaceRespawnManager " +
                    "no tiene un Start Respawn Point asignado.",
                    this
                );

                return null;
            }

            return startRespawnPoint;
        }

        RaceCheckpoint checkpoint =
            checkpointManager.GetCheckpoint(
                lastCheckpoint
            );

        if (checkpoint == null)
        {
            Debug.LogError(
                $"No se encontró el checkpoint {lastCheckpoint}.",
                this
            );

            return null;
        }

        if (checkpoint.RespawnPoint == null)
        {
            Debug.LogError(
                $"El checkpoint {lastCheckpoint} no tiene " +
                "RespawnPoint asignado.",
                checkpoint
            );

            return null;
        }

        return checkpoint.RespawnPoint;
    }
}