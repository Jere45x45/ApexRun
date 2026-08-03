using UnityEngine;

public class RuntimeKartConfiguration
{
    public EngineData Engine { get; private set; }
    public ChassisData Chassis { get; private set; }
    public WheelData Wheels { get; private set; }

    public RuntimeKartConfiguration(KartConfiguration baseConfiguration)
    {
        Engine = baseConfiguration.engine;
        Chassis = baseConfiguration.chassis;
        Wheels = baseConfiguration.wheels;
    }

    public void InstallEngine(EngineData engine)
    {
        Engine = engine;
    }

    public void InstallChassis(ChassisData chassis)
    {
        Chassis = chassis;
    }

    public void InstallWheels(WheelData wheels)
    {
        Wheels = wheels;
    }
}