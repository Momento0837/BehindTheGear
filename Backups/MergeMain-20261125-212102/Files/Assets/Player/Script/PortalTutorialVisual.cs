using UnityEngine;

// Portal-only visual. NPC prompts continue using the separate F animation.
public sealed class PortalTutorialVisual : MonoBehaviour
{
    [SerializeField] private string animationResource = "W";
    [SerializeField, Min(1)] private float displayWidth = 59.4f;
    [Tooltip("Optional replacement sprites in playback order. Leave empty to use W.gif's generated atlas.")]
    [SerializeField] private Sprite[] replacementFrames;
    [SerializeField, Min(.01f)] private float secondsPerFrame = .08f;
    public float DisplayWidth => displayWidth;
    public ControlHintAnimation Animation => ControlHintAnimation.Load(animationResource);
    public Sprite Frame(float time) => replacementFrames != null && replacementFrames.Length > 0
        ? replacementFrames[Mathf.FloorToInt(Mathf.Max(0, time) * 1.32f / secondsPerFrame) % replacementFrames.Length]
        : Animation.Frame(time);
    public Vector2 Size => replacementFrames != null && replacementFrames.Length > 0 && replacementFrames[0] != null
        ? replacementFrames[0].rect.size : Animation.Size;
}
