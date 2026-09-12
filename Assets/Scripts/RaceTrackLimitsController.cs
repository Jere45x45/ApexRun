using System.Collections.Generic;
using UnityEngine;

public class RaceTrackLimitsController : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private RacePenaltyManager penaltyManager;

    [Header("Track Limits")]
    [SerializeField]
    [Min(1)]
    private int minimumInvalidWheelsForPenalty = 3;

    [SerializeField]
    [Min(0f)]
    private float requiredStayDuration = 0.5f;

    [SerializeField]
    [Min(0f)]
    private float penaltySeconds = 3f;

    private class KartTrackLimitsState
    {
        public KartBehaviour Kart;
        public float invalidSince = -1f;
        public bool penaltyApplied;
    }

    private readonly Dictionary<
        KartBehaviour,
        KartTrackLimitsState
    > states = new();

    private void FixedUpdate()
    {
        foreach (
            KeyValuePair<
                KartBehaviour,
                KartTrackLimitsState
            > pair in states)
        {
            EvaluateKart(pair.Value);
        }
    }

    public void RegisterKart(
        KartBehaviour kart)
    {
        if (kart == null)
            return;

        if (states.ContainsKey(kart))
            return;

        KartTrackLimitsState state =
            new KartTrackLimitsState
            {
                Kart = kart
            };

        states.Add(
            kart,
            state
        );

        Rigidbody rb =
            kart.GetComponent<Rigidbody>();

        if (penaltyManager != null &&
            rb != null)
        {
            penaltyManager.RegisterKart(rb);
        }
    }

    public void UnregisterKart(
        KartBehaviour kart)
    {
        if (kart == null)
            return;

        if (!states.Remove(kart))
            return;

        if (penaltyManager == null)
            return;

        Rigidbody rb =
            kart.GetComponent<Rigidbody>();

        if (rb != null)
        {
            penaltyManager.UnregisterKart(rb);
        }
    }

    public int GetInvalidWheelCount(
        KartBehaviour kart)
    {
        if (kart == null)
            return 0;

        KartPhysics physics =
            kart.Physics;

        if (physics == null)
            return 0;

        int invalidWheelCount = 0;

        foreach (
            WheelPhysics wheel
            in physics.Wheels)
        {
            if (wheel == null)
                continue;

            if (!wheel.IsGrounded)
                continue;

            if (wheel.IsOnInvalidSurface)
            {
                invalidWheelCount++;
            }
        }

        return invalidWheelCount;
    }

    public bool IsOutsideTrackLimits(
        KartBehaviour kart)
    {
        return GetInvalidWheelCount(kart) >=
               minimumInvalidWheelsForPenalty;
    }

    private void EvaluateKart(
        KartTrackLimitsState state)
    {
        if (state == null ||
            state.Kart == null)
        {
            return;
        }

        KartBehaviour kart =
            state.Kart;

        int invalidWheelCount =
            GetInvalidWheelCount(kart);

        bool outsideTrackLimits =
            invalidWheelCount >=
            minimumInvalidWheelsForPenalty;

        if (!outsideTrackLimits)
        {
            ResetState(state);
            return;
        }

        if (state.invalidSince < 0f)
        {
            state.invalidSince =
                Time.time;

            state.penaltyApplied =
                false;

            return;
        }

        if (state.penaltyApplied)
            return;

        float durationOutside =
            Time.time -
            state.invalidSince;

        if (durationOutside <
            requiredStayDuration)
        {
            return;
        }

        ApplyPenalty(state);
    }

    private void ApplyPenalty(
        KartTrackLimitsState state)
    {
        if (penaltyManager == null)
            return;

        KartBehaviour kart =
            state.Kart;

        if (kart.Physics == null)
            return;

        Rigidbody rb =
            kart.Physics.Rigidbody;

        if (rb == null)
            return;

        penaltyManager.AddPenalty(
            rb,
            penaltySeconds
        );

        state.penaltyApplied =
            true;
    }

    private void ResetState(
        KartTrackLimitsState state)
    {
        state.invalidSince = -1f;
        state.penaltyApplied = false;
    }

    public void ResetAllStates()
    {
        foreach (
            KeyValuePair<
                KartBehaviour,
                KartTrackLimitsState
            > pair in states)
        {
            ResetState(pair.Value);
        }
    }
}