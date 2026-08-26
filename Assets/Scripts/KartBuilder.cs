using System;

public static class KartBuilder
{
    public static KartStats Build(RuntimeKartConfiguration configuration)
    {
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        if (configuration.Engine == null)
            throw new InvalidOperationException(
                "El kart no tiene un motor instalado."
            );

        if (configuration.Chassis == null)
            throw new InvalidOperationException(
                "El kart no tiene un chasis instalado."
            );

        if (configuration.Wheels == null)
            throw new InvalidOperationException(
                "El kart no tiene ruedas instaladas."
            );

        KartStats stats = new KartStats();

        configuration.Engine.Apply(stats);
        configuration.Chassis.Apply(stats);
        configuration.Wheels.Apply(stats);

        if (configuration.AeroKit != null)
        {
            configuration.AeroKit.Apply(stats);
        }

        return stats;
    }
}