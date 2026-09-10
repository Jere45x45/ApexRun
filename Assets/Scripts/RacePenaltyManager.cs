using System;
using System.Collections.Generic;
using UnityEngine;

public class RacePenaltyManager : MonoBehaviour
{
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

    public void AddPenalty(Rigidbody kartRigidbody, float seconds)
    {
        if (kartRigidbody == null)
            return;

        if (seconds <= 0f)
            return;

        RegisterKart(kartRigidbody);

        penaltyTimeByKart[kartRigidbody] += seconds;

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