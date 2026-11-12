using UnityEngine;

/// <summary>
/// Para los bots del entrenamiento (JuliVersionMINI): les instala las piezas
/// de su nivel (BotPartsByLevel), así entrenan con las mismas estadísticas
/// con las que corren en la carrera. Si se les cambia el nivel (botones de
/// la escena), cambia las piezas. Lo que se ve lo sigue eligiendo
/// BotRandomizer al azar.
/// En la carrera no hace falta: ahí las pone NetworkKartSpawner.
/// </summary>
[RequireComponent(typeof(CustomIABots))]
public class BotLevelParts : MonoBehaviour
{
    [SerializeField] private BotPartsByLevel parts;

    private CustomIABots bot;
    private KartConfigurationController configuration;
    private RaceDifficulty installed;
    private bool hasInstalled;

    private void Awake()
    {
        bot = GetComponent<CustomIABots>();
        configuration = GetComponentInChildren<KartConfigurationController>(true);
    }

    private void Start()
    {
        Apply();
    }

    private void Update()
    {
        if (!hasInstalled || bot.Difficulty != installed)
            Apply();
    }

    private void Apply()
    {
        if (parts == null || configuration == null || configuration.Configuration == null)
            return;

        installed = bot.Difficulty;
        hasInstalled = true;
        parts.Install(configuration, installed);
    }
}
