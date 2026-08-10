using UnityEngine;

public class TrainingBounds : MonoBehaviour
{
    private void OnTriggerEnter(Collider col)
    {
        KartAgent agent = col.GetComponentInParent<KartAgent>();

        if (agent != null)
        {
            agent.FallOffTrack();
        }
    }
}
