using System;

[Serializable]
public class BotExperience
{
    public float[] observations;

    public float steering;
    public float throttle;
    public bool brake;

    public float reward;

    public BotExperience(
        float[] observations,
        float steering,
        float throttle,
        bool brake,
        float reward)
    {
        this.observations = observations;
        this.steering = steering;
        this.throttle = throttle;
        this.brake = brake;
        this.reward = reward;
    }
}
