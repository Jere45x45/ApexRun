using System;

public class RuntimeKartConfiguration
{
    public EngineData Engine { get; private set; }

    public ChassisData Chassis { get; private set; }

    public WheelData Wheels { get; private set; }

    public RuntimeKartConfiguration(KartConfiguration baseConfiguration)
    {
        if (baseConfiguration == null)
            throw new ArgumentNullException(nameof(baseConfiguration));

        Engine = baseConfiguration.engine;
        Chassis = baseConfiguration.chassis;
        Wheels = baseConfiguration.wheels;
    }

    public void InstallEngine(EngineData engine)
    {
        if (engine == null)
            throw new ArgumentNullException(nameof(engine));

        Engine = engine;
    }

    public void InstallChassis(ChassisData chassis)
    {
        if (chassis == null)
            throw new ArgumentNullException(nameof(chassis));

        Chassis = chassis;
    }

    public void InstallWheels(WheelData wheels)
    {
        if (wheels == null)
            throw new ArgumentNullException(nameof(wheels));

        Wheels = wheels;
    }

    public KartPart GetInstalledPart(PartType type)
    {
        switch (type)
        {
            case PartType.Engine:
                return Engine;

            case PartType.Chassis:
                return Chassis;

            case PartType.Wheels:
                return Wheels;

            default:
                return null;
        }
    }
}