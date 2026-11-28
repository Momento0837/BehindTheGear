using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Presentation only. The original Button continues to own clicks, submit and navigation.
/// Copper plates for the slot actions; ConfigureAccount gives the account button its own verdigris (oxidised copper)
/// capsule with a cloud icon so it reads as a different kind of control.
/// </summary>
[RequireComponent(typeof(Button))]
public sealed class SaveSlotButtonStyle : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler, ISubmitHandler
{
    private Button button;
    private TMP_Text label;
    private SaveSlotPlateGraphic plate;
    private Image grain;
    private Image gear;
    private bool hovered, selected, pointerDown, destructive, account, attention;
    private float iconX = 30f, labelLeft = 36f, labelRight = 36f, labelLift;
    private TMP_Text detail;
    private int previousState = -1;
    private bool previousPrimary;
    private float submitUntil;
    public bool IsPrimary { get; private set; }
    public string VisualState { get; private set; }

    /// <summary>Verdigris capsule with an icon on the left (used by the account button).</summary>
    public void ConfigureAccount(TMP_Text text, Sprite icon)
    {
        account = true;
        iconX = 36f; labelLeft = 62f; labelRight = 24f;
        Configure(text, null, icon, false);
        plate.CornerRadius = 999f; // clamped to half the height: a capsule
        plate.ShowPin = false;
        if (gear != null)
        {
            gear.gameObject.name = "Account Icon";
            gear.rectTransform.sizeDelta = new Vector2(45, 27); // 15x9 icon pixels at 3 menu units each
        }
        Apply(true);
    }

    public void SetCornerRadius(float radius) { if (plate != null) plate.CornerRadius = radius; }

