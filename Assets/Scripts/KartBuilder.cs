using UnityEngine;

public static class KartBuilder
{
    public static KartStats Build(KartConfiguration configuration)
    {
        KartStats stats = new KartStats();

        configuration.engine.Apply(stats);
        configuration.chassis.Apply(stats);
        configuration.wheels.Apply(stats);

        return stats;
    }
}