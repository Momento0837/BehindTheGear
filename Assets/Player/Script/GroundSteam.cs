using UnityEngine;

// Retired ground effect. Removes any transient strip left in a running scene after scripts reload.
// No scene-load hook remains, so new scenes never spawn this effect.
public sealed class GroundSteam : MonoBehaviour
{
    private void OnEnable()
    {
        foreach (var renderer in GetComponents<Renderer>()) renderer.enabled = false;
        Destroy(gameObject);
    }
}
