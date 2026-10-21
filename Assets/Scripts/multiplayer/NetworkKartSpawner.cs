using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Pone los karts en la grilla de largada. Corre solo en el servidor (el
/// host):
/// - Bots: los que se eligieron en el menú (RaceSetupStore.RaceBots, de 0 a
///   6), del nivel elegido. Salen adelante, en los primeros lugares, así el
///   jugador tiene que pasarlos. Los simula el host y los demás los ven como
///   un kart más.
/// - Jugadores: cuando uno se conecta, crea su kart en el primer lugar libre
///   detrás de los bots y se lo da (SpawnAsPlayerObject). Cuando se va,
///   Netcode borra su kart y el lugar queda libre.
/// </summary>
public class NetworkKartSpawner : MonoBehaviour
{
    [Tooltip("Prefab del kart de red (con NetworkObject y NetworkKart).")]
    [SerializeField] private NetworkObject kartPrefab;

    [Tooltip("Lugares de la grilla, en orden de largada.")]
    [SerializeField] private Transform[] gridSlots = new Transform[0];

    [Header("Bots")]
    [Tooltip("Prefab del bot de red (KartBotRed: un kart de red con CustomIABots y BotRandomizer).")]
    [SerializeField] private NetworkObject botPrefab;

    private readonly Dictionary<ulong, int> slotByClient = new Dictionary<ulong, int>();

    private NetworkManager networkManager;

    // Lugares de adelante que quedan para los bots.
    private int botSlots;
    private bool botsSpawned;

    private void Start()
    {
        networkManager = NetworkManager.Singleton;

        if (networkManager == null || kartPrefab == null || gridSlots.Length == 0)
        {
            Debug.LogError("NetworkKartSpawner necesita un NetworkManager en la escena, el prefab del kart y la grilla.", this);
            enabled = false;
            return;
        }

        // Siempre queda al menos un lugar para un jugador.
        botSlots = botPrefab != null ? Mathf.Clamp(RaceSetupStore.RaceBots, 0, Mathf.Max(0, gridSlots.Length - 1)) : 0;

        networkManager.OnClientConnectedCallback += HandleClientConnected;
        networkManager.OnClientDisconnectCallback += HandleClientDisconnected;

        // Si la red ya estaba andando al cargar la escena, los que ya están conectados también necesitan kart.
        if (networkManager.IsServer)
        {
            SpawnBots();

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
        if (!networkManager.IsServer)
            return;

        SpawnBots();
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

    /// <summary>Los bots de la carrera, en los primeros lugares (una sola vez).</summary>
    private void SpawnBots()
    {
        if (botsSpawned || botSlots == 0)
            return;

        botsSpawned = true;

        RaceDifficulty difficulty = RaceSetupStore.BotDifficulty;

        for (int i = 0; i < botSlots; i++)
        {
            Transform point = gridSlots[i];

            if (point == null)
                continue;

            NetworkObject bot = Instantiate(botPrefab, point.position, point.rotation);
            bot.name = $"Bot{i + 1}";

            CustomIABots driver = bot.GetComponent<CustomIABots>();

            if (driver != null)
                driver.SetDifficulty(difficulty);

            // Las piezas van antes de aparecer en red: así todos lo ven igual.
            BotRandomizer parts = bot.GetComponent<BotRandomizer>();

            if (parts != null)
                parts.RandomizeKart();

            bot.Spawn(true);

            NetworkKart networkKart = bot.GetComponent<NetworkKart>();

            if (networkKart != null)
                networkKart.SetDisplayName($"BOT {i + 1}");
        }

        Debug.Log($"CARRERA: {botSlots} bots, nivel {RaceDifficultySettings.Label(difficulty)}.", this);
    }

    private int FindFreeSlot()
    {
        for (int i = botSlots; i < gridSlots.Length; i++)
        {
            if (gridSlots[i] != null && !slotByClient.ContainsValue(i))
                return i;
        }

        return -1;
    }
}
