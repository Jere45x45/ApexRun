using UnityEngine;

public class RaceCheckpoint : MonoBehaviour
{
    [SerializeField]
    [Min(0)]
    private int checkpointIndex;

    [SerializeField]
    private RaceCheckpointManager manager;

    public int CheckpointIndex => checkpointIndex;

    private void Reset()
    {
        Collider collider =
            GetComponent<Collider>();

        if (collider != null)
        {
            collider.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (manager == null)
            return;

        Rigidbody rb =
            other.attachedRigidbody;

        if (rb == null)
            return;

        manager.TryPassCheckpoint(
            rb,
            checkpointIndex
        );
    }
}