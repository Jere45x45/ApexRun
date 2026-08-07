using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class KartAgent : Agent
{
    [SerializeField] private KartBehaviour kart;

    public override void OnEpisodeBegin()
    {
    }

    public override void CollectObservations(VectorSensor sensor)
    {
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        float steering = actions.ContinuousActions[0];
        float throttle = actions.ContinuousActions[1];

        kart.SetInputs(throttle, steering, false);
    }
}
