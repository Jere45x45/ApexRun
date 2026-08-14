using System;
using UnityEngine;

public class KartConfigurationController : MonoBehaviour
{
    [SerializeField]
    private KartConfiguration baseConfiguration;

    private RuntimeKartConfiguration runtimeConfiguration;

    public RuntimeKartConfiguration Configuration => runtimeConfiguration;

    public event Action ConfigurationChanged;

    private void Awake()
    {
        if (baseConfiguration == null)
        {
            Debug.LogError(
                "No hay una KartConfiguration asignada.",
                this
            );

            return;
        }

        runtimeConfiguration =
            new RuntimeKartConfiguration(baseConfiguration);
    }

    public void InstallPart(KartPart part)
    {
        if (part == null)
        {
            Debug.LogWarning(
                "Se intentó instalar una pieza nula.",
                this
            );

            return;
        }

        part.Install(runtimeConfiguration);

        ConfigurationChanged?.Invoke();
    }

    public KartPart GetInstalledPart(PartType type)
    {
        switch (type)
        {
            case PartType.Engine:
                return runtimeConfiguration.Engine;

            case PartType.Chassis:
                return runtimeConfiguration.Chassis;

            case PartType.Wheels:
                return runtimeConfiguration.Wheels;

            default:
                return null;
        }
    }
}