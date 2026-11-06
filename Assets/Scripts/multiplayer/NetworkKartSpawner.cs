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
/// - Después de la qualy (RaceSession.GridOrder), cada uno larga en el lugar
///   que sacó: bots y jugadores mezclados. El que no estaba en la qualy va al
///   primer lugar libre de atrás.
/// - Cada bot se arma con la misma semilla (RaceSession.BotSeed), así se ve
///   igual en la qualy y en todas las fechas del campeonato.
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

    [Tooltip("Con qué piezas corre cada nivel (solo estadísticas; lo que se ve lo elige BotRandomizer al azar). Es el mismo asset que usa el entrenamiento.")]
    [SerializeField] private BotPartsByLevel botParts;

    private readonly Dictionary<ulong, int> slotByClient = new Dictionary<ulong, int>();

    // Lugares ocupados (bots y jugadores).
    private readonly HashSet<int> usedSlots = new HashSet<int>();

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

        // Cada escena arma los bots de nuevo, con las mismas combinaciones.
        BotRandomizer.ResetUsedCombinations();

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
        if (networkManager.IsServer && slotByClient.TryGetValue(clientId, out int slot))
        {
            usedSlots.Remove(slot);
            slotByClient.Remove(clientId);
        }
    }

    private void SpawnKart(ulong clientId)
    {
        if (slotByClient.ContainsKey(clientId))
            return;

        int slot = SlotFor("P:" + clientId, FindFreeSlot());

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
        usedSlots.Add(slot);
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
            int slot = SlotFor($"B:Bot{i + 1}", i);

            if (slot < 0 || gridSlots[slot] == null)
                continue;

            usedSlots.Add(slot);
            Transform point = gridSlots[slot];

            NetworkObject bot = Instantiate(botPrefab, point.position, point.rotation);
            bot.name = $"Bot{i + 1}";

            CustomIABots driver = bot.GetComponent<CustomIABots>();

            if (driver != null)
                driver.SetDifficulty(difficulty);

            // Cómo anda: las piezas del nivel. Lo que se ve sigue siendo al
            // azar (BotRandomizer, abajo), sin tocar las estadísticas.
            InstallBotParts(bot, difficulty);

            // Las piezas van antes de aparecer en red: así todos lo ven igual.
            BotRandomizer parts = bot.GetComponent<BotRandomizer>();

            if (parts != null)
            {
                // Siempre la misma para este bot en este campeonato.
                Random.State previous = Random.state;
                Random.InitState(RaceSession.BotSeed + (i + 1) * 7919);
                parts.RandomizeKart();
                Random.state = previous;
            }

            bot.Spawn(true);

            NetworkKart networkKart = bot.GetComponent<NetworkKart>();

            if (networkKart != null)
                networkKart.SetDisplayName($"BOT {i + 1}");
        }

        Debug.Log($"CARRERA: {botSlots} bots, nivel {RaceDifficultySettings.Label(difficulty)}.", this);
    }

    /// <summary>Instala en el bot las piezas de su nivel (motor, ruedas, kit aero...).</summary>
    private void InstallBotParts(NetworkObject bot, RaceDifficulty difficulty)
    {
        if (botParts != null)
            botParts.Install(bot.GetComponentInChildren<KartConfigurationController>(true), difficulty);
    }

    private int FindFreeSlot()
    {
        for (int i = botSlots; i < gridSlots.Length; i++)
        {
            if (gridSlots[i] != null && !usedSlots.Contains(i) && !IsReserved(i))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Lugar de un kart: el que sacó en la qualy (RaceSession.GridOrder) si
    /// está libre; si no, fallback; si tampoco, el primer lugar libre que no
    /// sea de otro de la qualy (y como último recurso, cualquiera libre).
    /// </summary>
    private int SlotFor(string key, int fallback)
    {
        IReadOnlyList<string> order = RaceSession.GridOrder;

        if (order != null)
        {
            for (int i = 0; i < order.Count && i < gridSlots.Length; i++)
            {
                if (order[i] == key && gridSlots[i] != null && !usedSlots.Contains(i))
                    return i;
            }
        }

        if (fallback >= 0 && fallback < gridSlots.Length && gridSlots[fallback] != null && !usedSlots.Contains(fallback) && !IsReserved(fallback))
            return fallback;

        for (int i = 0; i < gridSlots.Length; i++)
        {
            if (gridSlots[i] != null && !usedSlots.Contains(i) && !IsReserved(i))
                return i;
        }

        for (int i = 0; i < gridSlots.Length; i++)
        {
            if (gridSlots[i] != null && !usedSlots.Contains(i))
                return i;
        }

        return -1;
    }

    /// <summary>Ese lugar lo sacó otro en la qualy.</summary>
    private static bool IsReserved(int slot)
    {
        IReadOnlyList<string> order = RaceSession.GridOrder;
        return order != null && slot < order.Count;
    }
}
