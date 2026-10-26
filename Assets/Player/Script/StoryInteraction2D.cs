using System;
using UnityEngine;

/// <summary>Overrides prototype interactions for the authored story sequence.</summary>
public sealed class StoryInteraction2D : MonoBehaviour
{
    public Action<GameObject> Activated;
    public void Interact(GameObject player) => Activated?.Invoke(player);
}
