using System;

public class RuntimeKartConfiguration
{
    public EngineData Engine { get; private set; }

    public ChassisData Chassis { get; private set; }

    public WheelData Wheels { get; private set; }

    public AeroKitData AeroKit { get; private set; }

    public SteeringWheelData SteeringWheel { get; private set; }

    public RuntimeKartConfiguration(KartConfiguration baseConfiguration)
    {
        if (baseConfiguration == null)
            throw new ArgumentNullException(nameof(baseConfiguration));

        Engine = baseConfiguration.engine;
        Chassis = baseConfiguration.chassis;
        Wheels = baseConfiguration.wheels;
        AeroKit = baseConfiguration.aeroKit;
        SteeringWheel = baseConfiguration.steeringWheel;
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

    public void InstallAeroKit(AeroKitData aeroKit)
    {
        if (aeroKit == null)
            throw new ArgumentNullException(nameof(aeroKit));

        AeroKit = aeroKit;
    }

    public void InstallSteeringWheel(SteeringWheelData steeringWheel)
    {
        if (steeringWheel == null)
            throw new ArgumentNullException(nameof(steeringWheel));

        SteeringWheel = steeringWheel;
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

            case PartType.AeroKit:
                return AeroKit;

            case PartType.SteeringWheel:
                return SteeringWheel;

            default:
                return null;
        }
    }
}
