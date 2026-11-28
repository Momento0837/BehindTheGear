using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Boss HP bar: bottom centre of the screen, 60 % wide, square pixel look. Small title above the name, a red fill,
/// a pale "damage lag" bar that catches up after a short delay, and a tick where phase 2 starts.
/// Rises in from below when shown, nudges on a heavy hit, fades out when the boss dies. Presentation only.
/// </summary>
public sealed class BossHealthBarUI : MonoBehaviour
{
    private static readonly Color Border = new Color32(0x21, 0x20, 0x1f, 0xff);
    private static readonly Color Back = new Color32(0x2f, 0x2d, 0x2c, 0xff);
    private static readonly Color Fill = new Color32(0x9e, 0x00, 0x00, 0xff);
    private static readonly Color Lag = new Color32(0xf0, 0xc2, 0x7a, 0xff);
    private static readonly Color NameColor = new Color32(0xf5, 0xf0, 0xed, 0xff);
    private static readonly Color TitleColor = new Color32(0xcb, 0xc4, 0xc7, 0xff);

    private const float ReferenceWidth = 1920f, ReferenceHeight = 1080f;
    // Everything stays inside the strip below the ground line (about 12 % of the screen in the forest), so the bar
    // never covers the boss's feet or what lies on the ground: the name is written on the bar itself.
    private const float BarWidth = ReferenceWidth * .6f, BarHeight = 30f, Frame = 4f;
    private const float BottomMargin = ReferenceHeight * .026f;
    private const float RiseSeconds = .4f, LagDelay = .35f, LagSpeed = .9f, FadeSeconds = .8f, NudgeSeconds = .15f;

    private BossGoliath boss;
    private RectTransform panel, fill, lag, tick;
    private CanvasGroup group;
    private float shown = 1f, lagShown = 1f, lagHoldUntil, appear, nudgeUntil, fade = 1f;

    public static BossHealthBarUI Show(BossGoliath owner)
    {
        var root = new GameObject("Boss Health Bar", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        root.transform.SetParent(owner.transform, false); // goes away with the boss
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 15; // under dialogue bubbles and menus
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
        scaler.matchWidthOrHeight = .5f;
        var bar = root.AddComponent<BossHealthBarUI>();
        bar.boss = owner;
        bar.group = root.GetComponent<CanvasGroup>();
        bar.group.interactable = bar.group.blocksRaycasts = false;
        bar.Build(owner.Profile);
        owner.Damaged += bar.OnDamaged;
        owner.PhaseChanged += bar.OnPhaseChanged;
        return bar;
    }

    private void Build(BossProfile profile)
    {
        panel = Box("Panel", transform, Color.clear, new Vector2(BarWidth + Frame * 2, 120f));
        panel.anchorMin = panel.anchorMax = new Vector2(.5f, 0f);
        panel.pivot = new Vector2(.5f, 0f);

        var frame = Box("Frame", panel, Border, new Vector2(BarWidth + Frame * 2, BarHeight + Frame * 2));
        Bottom(frame, 0f);
        var back = Box("Back", frame, Back, new Vector2(BarWidth, BarHeight));
        back.anchoredPosition = Vector2.zero;
        lag = Bar("Damage Lag", back, Lag);
        fill = Bar("Fill", back, Fill);

        // Phase 2 tick: a notch in the frame colour at the threshold.
        tick = Box("Phase Tick", back, Border, new Vector2(4f, BarHeight));
        tick.anchorMin = tick.anchorMax = new Vector2(profile.phaseTwoAt, .5f);
        tick.anchoredPosition = Vector2.zero;

        // Name centred on the bar (dark copy underneath so it reads on the red fill and on the empty bar alike).
        foreach (var (color, offset) in new[] { (Border, new Vector2(2f, -2f)), (NameColor, Vector2.zero) })
        {
            var nameText = Label("Name", profile.displayName, 24f, color);
            nameText.alignment = TextAlignmentOptions.Center;
            nameText.rectTransform.sizeDelta = new Vector2(BarWidth, BarHeight + Frame * 2);
            Bottom(nameText.rectTransform, 0f);
            nameText.rectTransform.anchoredPosition += offset;
        }
        var titleText = Label("Title", profile.title, 17f, TitleColor);
        Bottom(titleText.rectTransform, BarHeight + Frame * 2 + 2f);
        Layout();
    }

    private void OnDamaged(int taken)
    {
        lagHoldUntil = Time.unscaledTime + LagDelay;
        if (boss != null && taken >= boss.MaxHealth * .05f) nudgeUntil = Time.unscaledTime + NudgeSeconds;
    }

    // Phase 2: the tick breaks off and the bar jolts.
    private void OnPhaseChanged(int phase)
    {
        if (tick != null) tick.gameObject.SetActive(false);
        nudgeUntil = Time.unscaledTime + NudgeSeconds * 3f;
    }

    private void Update()
    {
        if (boss == null) return;
        float dt = Time.unscaledDeltaTime; // UI keeps moving through hit-stop and slow motion
        appear = Mathf.MoveTowards(appear, 1f, dt / RiseSeconds);
        shown = boss.MaxHealth > 0 ? Mathf.Clamp01(boss.CurrentHealth / (float)boss.MaxHealth) : 0f;
        if (lagShown < shown) lagShown = shown;
        else if (Time.unscaledTime >= lagHoldUntil) lagShown = Mathf.MoveTowards(lagShown, shown, LagSpeed * dt);
        if (boss.IsDead && Mathf.Approximately(lagShown, shown)) fade = Mathf.MoveTowards(fade, 0f, dt / FadeSeconds);
        Layout();
    }

    private void Layout()
    {
        float eased = 1f - (1f - appear) * (1f - appear);
        float nudge = Time.unscaledTime < nudgeUntil ? (Mathf.FloorToInt(Time.unscaledTime * 50f) % 2 == 0 ? 2f : -2f) : 0f;
        panel.anchoredPosition = new Vector2(nudge, Mathf.Lerp(-160f, BottomMargin, eased));
        fill.anchorMax = new Vector2(shown, 1f);
        lag.anchorMax = new Vector2(lagShown, 1f);
        group.alpha = fade;
    }

    private void OnDestroy()
    {
        if (boss == null) return;
        boss.Damaged -= OnDamaged;
        boss.PhaseChanged -= OnPhaseChanged;
    }

    private static RectTransform Box(string boxName, Transform parent, Color color, Vector2 size)
    {
        var go = new GameObject(boxName, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = size;
        return rect;
    }

    // A bar that fills from the left: its right edge is moved through anchorMax.x.
    private static RectTransform Bar(string barName, Transform parent, Color color)
    {
        var rect = Box(barName, parent, color, Vector2.zero);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }

    private static void Bottom(RectTransform rect, float y)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0f);
        rect.pivot = new Vector2(.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, y);
    }

    private TextMeshProUGUI Label(string labelName, string content, float size, Color color)
    {
        var go = new GameObject(labelName, typeof(RectTransform));
        go.transform.SetParent(panel, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        UIFontTheme.Apply(text, UIFontTheme.ExtraBold);
        text.text = content;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.Bottom;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        text.rectTransform.sizeDelta = new Vector2(BarWidth, size + 10f);
        return text;
    }
}
