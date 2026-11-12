using System;
using UnityEngine;

public class KartConfigurationController : MonoBehaviour
{
    [SerializeField]
    private KartConfiguration baseConfiguration;

    private RuntimeKartConfiguration runtimeConfiguration;

    public RuntimeKartConfiguration Configuration =>
        runtimeConfiguration;

    public event Action ConfigurationChanged;

    private void Awake()
    {
        InitializeRuntimeConfiguration();
    }

    private void InitializeRuntimeConfiguration()
    {
        if (baseConfiguration == null)
        {
            Debug.LogError(
                "No hay una KartConfiguration asignada.",
                this
            );

            runtimeConfiguration = null;
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

        if (runtimeConfiguration == null)
        {
            Debug.LogError(
                "No existe una configuración runtime válida.",
                this
            );

            return;
        }

        part.Install(runtimeConfiguration);

        ConfigurationChanged?.Invoke();
    }

    public void ResetToBaseConfiguration()
    {
        if (baseConfiguration == null)
        {
            Debug.LogError(
                "No se puede reiniciar la configuración porque no hay una KartConfiguration asignada.",
                this
            );

            return;
        }

        InitializeRuntimeConfiguration();

        ConfigurationChanged?.Invoke();
    }

    public KartPart GetInstalledPart(PartType type)
    {
        if (runtimeConfiguration == null)
        {
            Debug.LogError(
                "No existe una configuración runtime válida.",
                this
            );

            return null;
        }

        return runtimeConfiguration.GetInstalledPart(type);
    }
}