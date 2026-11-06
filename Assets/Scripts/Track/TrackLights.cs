using UnityEngine;

/// <summary>
/// Postes de luz de la pista (LucesPista): de noche se prenden los focos
/// (Spot Lights) y brillan los LED del poste; de día quedan apagados.
/// Lee la hora de TrackConditions, que la decide el servidor, así todos los
/// jugadores ven lo mismo. Fuera de una carrera quedan apagados.
/// Va en el objeto padre de los postes. Los focos tienen que arrancar apagados.
/// </summary>
public class TrackLights : MonoBehaviour
{
    [Tooltip("Los focos de los postes. Vacío: se buscan entre los hijos al empezar.")]
    [SerializeField] private Light[] lights = new Light[0];

    [Tooltip("Los modelos de los postes (LucesPista). Vacío: se buscan entre los hijos al empezar.")]
    [SerializeField] private Renderer[] poles = new Renderer[0];

    [Tooltip("Índice del material de los LED en el modelo del poste (0 = poste, 1 = luces).")]
    [SerializeField, Min(0)] private int ledMaterialIndex = 1;

    [Tooltip("Color del brillo de los LED prendidos (HDR: más de 1 brilla más).")]
    [SerializeField, ColorUsage(false, true)] private Color ledEmission = new Color(2.2f, 2.1f, 1.9f);

    [Header("Cuándo se prenden")]
    [SerializeField] private bool onAtNight = true;
    [SerializeField] private bool onAtSunset = true;

    [Tooltip("Intensidad de los focos al atardecer, en veces la de la noche.")]
    [SerializeField, Range(0f, 1f)] private float sunsetIntensity = 0.6f;

    [Tooltip("Apagar los focos de verdad y dejar solo los LED (por si la PC anda lenta de noche).")]
    [SerializeField] private bool ledsOnly;

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private float[] nightIntensities = new float[0];
    private MaterialPropertyBlock block;
    private float currentLevel = -1f;

    private void Awake()
    {
        if (lights == null || lights.Length == 0)
            lights = GetComponentsInChildren<Light>(true);

        if (poles == null || poles.Length == 0)
            poles = GetComponentsInChildren<MeshRenderer>(true);

        nightIntensities = new float[lights.Length];

        for (int i = 0; i < lights.Length; i++)
            nightIntensities[i] = lights[i] != null ? lights[i].intensity : 0f;

        block = new MaterialPropertyBlock();
        Apply(0f);
    }

    private void Update()
    {
        float level = TargetLevel();

        if (!Mathf.Approximately(level, currentLevel))
            Apply(level);
    }

    /// <summary>0 = apagadas, 1 = de noche; al atardecer, sunsetIntensity.</summary>
    private float TargetLevel()
    {
        if (!TrackConditions.Active)
            return 0f;

        if (onAtNight && TrackConditions.TimeOfDay == TimeOfDay.Night)
            return 1f;

        if (onAtSunset && TrackConditions.TimeOfDay == TimeOfDay.Sunset)
            return sunsetIntensity;

        return 0f;
    }

    private void Apply(float level)
    {
        currentLevel = level;
        bool on = level > 0f;

        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] == null)
                continue;

            lights[i].enabled = on && !ledsOnly;
            lights[i].intensity = nightIntensities[i] * level;
        }

        // Los LED brillan aunque los focos estén apagados (ledsOnly).
        block.SetColor(EmissionColorId, on ? ledEmission * Mathf.Max(0.5f, level) : Color.black);

        foreach (Renderer pole in poles)
        {
            if (pole != null && ledMaterialIndex < pole.sharedMaterials.Length)
                pole.SetPropertyBlock(block, ledMaterialIndex);
        }
    }
}
