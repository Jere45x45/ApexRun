using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// SOLO PARA PROBAR la red sin menú ni sesiones:
/// - En el editor con Multiplayer Play Mode, el editor principal arranca como
///   host y los jugadores virtuales se conectan como clientes, solos.
/// - En un build, botones Host / Cliente arriba a la izquierda.
/// - Habilita los mandos del kart propio cuando está listo (en la carrera
///   real eso lo hace RaceManager con la cuenta regresiva).
/// </summary>
public class NetworkTestStarter : MonoBehaviour
{
    [Tooltip("Arrancar solo (host en el editor principal, cliente en los jugadores virtuales).")]
    [SerializeField] private bool autoStartInEditor = true;

    private void OnEnable()
    {
        NetworkKart.LocalKartSpawned += HandleLocalKartSpawned;
    }

    private void OnDisable()
    {
        NetworkKart.LocalKartSpawned -= HandleLocalKartSpawned;
    }

    private IEnumerator Start()
    {
#if UNITY_EDITOR
        if (!autoStartInEditor)
            yield break;

        NetworkManager manager = NetworkManager.Singleton;

        if (manager == null)
            yield break;

        if (Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor)
        {
            manager.StartHost();
        }
        else
        {
            // El host tarda un momento en abrir el puerto.
            yield return new WaitForSeconds(1f);
            manager.StartClient();
        }
#else
        yield break;
#endif
    }

    private void OnGUI()
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (manager == null || manager.IsClient || manager.IsServer)
            return;

        GUILayout.BeginArea(new Rect(10, 10, 200, 100));

        if (GUILayout.Button("Host"))
            manager.StartHost();

        if (GUILayout.Button("Cliente"))
            manager.StartClient();

        GUILayout.EndArea();
    }

    private void HandleLocalKartSpawned(NetworkKart kart)
    {
        StartCoroutine(EnableInputWhenReady(kart.Kart));
    }

    private static IEnumerator EnableInputWhenReady(KartBehaviour kart)
    {
        // KartBehaviour.Start arma la física y deja los mandos apagados.
        while (kart != null && kart.Vehicle == null)
            yield return null;

        yield return null;

        if (kart != null)
            kart.SetInputEnabled(true);
    }
}
