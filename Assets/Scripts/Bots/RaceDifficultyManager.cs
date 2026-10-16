
using UnityEngine;

public class RaceDifficultyManager : MonoBehaviour
{
    public void SetEasy()
    {
        SelectDifficulty(RaceDifficulty.Easy);
    }

    public void SetMedium()
    {
        SelectDifficulty(RaceDifficulty.Medium);
    }

    public void SetHard()
    {
        SelectDifficulty(RaceDifficulty.Hard);
    }

    private void SelectDifficulty(RaceDifficulty difficulty)
    {
        RaceDifficultySettings.Selected = difficulty;

        Debug.Log("Dificultad seleccionada: " + difficulty);
    }

    public void ApplyDifficultyToBots()
    {
        CustomIABots[] bots =
            FindObjectsByType<CustomIABots>(
                FindObjectsSortMode.None
            );

        foreach (CustomIABots bot in bots)
        {
            bot.SetDifficulty(
                (int)RaceDifficultySettings.Selected + 1
            );
        }

        Debug.Log(
            "Dificultad aplicada a " + bots.Length + " bots."
        );
    }
}
