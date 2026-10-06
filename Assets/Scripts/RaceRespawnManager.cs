using UnityEngine;

public class RaceRespawnManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private RaceCheckpointManager checkpointManager;

    [Tooltip("Sabe el último checkpoint de cada kart también en los clientes.")]
    [SerializeField]
    private RaceTimingManager timingManager;

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

        // La posición de un kart la manda su dueño: lo reubica la computadora
        // que lo simula, y a las demás les llega por red.
        KartBehaviour simulatedKart =
            kart.GetComponent<KartBehaviour>();

        if (simulatedKart != null &&
            !simulatedKart.IsSimulated)
        {
            return;
        }

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

        // En red, Teleport avisa que es un salto y las copias no lo
        // muestran deslizándose desde donde estaba.
        Unity.Netcode.Components.NetworkTransform networkTransform =
            kart.GetComponent<Unity.Netcode.Components.NetworkTransform>();

        if (networkTransform != null &&
            networkTransform.IsSpawned &&
            networkTransform.CanCommitToTransform)
        {
            networkTransform.Teleport(
                respawnPosition,
                respawnPoint.rotation,
                kart.transform.localScale
            );
        }
        else
        {
            kart.position =
                respawnPosition;

            kart.rotation =
                respawnPoint.rotation;
        }

        kart.linearVelocity =
            Vector3.zero;

        kart.angularVelocity =
            Vector3.zero;

        // La física del kart guarda estado propio (giro de las ruedas,
        // motor, goma deformada): se pone a cero junto con el Rigidbody.
        KartBehaviour behaviour =
            kart.GetComponent<KartBehaviour>();

        if (behaviour != null)
        {
            behaviour.ResetMotion();
        }
    }

    private Transform GetRespawnPoint(
        Rigidbody kart)
    {
        int lastCheckpoint =
            timingManager != null
                ? timingManager.GetLastCheckpoint(kart)
                : checkpointManager.GetLastCheckpoint(kart);

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