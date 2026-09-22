using UnityEngine;
using UnityEngine.Events;

/// <summary>Add this to an object that the player can activate with F.</summary>
public sealed class Interactable2D : MonoBehaviour
{
    [SerializeField] private UnityEvent onInteract;

    public void Interact(GameObject interactor)
    {
        onInteract?.Invoke();
    }
}
