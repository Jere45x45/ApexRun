using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Pone un kart por jugador en la grilla de largada. Corre solo en el
/// servidor (el host): cuando un jugador se conecta, crea su kart en el primer
/// lugar libre y se lo da (SpawnAsPlayerObject). Cuando se va, Netcode borra su
/// kart y el lugar queda libre.
/// </summary>
public class NetworkKartSpawner : MonoBehaviour
{
    [Tooltip("Prefab del kart de red (con NetworkObject y NetworkKart).")]
    [SerializeField] private NetworkObject kartPrefab;

    [Tooltip("Lugares de la grilla, en orden de largada.")]
    [SerializeField] private Transform[] gridSlots = new Transform[0];

    private readonly Dictionary<ulong, int> slotByClient = new Dictionary<ulong, int>();

    private NetworkManager networkManager;

    private void Start()
    {
        networkManager = NetworkManager.Singleton;

        if (networkManager == null || kartPrefab == null || gridSlots.Length == 0)
        {
            Debug.LogError("NetworkKartSpawner necesita un NetworkManager en la escena, el prefab del kart y la grilla.", this);
            enabled = false;
            return;
        }

        networkManager.OnClientConnectedCallback += HandleClientConnected;
        networkManager.OnClientDisconnectCallback += HandleClientDisconnected;

        // Si la red ya estaba andando al cargar la escena, los que ya están conectados también necesitan kart.
        if (networkManager.IsServer)
        {
            foreach (ulong clientId in networkManager.ConnectedClientsIds)
                SpawnKart(clientId);
        }
    }

    private void OnDestroy()
    {
        if (networkManager == null)
            return;

        networkManager.OnClientConnectedCallback -= HandleClientConnected;
        networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (networkManager.IsServer)
            SpawnKart(clientId);
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (networkManager.IsServer)
            slotByClient.Remove(clientId);
    }

    private void SpawnKart(ulong clientId)
    {
        if (slotByClient.ContainsKey(clientId))
            return;

        int slot = FindFreeSlot();

        if (slot < 0)
        {
            Debug.LogWarning($"NetworkKartSpawner: no hay lugar en la grilla para el jugador {clientId}.", this);
            return;
        }

        Transform point = gridSlots[slot];
        NetworkObject kart = Instantiate(kartPrefab, point.position, point.rotation);

        kart.name = $"Kart_Jugador{clientId}";
        kart.SpawnAsPlayerObject(clientId, true);

        slotByClient[clientId] = slot;
    }

    private int FindFreeSlot()
    {
        for (int i = 0; i < gridSlots.Length; i++)
        {
            if (gridSlots[i] != null && !slotByClient.ContainsValue(i))
                return i;
        }

        return -1;
    }
}
