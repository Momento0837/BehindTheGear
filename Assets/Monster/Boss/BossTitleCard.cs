using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Cinematic overlay for the boss: letterbox bars, the title card ("붉은 완장의 두목 / 강철 공방장 바르톨" with an
/// underline that opens from the middle) and a one-frame white flash for the finishing blow.
/// All of it runs on real time, so it keeps moving during hit-stop and slow motion. Presentation only.
/// </summary>
public sealed class BossTitleCard : MonoBehaviour
{
    private const float ReferenceWidth = 1920f, ReferenceHeight = 1080f;
    private const float BarShare = .09f; // each bar covers this share of the screen height
    private static readonly Color NameColor = new Color32(0xf5, 0xf0, 0xed, 0xff);
    private static readonly Color TitleColor = new Color32(0xcb, 0xc4, 0xc7, 0xff);
    private static readonly Color LineColor = new Color32(0x9e, 0x00, 0x00, 0xff);

    private RectTransform top, bottom, line;
    private TextMeshProUGUI title, bossName;
    private CanvasGroup card;
    private Image flash;
    private float bars; // 0 = open, 1 = closed in

    public static BossTitleCard Create(Transform owner)
    {
        var root = new GameObject("Boss Title Card", typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(owner, false);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 16; // just over the boss HP bar, under dialogue and menus
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
        scaler.matchWidthOrHeight = .5f;
        var overlay = root.AddComponent<BossTitleCard>();
        overlay.Build();
        return overlay;
    }

    private void Build()
    {
        flash = Box("Flash", transform, Color.white).GetComponent<Image>();
        Stretch(flash.rectTransform);
        flash.enabled = false;

        top = Box("Letterbox Top", transform, Color.black);
        top.anchorMin = new Vector2(0f, 1f); top.anchorMax = Vector2.one; top.pivot = new Vector2(.5f, 1f);
        bottom = Box("Letterbox Bottom", transform, Color.black);
        bottom.anchorMin = Vector2.zero; bottom.anchorMax = new Vector2(1f, 0f); bottom.pivot = new Vector2(.5f, 0f);

        var group = new GameObject("Title", typeof(RectTransform), typeof(CanvasGroup));
        group.transform.SetParent(transform, false);
        card = group.GetComponent<CanvasGroup>();
        card.alpha = 0f; card.interactable = card.blocksRaycasts = false;
        var rect = (RectTransform)group.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .24f); // centre, lower part of the screen
        rect.sizeDelta = new Vector2(1400f, 220f);

        title = Label("Small Title", rect, 30f, TitleColor, 62f);
        bossName = Label("Name", rect, 76f, NameColor, -8f);
        line = Box("Underline", rect, LineColor);
        line.anchorMin = line.anchorMax = new Vector2(.5f, .5f);
        line.anchoredPosition = new Vector2(0f, -66f);
        line.sizeDelta = new Vector2(0f, 6f);
        ApplyBars();
    }

    /// <summary>Slides the letterbox bars in (true) or out (false).</summary>
    public IEnumerator Letterbox(bool closed, float seconds)
    {
        float from = bars, to = closed ? 1f : 0f;
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            float u = Mathf.Clamp01(t / seconds);
            bars = Mathf.Lerp(from, to, u * u * (3f - 2f * u));
            ApplyBars();
            yield return null;
        }
        bars = to;
        ApplyBars();
    }

    /// <summary>Small title, big name, underline opening from the middle; holds, then fades.</summary>
    public IEnumerator ShowTitle(string smallTitle, string name, float holdSeconds)
    {
        title.text = smallTitle;
        bossName.text = name;
        const float open = .35f, fade = .4f, lineWidth = 760f;
        for (float t = 0f; t < open; t += Time.unscaledDeltaTime)
        {
            float u = Mathf.Clamp01(t / open);
            card.alpha = u;
            line.sizeDelta = new Vector2(lineWidth * (1f - (1f - u) * (1f - u)), 6f);
            yield return null;
        }
        card.alpha = 1f;
        line.sizeDelta = new Vector2(lineWidth, 6f);
        yield return new WaitForSecondsRealtime(holdSeconds);
        for (float t = 0f; t < fade; t += Time.unscaledDeltaTime)
        {
            card.alpha = 1f - Mathf.Clamp01(t / fade);
            yield return null;
        }
        card.alpha = 0f;
    }

    /// <summary>A white screen for `seconds` (the finishing blow).</summary>
    public IEnumerator Flash(float seconds)
    {
        flash.enabled = true;
        yield return new WaitForSecondsRealtime(seconds);
        flash.enabled = false;
    }

    private void ApplyBars()
    {
        float height = ReferenceHeight * BarShare * bars;
        top.sizeDelta = bottom.sizeDelta = new Vector2(0f, height);
        top.gameObject.SetActive(bars > 0f);
        bottom.gameObject.SetActive(bars > 0f);
    }

    private static RectTransform Box(string boxName, Transform parent, Color color)
    {
        var go = new GameObject(boxName, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static TextMeshProUGUI Label(string labelName, Transform parent, float size, Color color, float y)
    {
        var go = new GameObject(labelName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        UIFontTheme.Apply(text, UIFontTheme.ExtraBold);
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        text.rectTransform.sizeDelta = new Vector2(1400f, size + 20f);
        text.rectTransform.anchoredPosition = new Vector2(0f, y);
        return text;
    }
}
