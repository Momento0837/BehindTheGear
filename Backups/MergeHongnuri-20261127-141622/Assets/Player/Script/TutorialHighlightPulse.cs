using UnityEngine;
using UnityEngine.UI;

public static class TutorialHighlightPulse
{
    public const float FadeSeconds = .5f;
    public const float DimOpacity = .3f;

    // Shared by the inventory shortcut and facility buttons: two pulses, then one second of rest.
    public static void Apply(Image border, float elapsed)
    {
        float cycle = Mathf.Repeat(Mathf.Max(0, elapsed - FadeSeconds), 4.2f);
        border.enabled = elapsed >= FadeSeconds && cycle < 3.2f;
        if (!border.enabled) return;
        float time = Mathf.Repeat(cycle, 1.6f);
        float progress = Mathf.Clamp01(time / .8f);
        border.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.5f, 1f, progress);
        float alpha = time < .8f ? Mathf.Lerp(.2f, 1f, progress) : time < 1.1f ? 1f : 1f - Mathf.Clamp01((time - 1.1f) / .5f);
        border.color = new Color(1, 1, 1, alpha);
    }
}
