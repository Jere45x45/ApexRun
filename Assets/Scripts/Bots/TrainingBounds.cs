using UnityEngine;

public class TrainingBounds : MonoBehaviour
{
    private void OnTriggerEnter(Collider col)
    {
        CustomIABots ai = col.GetComponentInParent<CustomIABots>();

        if (ai != null)
        {
            ai.FallOffTrack();
        }
    }
}
