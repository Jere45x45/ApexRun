using UnityEngine;
using System.Collections.Generic;
using TMPro;

public class CustomIABots : MonoBehaviour
{
    [Header("Kart")]
    [SerializeField] private BotBehaviour bot;
    [SerializeField] private KartRaySensor sensor;
    [SerializeField] private BotLearning learning;

    [Header("Track Progress")]
    [SerializeField] private Transform[] progressPoints;

    [Header("Rewards")]
    [SerializeField] private float progressReward = 2f;
    [SerializeField] private float finishReward = 20f;
    [SerializeField] private float fallPenalty = -5f;
    [SerializeField] private float movementReward = 0.05f;

    [Header("Learning")]
    [SerializeField] private float decisionInterval = 0.1f;

    private int currentProgress = 0;

    private float decisionTimer;

    private float[] lastObservation;
    private float[] currentObservation;

    private int lastAction;

    private Vector3 startPosition;
    private Quaternion startRotation;

    private Rigidbody rb;

    private void Awake()
    {
        if (bot == null)
            bot = GetComponent<BotBehaviour>();

        if (sensor == null)
            sensor = GetComponent<KartRaySensor>();

        if (learning == null)
            learning = BotLearning.Instance;

        rb = GetComponent<Rigidbody>();

        startPosition = transform.position;
        startRotation = transform.rotation;
    }

    private void FixedUpdate()
    {
        decisionTimer += Time.fixedDeltaTime;

        if (decisionTimer < decisionInterval)
            return;

        decisionTimer = 0f;

        currentObservation = sensor.GetTrackSensors();

        MakeDecision();

        CheckProgress();
    }

    private void MakeDecision()
    {
        int action = learning.ChooseAction(currentObservation);

        float steering = 0f;

        if (action == 0)
            steering = -1f;
        else if (action == 1)
            steering = 0f;
        else if (action == 2)
            steering = 1f;

        bot.SetInputs(
            1f,
            steering,
            false
        );

        if (lastObservation != null)
        {
            float reward = 0f;

            if (rb.linearVelocity.magnitude > 0.5f)
                reward += movementReward;

            learning.Learn(
                lastObservation,
                lastAction,
                reward,
                currentObservation
            );
        }

        lastObservation = (float[])currentObservation.Clone();
        lastAction = action;
    }

    private void CheckProgress()
    {
        if (progressPoints == null ||
            progressPoints.Length == 0)
            return;

        if (currentProgress >= progressPoints.Length)
            return;

        float distance = Vector3.Distance(
            transform.position,
            progressPoints[currentProgress].position
        );

        if (distance < 5f)
        {
            learning.Learn(
                lastObservation,
                lastAction,
                progressReward,
                currentObservation
            );

            currentProgress++;

            Debug.Log(
                "BOT PASÓ PROGRESO: " +
                currentProgress +
                "/" +
                progressPoints.Length
            );

            if (currentProgress >= progressPoints.Length)
            {
                learning.Learn(
                    lastObservation,
                    lastAction,
                    finishReward,
                    currentObservation
                );

                Debug.Log("BOT TERMINÓ LA PISTA");

                ResetBot();
            }
        }
    }

    public void FallOffTrack()
    {
        if (lastObservation != null)
        {
            learning.Learn(
                lastObservation,
                lastAction,
                fallPenalty,
                currentObservation
            );
        }

        Debug.Log("el bot se cayo");

        ResetBot();
    }

    private void ResetBot()
    {
        bot.SetInputs(0f, 0f, true);

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        transform.position = startPosition;
        transform.rotation = startRotation;

        currentProgress = 0;

        lastObservation = null;

        decisionTimer = 0f;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("FallZone"))
        {
            FallOffTrack();
        }
    }
}
