using UnityEngine;

public class RaceStarter : MonoBehaviour
{
    [SerializeField] private KartAgent[] bots;
    [SerializeField] private GameObject racePanel;
    [SerializeField] private GameObject ComenzarBotton;

    public void StartRace()
    {
        foreach (KartAgent bot in bots)
        {
            if (bot != null)
            {
                bot.StartRace();
            }
        }

        if (racePanel != null)
        {
            racePanel.SetActive(false);
        }
        
        if (ComenzarBotton != null)
        {
            ComenzarBotton.SetActive(false);
        }
    }
}
