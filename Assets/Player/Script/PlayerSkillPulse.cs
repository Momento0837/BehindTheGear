using UnityEngine;

/// <summary>Small runtime ring effect; does not change player materials or require imported art.</summary>
public sealed class PlayerSkillPulse : MonoBehaviour
{
    private SpriteRenderer ring;
    private Texture2D texture;
    private Sprite sprite;
    private float radius, elapsed;
    private Color tint;
    private const float Duration = .4f;

    public static void Create(Vector3 center, float radius, Color tint, SpriteRenderer source)
    {
        var effect = new GameObject("Player Skill Pulse").AddComponent<PlayerSkillPulse>();
        effect.transform.position = center;
        effect.radius = radius;
        effect.tint = tint;
        effect.ring = effect.gameObject.AddComponent<SpriteRenderer>();
        if (source != null)
        {
            effect.ring.sharedMaterial = source.sharedMaterial;
            effect.ring.sortingLayerID = source.sortingLayerID;
            effect.ring.sortingOrder = source.sortingOrder + 1;
        }
        const int size = 64;
        effect.texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float distance = Vector2.Distance(new Vector2(x + .5f, y + .5f), Vector2.one * (size * .5f)) / (size * .5f);
            byte alpha = (byte)(Mathf.Clamp01(1f - Mathf.Abs(distance - .88f) / .07f) * 255);
            pixels[y * size + x] = new Color32(255, 255, 255, alpha);
        }
        effect.texture.SetPixels32(pixels); effect.texture.Apply();
        effect.sprite = Sprite.Create(effect.texture, new Rect(0, 0, size, size), Vector2.one * .5f, size);
        effect.ring.sprite = effect.sprite;
        effect.transform.localScale = Vector3.one * radius * .3f;
        effect.ring.color = tint;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(elapsed / Duration);
        transform.localScale = Vector3.one * (radius * 2f * Mathf.Lerp(.15f, 1f, progress));
        ring.color = new Color(tint.r, tint.g, tint.b, tint.a * (1f - progress));
        if (progress >= 1f) Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (sprite != null) Destroy(sprite);
        if (texture != null) Destroy(texture);
    }
}
