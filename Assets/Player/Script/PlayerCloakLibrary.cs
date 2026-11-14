using System.Collections.Generic;
using UnityEngine;

public sealed class PlayerCloakLibrary : ScriptableObject
{
    public RuntimeAnimatorController controller;
    public Sprite[] bodySprites;
    public Sprite[] cloakSprites;
    public Sprite authoredBodyReference;
    public Sprite animatedBodyReference;
    // The scene's trimmed standing sprite and the animation's full canvas have
    // different origins, even though both use a centered pivot and the same PPU.
    public Vector2 AnimationOffset => authoredBodyReference != null && animatedBodyReference != null
        ? (authoredBodyReference.rect.position + authoredBodyReference.pivot) / authoredBodyReference.pixelsPerUnit
          - (animatedBodyReference.rect.position + animatedBodyReference.pivot) / animatedBodyReference.pixelsPerUnit
        : Vector2.zero;
    private Dictionary<Sprite, Sprite> frames;
    public Sprite Frame(Sprite body)
    {
        if (body == null) return null;
        if (frames == null)
        {
            frames = new();
            for (int i = 0; i < bodySprites.Length; i++) frames[bodySprites[i]] = cloakSprites[i];
        }
        return frames.TryGetValue(body, out var sprite) ? sprite : null;
    }
}
