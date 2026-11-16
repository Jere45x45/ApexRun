using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Límites de pista: con minimumInvalidWheelsForPenalty (3) ruedas fuera
/// (pasto, grava...) durante requiredStayDuration, penalización. Una rueda que
/// toca el piano o el asfalto cuenta como adentro, aunque también roce el pasto
/// (KartWheel.TouchesTrack). Una rueda en el aire cuenta según lo último que
/// tocó (KartWheel.LastContactInvalid): rebotando en la grava sigue afuera, y
/// si saltó desde el piano sigue adentro.
/// - Rebotando (por ejemplo en la grava con relieve) las ruedas en el aire
///   cuentan como afuera si alguna toca fuera o si ya estaba afuera. Y un
///   instante sin tocar nada (hasta outsideGrace) no corta la cuenta.
/// </summary>
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

    [Tooltip("Cuánto tiene que estar adentro (todas las ruedas en la pista) para que se corte la cuenta (s). Así un rebote no la reinicia.")]
    [SerializeField]
    [Min(0f)]
    private float outsideGrace = 0.3f;

    private class KartTrackLimitsState
    {
        public KartBehaviour Kart;
        public float invalidSince = -1f;
        public float lastOutside = -1f;
        public bool outside;
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

        KartVehicle vehicle =
            kart.Vehicle;

        if (vehicle == null)
            return 0;

        int invalidWheelCount = 0;

        foreach (
            KartWheel wheel
            in vehicle.Wheels)
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
        // Solo se juzga mientras el piloto maneja: ni en la cuenta regresiva
        // ni después de cruzar la meta.
        if (kart == null || !kart.InputEnabled)
            return false;

        if (states.TryGetValue(kart, out KartTrackLimitsState state))
            return state.outside;

        return IsOutsideNow(kart, false);
    }

    /// <summary>
    /// Afuera en este instante: las ruedas que tocan fuera, más las que van en
    /// el aire si alguna toca fuera o si ya estaba afuera (rebotando).
    /// </summary>
    private bool IsOutsideNow(
        KartBehaviour kart,
        bool wasOutside)
    {
        if (kart == null || !kart.InputEnabled || kart.Vehicle == null)
            return false;

        int invalid = 0;
        bool anyGrounded = false;

        foreach (KartWheel wheel in kart.Vehicle.Wheels)
        {
            if (wheel == null)
                continue;

            if (wheel.IsGrounded)
            {
                anyGrounded = true;

                if (wheel.IsOnInvalidSurface)
                    invalid++;
            }
            else if (wheel.LastContactInvalid)
            {
                invalid++;
            }
        }

        // Todo el kart en el aire (un salto): sigue como estaba.
        if (!anyGrounded)
            return wasOutside;

        return invalid >= minimumInvalidWheelsForPenalty;
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

        if (!kart.InputEnabled)
        {
            ResetState(state);
            return;
        }

        if (IsOutsideNow(kart, state.outside))
        {
            state.lastOutside = Time.time;
            state.outside = true;
        }
        else if (state.outside && Time.time - state.lastOutside > outsideGrace)
        {
            // Volvió a la pista (y no fue un rebote).
            ResetState(state);
            return;
        }

        if (!state.outside)
            return;

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

        if (kart.Vehicle == null)
            return;

        Rigidbody rb =
            kart.Vehicle.Body;

        if (rb == null)
            return;

        // Se mide en la computadora que simula el kart; la aplica el servidor.
        penaltyManager.RequestPenalty(
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
        state.lastOutside = -1f;
        state.outside = false;
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
