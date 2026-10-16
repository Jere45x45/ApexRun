using UnityEngine;
   
public enum RaceDifficulty
{
    Easy,
    Medium,
    Hard
}

public static class RaceDifficultySettings
{
    public static RaceDifficulty Selected = RaceDifficulty.Medium;
}
