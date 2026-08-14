public class RuntimeKartConfiguration
{
    public EngineData Engine { get; private set; }
    public ChassisData Chassis { get; private set; }
    public WheelData Wheels { get; private set; }

    public RuntimeKartConfiguration(KartConfiguration baseConfiguration)
    {
        if (baseConfiguration == null)
            throw new System.ArgumentNullException(nameof(baseConfiguration));

        Engine = baseConfiguration.engine;
        Chassis = baseConfiguration.chassis;
        Wheels = baseConfiguration.wheels;
    }

    public void InstallEngine(EngineData engine)
    {
        if (engine == null)
            throw new System.ArgumentNullException(nameof(engine));

        Engine = engine;
    }

    public void InstallChassis(ChassisData chassis)
    {
        if (chassis == null)
            throw new System.ArgumentNullException(nameof(chassis));

        Chassis = chassis;
    }

    public void InstallWheels(WheelData wheels)
    {
        if (wheels == null)
            throw new System.ArgumentNullException(nameof(wheels));

        Wheels = wheels;
    }
}