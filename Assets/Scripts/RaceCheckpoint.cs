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
}