using UnityEngine;

public enum RaceDifficulty
{
    Easy,
    Medium,
    Hard
}

/// <summary>Nivel de los bots elegido en el menú (queda guardado en RaceSetupStore).</summary>
public static class RaceDifficultySettings
{
    public static RaceDifficulty Selected
    {
        get => RaceSetupStore.BotDifficulty;
        set => RaceSetupStore.BotDifficulty = value;
    }

    /// <summary>Cómo se muestra cada nivel.</summary>
    public static string Label(RaceDifficulty difficulty)
    {
        switch (difficulty)
        {
            case RaceDifficulty.Easy: return "FÁCIL";
            case RaceDifficulty.Hard: return "DIFÍCIL";
            default: return "MEDIO";
        }
    }
}
