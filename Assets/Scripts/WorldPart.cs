using UnityEngine;

public class WorldPart : MonoBehaviour, IInteractable
{
    [SerializeField] private KartPart part;

    public KartPart Part => part;

    public KartPart Interact(PlayerInteraction player)
    {
        Destroy(gameObject);

        return part;
    }
}