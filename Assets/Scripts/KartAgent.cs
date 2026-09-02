using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class KartAgent : Agent
{
    [Header("Kart")]
    [SerializeField] private KartBehaviour kart;

    [Header("Checkpoints")]
    [SerializeField] private Transform[] checkpoints;

    private Rigidbody rb;

    private int nextCheckpoint;

    private Vector3 startPosition;
    private Quaternion startRotation;

    private void Start()
    {
        if (kart == null)
        {
            Debug.LogError(
                "KartAgent no tiene un KartBehaviour asignado.",
                this
            );

            return;
        }

        rb = GetComponent<Rigidbody>();

        if (rb == null)
        {
            Debug.LogError(
                "KartAgent necesita un Rigidbody en el mismo GameObject.",
                this
            );

            return;
        }

        startPosition = transform.position;
        startRotation = transform.rotation;

        nextCheckpoint = 0;
    }

    public override void OnEpisodeBegin()
    {
        if (kart == null || rb == null)
            return;

        nextCheckpoint = 0;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        transform.position = startPosition;
        transform.rotation = startRotation;

        kart.SetInputs(
            0f,
            0f,
            true
        );
    }

    public override void CollectObservations(
        VectorSensor sensor)
    {
        if (kart == null ||
            rb == null ||
            kart.Kart == null)
        {
            return;
        }

        if (checkpoints == null ||
            checkpoints.Length == 0)
        {
            return;
        }

        if (nextCheckpoint >= checkpoints.Length)
        {
            return;
        }

        Transform target =
            checkpoints[nextCheckpoint];

        if (target == null)
            return;

        Vector3 directionToTarget =
            target.position -
            transform.position;

        Vector3 localDirection =
            transform.InverseTransformDirection(
                directionToTarget.normalized
            );

        sensor.AddObservation(
            localDirection.x
        );

        sensor.AddObservation(
            localDirection.z
        );

        float maxSpeed =
            kart.Kart.Stats.maxSpeed;

        float speed = 0f;

        if (maxSpeed > 0f)
        {
            speed =
                rb.linearVelocity.magnitude /
                maxSpeed;
        }

        sensor.AddObservation(speed);
    }

    public override void OnActionReceived(
        ActionBuffers actions)
    {
        if (kart == null)
            return;

        float steering =
            Mathf.Clamp(
                actions.ContinuousActions[0],
                -1f,
                1f
            );

        float throttle =
            Mathf.Clamp(
                actions.ContinuousActions[1],
                -1f,
                1f
            );

        kart.SetInputs(
            throttle,
            steering,
            false
        );

        AddReward(-0.001f);
    }

    public void ReachCheckpoint(
        int checkpointIndex)
    {
        if (checkpointIndex != nextCheckpoint)
            return;

        AddReward(1f);

        nextCheckpoint++;

        if (nextCheckpoint >= checkpoints.Length)
        {
            AddReward(10f);
            EndEpisode();
        }
    }

    public void FallOffTrack()
    {
        AddReward(-2f);

        EndEpisode();
    }
}