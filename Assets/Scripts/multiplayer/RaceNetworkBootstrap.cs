using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// Arranca la red al cargar la carrera si nadie la arrancó antes:
/// - Si se llegó desde el menú con una partida armada, la red ya está andando
///   y no hace nada.
/// - Si no (un jugador, o probando la escena suelta), crea el NetworkManager y
///   arranca un host local que no abre puertos a internet.
/// - En el editor con Multiplayer Play Mode, los jugadores virtuales se
///   conectan como clientes al editor principal.
/// </summary>
public class RaceNetworkBootstrap : MonoBehaviour
{
    [Tooltip("Prefab con el NetworkManager y el transporte.")]
    [SerializeField] private NetworkManager networkManagerPrefab;

    [SerializeField] private RaceManager raceManager;

    [Tooltip("En el editor: cuántos jugadores esperar antes de largar (1 + los jugadores virtuales activos de Multiplayer Play Mode).")]
    [SerializeField, Min(1)] private int editorExpectedPlayers = 1;

    private const string LocalAddress = "127.0.0.1";
    private const ushort LocalPort = 7777;

    private IEnumerator Start()
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (manager == null)
        {
            if (networkManagerPrefab == null)
            {
                Debug.LogError("RaceNetworkBootstrap necesita el prefab del NetworkManager.", this);
                yield break;
            }

            manager = Instantiate(networkManagerPrefab);
        }

        // Vino desde el menú con la partida armada: esperar a todos los conectados.
        if (manager.IsListening)
        {
            if (manager.IsServer && raceManager != null)
                raceManager.SetExpectedPlayers(manager.ConnectedClientsIds.Count);

            yield break;
        }

        UnityTransport transport = manager.GetComponent<UnityTransport>();

        if (transport != null)
            transport.SetConnectionData(LocalAddress, LocalPort, LocalAddress);

#if UNITY_EDITOR
        if (!Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor)
        {
            // El host tarda un momento en abrir el puerto.
            yield return new WaitForSeconds(1f);
            manager.StartClient();
            yield break;
        }

        if (raceManager != null)
            raceManager.SetExpectedPlayers(editorExpectedPlayers);
#endif

        manager.StartHost();
    }
}
