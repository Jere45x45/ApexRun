using UnityEngine;

public static class KartBuilder
{
    public static KartStats Build(RuntimeKartConfiguration configuration)
    {
        KartStats stats = new KartStats();

        configuration.Engine.Apply(stats);
        configuration.Chassis.Apply(stats);
        configuration.Wheels.Apply(stats);

        return stats;
    }
}