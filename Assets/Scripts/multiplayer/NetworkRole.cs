using Unity.Netcode;

/// <summary>
/// Atajos para saber qué papel tiene esta computadora en la partida.
/// Sin red andando, cuenta como servidor (todo se decide acá).
/// </summary>
public static class NetworkRole
{
    /// <summary>True si la red está andando y esta computadora es solo cliente (no host).</summary>
    public static bool IsClientOnly
    {
        get
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager != null && manager.IsListening && !manager.IsServer;
        }
    }

    /// <summary>True si esta computadora decide la carrera: host, servidor o sin red.</summary>
    public static bool IsAuthority => !IsClientOnly;
}
