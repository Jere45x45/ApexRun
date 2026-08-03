using UnityEngine;

public class PlayerInteraction : MonoBehaviour
{
    [SerializeField] private Transform interactionOrigin;
    [SerializeField] private float interactionDistance = 3f;
    [SerializeField] private Inventory inventory;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            TryInteract();
        }
    }

    private void TryInteract()
    {
        Ray ray = new Ray(
            interactionOrigin.position,
            interactionOrigin.forward);
    
        if (Physics.Raycast(ray, out RaycastHit hit, interactionDistance))
        {
            IInteractable interactable =
                hit.collider.GetComponent<IInteractable>();
    
            if (interactable != null)
            {
                KartPart part = interactable.Interact(this);
    
                if (part != null)
                {
                    inventory.AddPart(part);
                }
            }
        }
    }
}