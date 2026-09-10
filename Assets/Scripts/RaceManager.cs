using System;
using UnityEngine;

public class RaceManager : MonoBehaviour
{
    public enum RaceState
    {
        Waiting,
        Countdown,
        Racing,
        Finished
    }

    [Header("Race")]
    [SerializeField]
    [Min(0f)]
    private float countdownDuration = 3f;

    [SerializeField]
    private bool startAutomatically = true;

    [Header("Participants")]
    [SerializeField]
    private KartBehaviour[] raceKarts;

    [Header("Systems")]
    [SerializeField]
    private RaceCheckpointManager checkpointManager;

    [SerializeField]
    private RaceLapManager lapManager;

    private RaceState currentState =
        RaceState.Waiting;

    private float countdownTimer;

    private int finishedKarts;

    public RaceState CurrentState =>
        currentState;

    public float CountdownTimer =>
        countdownTimer;

    public event Action<RaceState>
        StateChanged;

    public event Action
        RaceStarted;

    public event Action
        RaceFinished;

    private void OnEnable()
    {
        if (lapManager != null)
        {
            lapManager.KartFinished +=
                HandleKartFinished;
        }
    }

    private void OnDisable()
    {
        if (lapManager != null)
        {
            lapManager.KartFinished -=
                HandleKartFinished;
        }
    }

    private void Start()
    {
        if (!ValidateReferences())
            return;

        RegisterParticipants();

        SetParticipantsInput(false);

        if (startAutomatically)
        {
            BeginCountdown();
        }
    }

    private void Update()
    {
        if (currentState !=
            RaceState.Countdown)
        {
            return;
        }

        countdownTimer -=
            Time.deltaTime;

        if (countdownTimer <= 0f)
        {
            StartRace();
        }
    }

    public void BeginCountdown()
    {
        if (currentState !=
            RaceState.Waiting)
        {
            return;
        }

        countdownTimer =
            countdownDuration;

        SetParticipantsInput(false);

        ChangeState(
            RaceState.Countdown
        );
    }

    public void StartRace()
    {
        if (currentState !=
            RaceState.Countdown)
        {
            return;
        }

        countdownTimer = 0f;

        SetParticipantsInput(true);

        ChangeState(
            RaceState.Racing
        );

        RaceStarted?.Invoke();
    }

    public void FinishRace()
    {
        if (currentState ==
            RaceState.Finished)
        {
            return;
        }

        SetParticipantsInput(false);

        ChangeState(
            RaceState.Finished
        );

        RaceFinished?.Invoke();
    }

    public bool IsWaiting() =>
        currentState ==
        RaceState.Waiting;

    public bool IsCountdown() =>
        currentState ==
        RaceState.Countdown;

    public bool IsRacing() =>
        currentState ==
        RaceState.Racing;

    public bool IsFinished() =>
        currentState ==
        RaceState.Finished;

    private void RegisterParticipants()
    {
        if (raceKarts == null)
            return;

        finishedKarts = 0;

        foreach (KartBehaviour kart in raceKarts)
        {
            if (kart == null)
                continue;

            Rigidbody rb =
                kart.GetComponent<Rigidbody>();

            if (rb == null)
            {
                Debug.LogError(
                    $"El kart {kart.name} no tiene Rigidbody.",
                    kart
                );

                continue;
            }

            lapManager.RegisterKart(
                rb
            );
        }
    }

    private void SetParticipantsInput(
        bool enabled)
    {
        if (raceKarts == null)
            return;

        foreach (KartBehaviour kart in raceKarts)
        {
            if (kart == null)
                continue;

            kart.SetInputEnabled(
                enabled
            );
        }
    }

    private void HandleKartFinished(
        Rigidbody kart)
    {
        if (kart == null)
            return;

        finishedKarts++;

        if (raceKarts == null ||
            raceKarts.Length == 0)
        {
            FinishRace();
            return;
        }

        if (finishedKarts >=
            GetValidKartCount())
        {
            FinishRace();
        }
    }

    private int GetValidKartCount()
    {
        if (raceKarts == null)
            return 0;

        int count = 0;

        foreach (KartBehaviour kart in raceKarts)
        {
            if (kart != null)
            {
                count++;
            }
        }

        return count;
    }

    private void ChangeState(
        RaceState newState)
    {
        if (currentState ==
            newState)
        {
            return;
        }

        currentState =
            newState;

        Debug.Log(
            $"Race State → {currentState}",
            this
        );

        StateChanged?.Invoke(
            currentState
        );
    }

    private bool ValidateReferences()
    {
        bool valid = true;

        if (checkpointManager == null)
        {
            Debug.LogError(
                "RaceManager necesita un RaceCheckpointManager.",
                this
            );

            valid = false;
        }

        if (lapManager == null)
        {
            Debug.LogError(
                "RaceManager necesita un RaceLapManager.",
                this
            );

            valid = false;
        }

        return valid;
    }
}