    /// <summary>Small dim second line under the label (the save record on 이어하기). Empty text removes it.</summary>
    public void SetDetail(string text)
    {
        bool show = !string.IsNullOrEmpty(text);
        if (detail == null)
        {
            if (!show) return;
            var go = new GameObject("Detail", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(transform, false);
            detail = go.GetComponent<TMP_Text>();
            UIFontTheme.Apply(detail, UIFontTheme.Normal);
            detail.fontSize = 16; detail.alignment = TextAlignmentOptions.Center; detail.raycastTarget = false;
            var rect = detail.rectTransform;
            rect.anchorMin = new Vector2(0, 0); rect.anchorMax = new Vector2(1, 0); rect.pivot = new Vector2(.5f, 0);
            rect.sizeDelta = new Vector2(-72, 20);
        }
        detail.text = show ? text : "";
        detail.gameObject.SetActive(show);
        labelLift = show ? 18f : 0f;
        Apply(true);
    }

    /// <summary>Account button only: amber accent when the account needs the player's attention.</summary>
    public void SetAttention(bool value)
    {
        if (attention == value) return;
        attention = value; Apply(true);
    }

    public void Configure(TMP_Text text, Sprite existingButtonSprite, Sprite existingGearSprite, bool isDestructive)
    {
        button = GetComponent<Button>(); label = text; destructive = isDestructive;
        button.transition = Selectable.Transition.None;
        GetComponent<Image>().color = Color.clear; // Retain the original rectangular raycast surface.
        var background = new GameObject("Pixel Metal Plate", typeof(RectTransform), typeof(SaveSlotPlateGraphic));
        background.transform.SetParent(transform, false); background.transform.SetAsFirstSibling();
        plate = background.GetComponent<SaveSlotPlateGraphic>(); plate.raycastTarget = false;
        Stretch(plate.rectTransform, 0);
        button.targetGraphic = plate;
        if (existingButtonSprite != null)
        {
            var texture = new GameObject("Reused Main Button Grain", typeof(RectTransform), typeof(Image));
            texture.transform.SetParent(background.transform, false);
            grain = texture.GetComponent<Image>(); grain.sprite = existingButtonSprite;
            grain.type = Image.Type.Sliced;
            // Preserve source pixels at a 2-unit scale; do not edit the shared texture importer/borders.
            grain.pixelsPerUnitMultiplier = 50f / existingButtonSprite.pixelsPerUnit;
            grain.raycastTarget = false;
            Stretch(grain.rectTransform, 10);
        }
        if (existingGearSprite != null)
        {
            var cog = new GameObject("Copper Gear Inlay", typeof(RectTransform), typeof(Image));
            cog.transform.SetParent(transform, false);
            gear = cog.GetComponent<Image>(); gear.sprite = existingGearSprite;
            gear.preserveAspect = true; gear.raycastTarget = false;
            gear.rectTransform.anchorMin = gear.rectTransform.anchorMax = new Vector2(0, .5f);
            gear.rectTransform.anchoredPosition = new Vector2(iconX, 2);
            gear.rectTransform.sizeDelta = new Vector2(24, 24);
        }
        label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(labelLeft, 4); label.rectTransform.offsetMax = new Vector2(-labelRight, 0);
        label.alignment = TextAlignmentOptions.Center;
        Apply(true);
    }

    public void SetPrimary(bool value) { IsPrimary = value && !destructive && !account; Apply(true); }
    private void LateUpdate() => Apply(false);

    private void Apply(bool force)
    {
        if (plate == null || label == null || button == null) return;
        bool enabled = button.IsInteractable();
        bool down = enabled && (pointerDown || Time.unscaledTime < submitUntil);
        bool focus = enabled && (hovered || selected);
        int state = !enabled ? 0 : down ? 3 : focus ? 2 : 1;
        if (!force && state == previousState && previousPrimary == IsPrimary) return;
        previousState = state; previousPrimary = IsPrimary;
        VisualState = !enabled ? "Disabled" : down ? "Pressed" : focus ? "Focused" : "Normal";
        Color top = destructive ? new Color(.20f, .12f, .105f) : new Color(.23f, .185f, .15f);
        Color bottom = destructive ? new Color(.12f, .075f, .065f) : new Color(.125f, .10f, .085f);
        Color rim = destructive ? new Color(.38f, .23f, .19f)
            : IsPrimary ? new Color(.65f, .49f, .28f) : new Color(.35f, .28f, .215f);
        Color edge = destructive ? new Color(.43f, .285f, .23f) : new Color(.49f, .385f, .27f);
        Color pin = new(.52f, .40f, .27f);
        label.color = new Color(.87f, .80f, .68f);
        Color icon = destructive ? new Color(.55f, .36f, .29f) : new Color(.62f, .51f, .37f);
        if (account)
        {
            // Oxidised copper: the one cool colour on the menu, so the account button is easy to spot.
            top = attention ? new Color(.30f, .21f, .10f) : new Color(.15f, .29f, .27f);
            bottom = attention ? new Color(.17f, .11f, .06f) : new Color(.08f, .16f, .15f);
            rim = attention ? new Color(.72f, .52f, .24f) : new Color(.30f, .56f, .49f);
            edge = attention ? new Color(.86f, .66f, .34f) : new Color(.45f, .74f, .65f);
            label.color = attention ? new Color(1f, .87f, .64f) : new Color(.80f, .94f, .88f);
            icon = attention ? new Color(1f, .78f, .42f) : new Color(.62f, .90f, .80f);
        }
        if (IsPrimary) { top *= 1.18f; label.color = new Color(.97f, .88f, .69f); }
        if (focus)
        {
            rim = account ? (attention ? new Color(1f, .78f, .45f) : new Color(.62f, .95f, .82f))
                : destructive ? new Color(.62f, .38f, .29f) : new Color(.91f, .72f, .43f);
            top *= 1.28f; bottom *= 1.15f; edge = rim;
            label.color = account ? new Color(.94f, 1f, .97f) : destructive ? new Color(.94f, .78f, .68f) : new Color(1, .94f, .79f);
            icon = account ? new Color(.86f, 1f, .94f) : destructive ? icon : new Color(1f, .86f, .61f);
        }
        else if (IsPrimary) icon = new Color(1f, .86f, .61f);
        if (down) { (top, bottom) = (bottom * .75f, top * .85f); rim *= .85f; }
        if (!enabled)
        {
            top = new Color(.14f, .13f, .12f); bottom = new Color(.10f, .095f, .09f);
            rim = new Color(.25f, .23f, .20f); edge = new Color(.29f, .265f, .225f); pin = rim;
            label.color = new Color(.58f, .55f, .49f);
        }
        top.a = bottom.a = rim.a = edge.a = 1;
        plate.SetAppearance(rim, top, bottom, edge, pin, down);
        if (grain != null) grain.color = enabled ? new Color(.43f, .38f, .32f, .12f) : new Color(.3f, .29f, .27f, .055f);
        if (gear != null)
        {
            gear.color = !enabled ? new Color(.42f, .39f, .34f) : icon;
            gear.rectTransform.anchoredPosition = new Vector2(iconX, down ? -2 : 2);
        }
        label.rectTransform.offsetMin = new Vector2(labelLeft, (down ? 0 : 4) + labelLift);
        label.rectTransform.offsetMax = new Vector2(-labelRight, down ? -4 : 0);
        if (detail != null)
        {
            detail.rectTransform.anchoredPosition = new Vector2(0, down ? 9 : 13);
            detail.color = !enabled ? new Color(.42f, .39f, .34f) : focus ? new Color(.78f, .63f, .45f) : new Color(.62f, .49f, .37f);
        }
    }

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.one * inset; rect.offsetMax = Vector2.one * -inset;
    }

    public void OnPointerEnter(PointerEventData data) { hovered = true; Apply(false); }
    public void OnPointerExit(PointerEventData data) { hovered = false; pointerDown = false; Apply(false); }
    public void OnPointerDown(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left) pointerDown = true; Apply(false); }
    public void OnPointerUp(PointerEventData data) { pointerDown = false; Apply(false); }
    public void OnSelect(BaseEventData data) { selected = true; Apply(false); }
    public void OnDeselect(BaseEventData data) { selected = false; pointerDown = false; Apply(false); }
    public void OnSubmit(BaseEventData data) { submitUntil = Time.unscaledTime + .12f; Apply(false); }
    private void OnDisable() { hovered = selected = pointerDown = false; submitUntil = 0; previousState = -1; }
}
