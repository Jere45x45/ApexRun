using UnityEngine;

public class RaceRespawnZone : MonoBehaviour
{
    [SerializeField]
    private RaceRespawnManager respawnManager;

    private void Reset()
    {
        Collider collider =
            GetComponent<Collider>();

        if (collider != null)
        {
            collider.isTrigger = true;
        }
    }

    private void OnTriggerEnter(
        Collider other)
    {
        if (respawnManager == null)
            return;

        Rigidbody rb =
            other.attachedRigidbody;

        if (rb == null)
            return;

        respawnManager.Respawn(
            rb
        );
    }
}