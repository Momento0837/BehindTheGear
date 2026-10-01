using UnityEngine;

[CreateAssetMenu(menuName = "Behind The Gear/Inventory Item", fileName = "New Item")]
public sealed class InventoryItemDefinition : ScriptableObject
{
    public enum FallbackIcon { Potion, Crystal, Gear }

    [SerializeField] private string displayName = "Item";
    [SerializeField, TextArea] private string description;
    [SerializeField] private Sprite icon;
    [SerializeField] private Color tint = Color.white;
    [SerializeField] private FallbackIcon fallbackIcon;
    [SerializeField, Min(1)] private int maxStack = 99;
    private Sprite generatedIcon;

    public string DisplayName => displayName;
    public string Description => description;
    public int MaxStack => Mathf.Max(1, maxStack);
    public Sprite Icon => icon != null ? icon : generatedIcon != null ? generatedIcon : generatedIcon = CreateIcon();

    // Small code-drawn placeholders; assign an imported Sprite to replace them.
    private Sprite CreateIcon()
    {
        string[] pixels = fallbackIcon switch
        {
            FallbackIcon.Crystal => new[] { ".....OO.....", "....OHCO....", "...OHHCCO...", "..OHHHCCCO..", ".OHHHHCCCCO.", "OHHHHHCCCCCO", "OHHHHCCCCCCO", ".OHHCCCCCCO.", "..OHCCCCCO..", "...OCCCCO...", "....OCCO....", ".....OO....." },
            FallbackIcon.Gear => new[] { "....OOOO....", "....OCCO....", ".OOOCCCCOOO.", ".OCCCCCCCCO.", "OOCCOOOOCCOO", "OCCHO..OCCCO", "OCCHO..OCCCO", "OOCCOOOOCCOO", ".OCCCCCCCCO.", ".OOOCCCCOOO.", "....OCCO....", "....OOOO...." },
            _ => new[] { "....OOOO....", "....OCCO....", "....OHHO....", "....OHHO....", "...OHHHHO...", "..OHHHHHHO..", ".OHHCCCCCCO.", ".OHCCCCCCCO.", ".OHCCCCCCCO.", ".OCCCCCCCCO.", "..OCCCCCCO..", "...OOOOOO..." }
        };
        Texture2D texture = new(12, 12, TextureFormat.RGBA32, false) { name = displayName + " Icon", filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < 12; y++)
            for (int x = 0; x < 12; x++)
                texture.SetPixel(x, 11 - y, pixels[y][x] switch
                {
                    'O' => new Color(.23f, .17f, .15f),
                    'H' => Color.Lerp(tint, Color.white, .7f),
                    'C' => tint,
                    _ => Color.clear
                });
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, 12, 12), Vector2.one * .5f, 20f);
    }

    private void OnDisable()
    {
        if (generatedIcon == null) return;
        Texture2D texture = generatedIcon.texture;
        if (Application.isPlaying)
        {
            Destroy(generatedIcon);
            Destroy(texture);
        }
        else
        {
            DestroyImmediate(generatedIcon);
            DestroyImmediate(texture);
        }
        generatedIcon = null;
    }
}
