using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Penalizaciones de tiempo de cada kart. Las lleva el servidor (el host).
/// - AddPenalty: aplica una penalización. Solo en el servidor (zonas de
///   penalización, avisos de los clientes).
/// - RequestPenalty: lo que usan los sistemas que corren en cada computadora
///   (por ejemplo, los límites de pista, que se miden en el kart simulado). En
///   el servidor aplica; en un cliente le avisa al servidor, que controla que
///   el aviso venga del dueño de ese kart.
/// Los clientes ven las penalizaciones en RaceTimingManager.
/// </summary>
public class RacePenaltyManager : NetworkBehaviour
{
    [Tooltip("Penalización máxima que el servidor acepta en un solo aviso de un cliente (s).")]
    [SerializeField, Min(0f)] private float maxReportedPenalty = 10f;

    public event Action<Rigidbody, float> PenaltyApplied;

    private readonly Dictionary<Rigidbody, float> penaltyTimeByKart = new();

    public void RegisterKart(Rigidbody kartRigidbody)
    {
        if (kartRigidbody == null)
            return;

        if (!penaltyTimeByKart.ContainsKey(kartRigidbody))
        {
            penaltyTimeByKart.Add(kartRigidbody, 0f);
        }
    }

    public void UnregisterKart(Rigidbody kartRigidbody)
    {
        if (kartRigidbody == null)
            return;

        penaltyTimeByKart.Remove(kartRigidbody);
    }

    public bool IsRegistered(Rigidbody kartRigidbody)
    {
        if (kartRigidbody == null)
            return false;

        return penaltyTimeByKart.ContainsKey(kartRigidbody);
    }

    /// <summary>
    /// Pide una penalización para un kart. En el servidor se aplica; en un
    /// cliente se le avisa al servidor.
    /// </summary>
    public void RequestPenalty(Rigidbody kartRigidbody, float seconds)
    {
        if (kartRigidbody == null || seconds <= 0f)
            return;

        if (NetworkRole.IsAuthority)
        {
            AddPenalty(kartRigidbody, seconds);
            return;
        }

        NetworkObject kartObject = kartRigidbody.GetComponent<NetworkObject>();

        if (kartObject != null && kartObject.IsSpawned)
            ReportPenaltyRpc(kartObject, seconds);
    }

    [Rpc(SendTo.Server, RequireOwnership = false)]
    private void ReportPenaltyRpc(NetworkObjectReference kartReference, float seconds, RpcParams rpcParams = default)
    {
        if (!kartReference.TryGet(out NetworkObject kartObject))
            return;

        // Solo el dueño de un kart puede avisar de sus propias penalizaciones.
        if (kartObject.OwnerClientId != rpcParams.Receive.SenderClientId)
        {
            Debug.LogWarning($"[PENALTY] El jugador {rpcParams.Receive.SenderClientId} intentó penalizar un kart ajeno ({kartObject.name}).", this);
            return;
        }

        AddPenalty(kartObject.GetComponent<Rigidbody>(), Mathf.Clamp(seconds, 0f, maxReportedPenalty));
    }

    /// <summary>Aplica una penalización. Solo en el servidor.</summary>
    public void AddPenalty(Rigidbody kartRigidbody, float seconds)
    {
        if (kartRigidbody == null)
            return;

        if (seconds <= 0f)
            return;

        if (!NetworkRole.IsAuthority)
        {
            Debug.LogWarning("RacePenaltyManager.AddPenalty solo se usa en el servidor; en un cliente va RequestPenalty.", this);
            return;
        }

        RegisterKart(kartRigidbody);

        penaltyTimeByKart[kartRigidbody] += seconds;

        Debug.Log(
            $"[PENALTY] {kartRigidbody.name} recibió +{seconds:F1}s. " +
            $"Penalización total: {penaltyTimeByKart[kartRigidbody]:F1}s",
            kartRigidbody
        );

        PenaltyApplied?.Invoke(kartRigidbody, seconds);
    }

    public float GetPenaltyTime(Rigidbody kartRigidbody)
    {
        if (kartRigidbody == null)
            return 0f;

        if (penaltyTimeByKart.TryGetValue(kartRigidbody, out float penaltyTime))
        {
            return penaltyTime;
        }

        return 0f;
    }

    public void ClearPenalty(Rigidbody kartRigidbody)
    {
        if (kartRigidbody == null)
            return;

        if (penaltyTimeByKart.ContainsKey(kartRigidbody))
        {
            penaltyTimeByKart[kartRigidbody] = 0f;
        }
    }

    public void ClearAllPenalties()
    {
        List<Rigidbody> karts = new(penaltyTimeByKart.Keys);

        foreach (Rigidbody kart in karts)
        {
            penaltyTimeByKart[kart] = 0f;
        }
    }
}
