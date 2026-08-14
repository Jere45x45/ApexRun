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

    [Header("Settings")]
    [SerializeField] private float maxSpeed = 30f;

    private Rigidbody rb;

    private int nextCheckpoint = 0;

    private Vector3 startPosition;
    private Quaternion startRotation;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        startPosition = transform.position;
        startRotation = transform.rotation;
    }

    public override void OnEpisodeBegin()
    {
        nextCheckpoint = 0;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        transform.position = startPosition;
        transform.rotation = startRotation;

        kart.SetInputs(0f, 0f, false);
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (checkpoints.Length == 0)
        {
            return;
        }

        Transform target = checkpoints[nextCheckpoint];

        Vector3 directionToTarget = target.position - transform.position;

        Vector3 localDirection = transform.InverseTransformDirection(
            directionToTarget.normalized
        );

        sensor.AddObservation(localDirection.x);
        sensor.AddObservation(localDirection.z);

        float speed = rb.linearVelocity.magnitude / maxSpeed;
        sensor.AddObservation(speed);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        float steering = Mathf.Clamp(
            actions.ContinuousActions[0],
            -1f,
            1f
        );

        float throttle = Mathf.Clamp(
            actions.ContinuousActions[1],
         -1f,
         1f
        );

        kart.SetInputs(throttle, steering, false);

        AddReward(-0.001f);
    }
    
    public override void Heuristic(in ActionBuffers actionsOut)
    {
    var actions = actionsOut.ContinuousActions;

    actions[0] = Input.GetAxis("Horizontal");
    actions[1] = Input.GetAxis("Vertical");
    }

    public void ReachCheckpoint(int checkpointIndex)
    {
        if (checkpointIndex == nextCheckpoint)
        {
            AddReward(1f);

            nextCheckpoint++;

            if (nextCheckpoint >= checkpoints.Length)
            {
                AddReward(10f);
                EndEpisode();
            }
        }
    }
    private void OnTriggerEnter(Collider other)
    {
    if (other.CompareTag("FallZone"))
    {
        FallOffTrack();
    }
    }

    public void FallOffTrack()
    {
    AddReward(-2f);
    EndEpisode();
    }
}