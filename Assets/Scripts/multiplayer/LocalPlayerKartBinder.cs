using UnityEngine;

/// <summary>
/// Conecta la escena con el kart del jugador de esta computadora cuando
/// aparece por red: por ahora, la cámara lo sigue.
/// </summary>
public class LocalPlayerKartBinder : MonoBehaviour
{
    [SerializeField] private KartCameraController cameraController;

    private void OnEnable()
    {
        NetworkKart.LocalKartSpawned += HandleLocalKartSpawned;
    }

    private void OnDisable()
    {
        NetworkKart.LocalKartSpawned -= HandleLocalKartSpawned;
    }

    private void HandleLocalKartSpawned(NetworkKart kart)
    {
        if (cameraController != null)
            cameraController.SetTarget(kart.GetComponent<Rigidbody>());
    }
}
