using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.Serialization;

public class KartAgent : Agent
{
    [Header("Bot")]
    [SerializeField] private BotBehaviour bot;
    private KartRaySensor raySensor;

    [Header("Random Parts")]
    [SerializeField] private BotRandomizer randomizer;

    [Header("Track Progress")]
    [FormerlySerializedAs("checkpoints")]
    [SerializeField] private Transform[] progressPoints;

    [SerializeField] private float progressReward = 2f;
    [SerializeField] private float finishReward = 20f;

    [Header("Settings")]
    [SerializeField] private float maxSpeed = 30f;

    [Header("Rewards")]
    [SerializeField] private float fallPenalty = -5f;
    [SerializeField] private float timePenalty = -0.001f;

    [Header("Stuck Detection")]
    [SerializeField] private float stuckTime = 3f;
    [SerializeField] private float minSpeedToConsiderMoving = 0.5f;
    private bool isStopped = false; 
    private int PuntoDeProgresoActual = 0;

    private float stuckTimer = 0f;
    private Vector3 lastPosition;

    private Vector3 startPosition;
    private Quaternion startRotation;

    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        raySensor = GetComponent<KartRaySensor>();

        startPosition = transform.position;
        startRotation = transform.rotation;

        lastPosition = transform.position;
    }

    public override void OnEpisodeBegin()
    {
        PuntoDeProgresoActual = 0;
        isStopped = false;
        PuntoDeProgresoActual = 0;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        transform.position = startPosition;
        transform.rotation = startRotation;

        bot.SetInputs(0f, 0f, false);

        stuckTimer = 0f;
        lastPosition = transform.position;

        if (randomizer != null)
        {
            randomizer.RandomizeKart();
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        float[] trackSensors = raySensor.GetTrackSensors();

        for (int i = 0; i < trackSensors.Length; i++)
        {
            sensor.AddObservation(trackSensors[i]);
        }

        float speed = rb.linearVelocity.magnitude / maxSpeed;
        speed = Mathf.Clamp01(speed);

        sensor.AddObservation(speed);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        
        if (IsWriting() || isStopped)
        {
            bot.SetInputs(0f, 0f, true); // Enviamos freno de mano activo (true)
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            return;
        }

        float steering = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
        float throttle = Mathf.Clamp01(actions.ContinuousActions[1]);
        float brake = Mathf.Clamp01(actions.ContinuousActions[2]);

        bot.SetInputs(throttle, steering, brake > 0.5f);

        float forwardSpeed = Vector3.Dot(
            rb.linearVelocity,
            transform.forward
        );

        if (forwardSpeed > 0f)
        {
            AddReward(forwardSpeed * 0.001f);
        }

        AddReward(timePenalty);

        CheckProgress();
    }

    public void StopAgent()
{
    isStopped = true;
    bot.SetInputs(0f, 0f, true);
    rb.linearVelocity = Vector3.zero;
    rb.angularVelocity = Vector3.zero;
}

public void ResumeAgent()
{
    isStopped = false;
}

    private void CheckProgress()
    {
        if (progressPoints == null || progressPoints.Length == 0)
            return;

        int closestPoint = PuntoDeProgresoActual;

        float closestDistance = Vector3.Distance(
            transform.position,
            progressPoints[PuntoDeProgresoActual].position
        );

        int maxPointToCheck = Mathf.Min(
            PuntoDeProgresoActual + 3,
            progressPoints.Length - 1
        );

        for (int i = PuntoDeProgresoActual + 1; i <= maxPointToCheck; i++)
        {
            float distance = Vector3.Distance(
                transform.position,
                progressPoints[i].position
            );

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestPoint = i;
            }
        }

        if (closestPoint > PuntoDeProgresoActual)
        {
            int pointsPassed = closestPoint - PuntoDeProgresoActual;

            AddReward(progressReward * pointsPassed);

            PuntoDeProgresoActual = closestPoint;

            // Llegó al último punto
            if (PuntoDeProgresoActual >= progressPoints.Length - 1)
            {
                AddReward(finishReward);
                EndEpisode();
            }
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var actions = actionsOut.ContinuousActions;

        actions[0] = Input.GetAxis("Horizontal"); // dirección
        actions[1] = Input.GetAxis("Vertical");   // acelerar

        actions[2] = Input.GetKey(KeyCode.Space) ? 1f : 0f; // freno
    }

    private void OnTriggerEnter(Collider other)
    {
         if (other.CompareTag("Finish"))
    {
        AddReward(finishReward); // Usamos tu variable finishReward configurada en el inspector
        
        // Activamos el freno total que creamos antes
        StopAgent(); 

        // OPCIONAL: Si quieres que el episodio termine un par de segundos después de frenar,
        // puedes usar una Corrutina. Si prefieres que se quede frenado para siempre, borra la línea de abajo.
        StartCoroutine(EndEpisodeDelayed(2f)); 
    }

    if (other.CompareTag("FallZone"))
    {
        FallOffTrack();
    }
    }

    private void FixedUpdate()
    {
        float movementSpeed =
            Vector3.Distance(
                transform.position,
                lastPosition
            ) / Time.fixedDeltaTime;

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

    private bool IsWriting()
    {
        return EventSystem.current != null &&
               EventSystem.current.currentSelectedGameObject != null &&
               EventSystem.current.currentSelectedGameObject
                   .GetComponent<TMP_InputField>() != null;
    }
    private System.Collections.IEnumerator EndEpisodeDelayed(float delay)
{
    yield return new WaitForSeconds(delay);
    EndEpisode();
}
}