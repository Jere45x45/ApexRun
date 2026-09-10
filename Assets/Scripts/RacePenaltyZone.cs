using System.Collections.Generic;
using UnityEngine;

public class RacePenaltyZone : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RacePenaltyManager penaltyManager;

    [Header("Penalty")]
    [SerializeField] private float requiredStayDuration = 0.75f;
    [SerializeField] private float penaltySeconds = 3f;

    private class KartPenaltyState
    {
        public int overlapCount;
        public float enterTime;
        public bool penaltyApplied;
    }

    private readonly Dictionary<Rigidbody, KartPenaltyState> kartStates = new();

    private void OnTriggerEnter(Collider other)
    {
        Rigidbody kartRigidbody = other.attachedRigidbody;

        if (kartRigidbody == null)
            return;

        if (penaltyManager == null)
            return;

        if (!penaltyManager.IsRegistered(kartRigidbody))
            return;

        if (!kartStates.TryGetValue(kartRigidbody, out KartPenaltyState state))
        {
            state = new KartPenaltyState
            {
                overlapCount = 1,
                enterTime = Time.time,
                penaltyApplied = false
            };

            kartStates.Add(kartRigidbody, state);
        }
        else
        {
            state.overlapCount++;
        }
    }

    private void OnTriggerStay(Collider other)
    {
        Rigidbody kartRigidbody = other.attachedRigidbody;

        if (kartRigidbody == null)
            return;

        if (!kartStates.TryGetValue(kartRigidbody, out KartPenaltyState state))
            return;

        if (state.penaltyApplied)
            return;

        float timeInside = Time.time - state.enterTime;

        if (timeInside >= requiredStayDuration)
        {
            penaltyManager.AddPenalty(kartRigidbody, penaltySeconds);

            state.penaltyApplied = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        Rigidbody kartRigidbody = other.attachedRigidbody;

        if (kartRigidbody == null)
            return;

        if (!kartStates.TryGetValue(kartRigidbody, out KartPenaltyState state))
            return;

        state.overlapCount--;

        if (state.overlapCount <= 0)
        {
            kartStates.Remove(kartRigidbody);
        }
    }

    private void OnDisable()
    {
        kartStates.Clear();
    }
}