using UnityEngine;

/// <summary>
/// Con qué piezas corre un bot según su nivel: el fácil con las peores, el
/// medio con unas ni malas ni espectaculares y el difícil con las mejores.
/// Solo cambian cómo anda el kart (estadísticas). Lo que se ve lo elige
/// BotRandomizer al azar y no cambia las estadísticas.
/// Lo usan la carrera (NetworkKartSpawner) y el entrenamiento
/// (BotLevelParts en los bots de JuliVersionMINI), así los bots entrenan con
/// las mismas piezas con las que corren.
/// </summary>
[CreateAssetMenu(fileName = "BotPiezasPorNivel", menuName = "ApexRun/Piezas de los bots por nivel")]
public class BotPartsByLevel : ScriptableObject
{
    [Tooltip("Fácil: las peores piezas.")]
    [SerializeField] private KartPart[] easy = new KartPart[0];

    [Tooltip("Medio: ni malas ni espectaculares.")]
    [SerializeField] private KartPart[] medium = new KartPart[0];

    [Tooltip("Difícil: las mejores piezas.")]
    [SerializeField] private KartPart[] hard = new KartPart[0];

    public KartPart[] GetParts(RaceDifficulty difficulty)
    {
        switch (difficulty)
        {
            case RaceDifficulty.Easy: return easy;
            case RaceDifficulty.Hard: return hard;
            default: return medium;
        }
    }

    /// <summary>Instala las piezas del nivel en el kart (solo estadísticas).</summary>
    public void Install(KartConfigurationController configuration, RaceDifficulty difficulty)
    {
        if (configuration == null)
            return;

        foreach (KartPart part in GetParts(difficulty))
        {
            if (part != null)
                configuration.InstallPart(part);
        }
    }
}
