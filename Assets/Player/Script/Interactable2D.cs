using UnityEngine;
using UnityEngine.Events;

/// <summary>Add this to an object that the player can activate with F.</summary>
public sealed class Interactable2D : MonoBehaviour
{
    [SerializeField] private UnityEvent onInteract;

    public void Interact(GameObject interactor)
    {
        onInteract?.Invoke();

        ScenePortal2D portal = GetComponent<ScenePortal2D>();
        if (portal != null)
        {
            portal.Travel(interactor);
            return;
        }

        QuestGiver2D questGiver = GetComponent<QuestGiver2D>();
        if (questGiver != null)
        {
            questGiver.Interact();
            return;
        }

        // A component placed on this same scene object needs no fragile UnityEvent setup.
        // This is intentionally not an object lookup or runtime creation.
        GetComponent<CutsceneVideoSequence>()?.PlayFromInteraction();
        GetComponent<ConversationDialogue>()?.Play();
    }
}
