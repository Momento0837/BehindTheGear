using UnityEngine;

// Keeps the editable UI prefabs outside Resources while including them in player builds.
[CreateAssetMenu(menuName = "Behind the Gear/UI/Dialogue Canvas Prefabs")]
public sealed class DialogueCanvasPrefabs : ScriptableObject
{
    public GameObject storyCanvas;
    public GameObject treatmentCanvas;
}
