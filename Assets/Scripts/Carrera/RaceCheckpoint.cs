using UnityEngine;

public class RaceCheckpoint : MonoBehaviour
{
    [Header("Checkpoint")]
    [SerializeField]
    private int checkpointIndex;

    [Header("Respawn")]
    [SerializeField]
    private Transform respawnPoint;

    public int CheckpointIndex =>
        checkpointIndex;

    public Transform RespawnPoint =>
        respawnPoint;

    private void Awake()
    {
        ResolveRespawnPoint();
    }

    private void OnTriggerEnter(Collider other)
    {
        Rigidbody rb =
            other.attachedRigidbody;

        if (rb == null)
            return;

        RaceCheckpointManager manager =
            FindFirstObjectByType<RaceCheckpointManager>();

        if (manager == null)
            return;

        manager.TryPassCheckpoint(
            rb,
            checkpointIndex
        );
    }

    private void ResolveRespawnPoint()
    {
        if (respawnPoint != null)
            return;

        Transform[] children =
            GetComponentsInChildren<Transform>(
                true
            );

        foreach (Transform child in children)
        {
            if (child == transform)
                continue;

            if (child.name == "RespawnPoint")
            {
                respawnPoint = child;
                return;
            }
        }

        Debug.LogError(
            $"El checkpoint {checkpointIndex} no tiene " +
            "un hijo llamado 'RespawnPoint'.",
            this
        );
    }
}