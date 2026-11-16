using UnityEngine;

public class RaceRespawnZone : MonoBehaviour
{
    [SerializeField]
    private RaceRespawnManager respawnManager;

    private void OnTriggerEnter(Collider other)
    {
        Rigidbody kart =
            other.attachedRigidbody;

        if (kart == null)
            return;

        if (respawnManager == null)
            return;

        respawnManager.RespawnKart(
            kart
        );
    }
}