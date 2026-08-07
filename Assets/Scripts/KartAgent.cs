using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class KartAgent : Agent
{
     [SerializeField] private KartBehaviour kart;

    private int nextCheckpoint = 0;

    public override void OnEpisodeBegin()
    {
        nextCheckpoint = 0;
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

    public void ReachCheckpoint(int checkpointIndex)
    {
        if (checkpointIndex == nextCheckpoint)
        {
            AddReward(1f);

            nextCheckpoint++;

            if (nextCheckpoint >= 8)
            {
                AddReward(10f);
                EndEpisode();
            }
        }
    }
}
