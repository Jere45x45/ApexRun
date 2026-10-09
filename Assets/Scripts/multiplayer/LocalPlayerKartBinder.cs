using UnityEngine;

/// <summary>
/// Conecta la escena con los karts que aparecen por red:
/// - El kart del jugador de esta computadora: lo sigue la cámara y lo muestran
///   el HUD, los resultados y el minimapa (en amarillo).
/// - Los demás karts: aparecen en el minimapa y se sacan cuando se van.
/// </summary>
public class LocalPlayerKartBinder : MonoBehaviour
{
    [SerializeField] private KartCameraController cameraController;

    [SerializeField] private RaceHUD hud;

    [SerializeField] private RaceResultsUI results;

    [SerializeField] private TrackMinimap minimap;

    private void OnEnable()
    {
        NetworkKart.LocalKartSpawned += HandleLocalKartSpawned;
        NetworkKart.Spawned += HandleKartSpawned;
        NetworkKart.Despawned += HandleKartDespawned;
    }

    private void OnDisable()
    {
        NetworkKart.LocalKartSpawned -= HandleLocalKartSpawned;
        NetworkKart.Spawned -= HandleKartSpawned;
        NetworkKart.Despawned -= HandleKartDespawned;
    }

    private void Start()
    {
        // Karts que aparecieron antes que este componente.
        foreach (NetworkKart kart in FindObjectsByType<NetworkKart>(FindObjectsSortMode.None))
        {
            if (!kart.IsSpawned)
                continue;

            if (kart.IsLocalPlayer)
                HandleLocalKartSpawned(kart);

            HandleKartSpawned(kart);
        }
    }

    private void HandleLocalKartSpawned(NetworkKart kart)
    {
        Rigidbody body = kart.GetComponent<Rigidbody>();

        if (cameraController != null)
            cameraController.SetTarget(body);

        if (hud != null)
            hud.SetPlayerKart(body);

        if (results != null)
            results.SetPlayerKart(body);
    }

    private void HandleKartSpawned(NetworkKart kart)
    {
        if (minimap != null)
            minimap.AddKart(kart.transform, kart.IsLocalPlayer);
    }

    private void HandleKartDespawned(NetworkKart kart)
    {
        if (minimap != null && kart != null)
            minimap.RemoveKart(kart.transform);
    }
}
