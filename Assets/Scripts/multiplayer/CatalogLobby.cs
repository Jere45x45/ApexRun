using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Sala del Catálogo en red: quién está y quién ya eligió sus piezas.
/// - Cada jugador se anota con su nombre al entrar al Catálogo y avisa cuando
///   está listo (o cuando cancela). La lista la lleva el servidor y la ven
///   todos.
/// - El anfitrión puede largar cuando todos los invitados están listos.
/// En un jugador, o con el Catálogo abierto suelto, no se usa.
/// </summary>
public class CatalogLobby : NetworkBehaviour
{
    public struct Player : INetworkSerializeByMemcpy, System.IEquatable<Player>
    {
        public ulong ClientId;
        public FixedString32Bytes Name;
        public byte Ready;

        public bool IsReady => Ready != 0;

        public bool Equals(Player other)
        {
            return ClientId == other.ClientId && Name.Equals(other.Name) && Ready == other.Ready;
        }
    }

    private NetworkList<Player> players;

    public int Count => IsSpawned ? players.Count : 0;

    public Player this[int index] => players[index];

    /// <summary>True si el jugador de esta computadora avisó que está listo.</summary>
    public bool IsLocalReady => IsSpawned && IsReady(NetworkManager.LocalClientId);

    private void Awake()
    {
        players = new NetworkList<Player>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;

        string playerName = GameSession.Instance != null ? GameSession.Instance.LocalPlayerName : "Piloto";

        JoinRpc(new FixedString32Bytes(playerName));
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
    }

    public bool IsReady(ulong clientId)
    {
        int index = IndexOf(clientId);
        return index >= 0 && players[index].IsReady;
    }

    public bool IsHostPlayer(ulong clientId)
    {
        return clientId == NetworkManager.ServerClientId;
    }

    /// <summary>El jugador de esta computadora avisa que está listo (o cancela).</summary>
    public void SetReady(bool ready)
    {
        if (IsSpawned)
            SetReadyRpc(ready);
    }

    /// <summary>
    /// Solo el servidor: cuántos invitados conectados están listos y cuántos
    /// hay. Cuenta los conectados (no la lista): uno que todavía está cargando
    /// el Catálogo cuenta como no listo.
    /// </summary>
    public void CountGuests(out int ready, out int total)
    {
        ready = 0;
        total = 0;

        if (!IsSpawned || !IsServer)
            return;

        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            if (clientId == NetworkManager.ServerClientId)
                continue;

            total++;

            if (IsReady(clientId))
                ready++;
        }
    }

    [Rpc(SendTo.Server, RequireOwnership = false)]
    private void JoinRpc(FixedString32Bytes playerName, RpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        Player player = new Player
        {
            ClientId = clientId,
            Name = playerName,
            Ready = 0
        };

        int index = IndexOf(clientId);

        if (index >= 0)
            players[index] = player;
        else
            players.Add(player);
    }

    [Rpc(SendTo.Server, RequireOwnership = false)]
    private void SetReadyRpc(bool ready, RpcParams rpcParams = default)
    {
        int index = IndexOf(rpcParams.Receive.SenderClientId);

        if (index < 0)
            return;

        Player player = players[index];
        player.Ready = (byte)(ready ? 1 : 0);
        players[index] = player;
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        int index = IndexOf(clientId);

        if (index >= 0)
            players.RemoveAt(index);
    }

    private int IndexOf(ulong clientId)
    {
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].ClientId == clientId)
                return i;
        }

        return -1;
    }
}
