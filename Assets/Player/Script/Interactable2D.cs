using UnityEngine;
using UnityEngine.Events;

/// <summary>Add this to an object that the player can activate with F.</summary>
public sealed class Interactable2D : MonoBehaviour
{
    [SerializeField] private UnityEvent onInteract;

    public void Interact(GameObject interactor)
    {
        onInteract?.Invoke();

        // A component placed on this same scene object needs no fragile UnityEvent setup.
        // This is intentionally not an object lookup or runtime creation.
        GetComponent<CutsceneVideoSequence>()?.PlayFromInteraction();
        GetComponent<ConversationDialogue>()?.Play();
    }
}
