using TMPro;
using UnityEngine;

/// <summary>Real PFStardust font weights with clean UI materials, separate from speaker fonts.</summary>
public static class UIFontTheme
{
    private static TMP_FontAsset normal;
    private static TMP_FontAsset bold;
    private static TMP_FontAsset extraBold;
    public static TMP_FontAsset Normal => normal != null ? normal : normal = Resources.Load<TMP_FontAsset>("UI/Fonts/PFStardustNormal");
    public static TMP_FontAsset Bold => bold != null ? bold : bold = Resources.Load<TMP_FontAsset>("UI/Fonts/PFStardustBold");
    public static TMP_FontAsset ExtraBold => extraBold != null ? extraBold : extraBold = Resources.Load<TMP_FontAsset>("UI/Fonts/PFStardustExtraBold");

    public static void Apply(TMP_Text text, TMP_FontAsset font)
    {
        if (text == null || font == null) return;
        text.font = font;
        text.fontSharedMaterial = font.material;
        text.fontStyle &= ~FontStyles.Bold;
        text.fontWeight = FontWeight.Regular;
    }
}
