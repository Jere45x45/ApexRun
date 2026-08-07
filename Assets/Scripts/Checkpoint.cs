using UnityEngine;

public class Checkpoint : MonoBehaviour
{
    public int checkpointIndex;

    private void OnTriggerEnter(Collider col)
    {
        KartAgent agent = col.GetComponentInParent<KartAgent>();

        if (agent != null)
        {
            agent.ReachCheckpoint(checkpointIndex);
        }
    }
}
