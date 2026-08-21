using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class KartAgent : Agent
{
    [Header("Kart")]
    [SerializeField] private BotBehaviour bot;

    [Header("Checkpoints")]
    [SerializeField] private Transform[] checkpoints;

    [Header("Settings")]
    [SerializeField] private float maxSpeed = 30f;

    [Header("Rewards")]
    [SerializeField] private float checkpointReward = 1f;
    [SerializeField] private float finishReward = 10f;
    [SerializeField] private float fallPenalty = -2f;
    [SerializeField] private float timePenalty = -0.001f;

    [SerializeField] private float stuckTime = 3f;
    [SerializeField] private float minSpeedToConsiderMoving = 0.5f;

    private float stuckTimer = 0f;
    private Vector3 lastPosition;

    private int nextCheckpoint = 0;

    private Vector3 startPosition;
    private Quaternion startRotation;
    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        startPosition = transform.position;
        startRotation = transform.rotation;
        
        lastPosition = transform.position;
    }

    public override void OnEpisodeBegin()
    {
        nextCheckpoint = 0;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        transform.position = startPosition;
        transform.rotation = startRotation;

        bot.SetInputs(0f, 0f, false);

       stuckTimer = 0f;
       lastPosition = transform.position;
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

        bot.SetInputs(throttle, steering, false);

        AddReward(timePenalty);
    }
    
    public override void Heuristic(in ActionBuffers actionsOut)
    {
    var actions = actionsOut.ContinuousActions;

    actions[0] = Input.GetAxis("Horizontal");
    actions[1] = Input.GetAxis("Vertical");
    }

    public void ReachCheckpoint(int checkpointIndex)
    {
        if (checkpointIndex != nextCheckpoint)
        {
            return;
        }

     AddReward(checkpointReward);

        nextCheckpoint++;

        if (nextCheckpoint >= checkpoints.Length)
        {
            AddReward(finishReward);
            EndEpisode();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("FallZone"))
        {
            FallOffTrack();
            Debug.Log("se callo");
        }
    }

    private void FixedUpdate()
    {
        float movementSpeed =
        Vector3.Distance(transform.position, lastPosition)
        / Time.fixedDeltaTime;

        if (movementSpeed < minSpeedToConsiderMoving)
        {
            stuckTimer += Time.fixedDeltaTime;
        }
        else
        {
            stuckTimer = 0f;
        }

     lastPosition = transform.position;

        if (stuckTimer >= stuckTime)
        {
            FallOffTrack();
        }
    }
  
    public void FallOffTrack()
    {
        AddReward(fallPenalty);
        EndEpisode();
    }
}