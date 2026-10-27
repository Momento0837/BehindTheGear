using UnityEngine;

/// <summary>Compatibility component for existing scenes. HP is displayed by InGameVitalsHUD.</summary>
[RequireComponent(typeof(PlayerHealth))]
public sealed class PlayerHealthHUD : MonoBehaviour
{
    public GameObject CanvasRoot => null;
    public void PreserveHud() { }
}
