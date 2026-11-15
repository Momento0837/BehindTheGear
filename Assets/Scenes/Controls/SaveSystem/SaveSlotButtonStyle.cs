using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Presentation only. The original Button continues to own clicks, submit and navigation.</summary>
[RequireComponent(typeof(Button))]
public sealed class SaveSlotButtonStyle : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler, ISubmitHandler
{
    private Button button;
    private TMP_Text label;
    private SaveSlotPlateGraphic plate;
    private Image grain;
    private Image gear;
    private bool hovered, selected, pointerDown, destructive;
    private int previousState = -1;
    private bool previousPrimary;
    private float submitUntil;
    public bool IsPrimary { get; private set; }
    public string VisualState { get; private set; }

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
            gear.rectTransform.anchoredPosition = new Vector2(30, 2);
            gear.rectTransform.sizeDelta = new Vector2(24, 24);
        }
        label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(36, 4); label.rectTransform.offsetMax = new Vector2(-36, 0);
        label.alignment = TextAlignmentOptions.Center;
        Apply(true);
    }

    public void SetPrimary(bool value) { IsPrimary = value && !destructive; Apply(true); }
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
        if (IsPrimary) { top *= 1.18f; label.color = new Color(.97f, .88f, .69f); }
        if (focus)
        {
            rim = destructive ? new Color(.62f, .38f, .29f) : new Color(.91f, .72f, .43f);
            top *= 1.28f; bottom *= 1.15f; edge = rim;
            label.color = destructive ? new Color(.94f, .78f, .68f) : new Color(1, .94f, .79f);
        }
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
            gear.color = !enabled ? new Color(.42f, .39f, .34f) : destructive ? new Color(.55f, .36f, .29f)
                : focus || IsPrimary ? new Color(1f, .86f, .61f) : new Color(.62f, .51f, .37f);
            gear.rectTransform.anchoredPosition = new Vector2(30, down ? -2 : 2);
        }
        label.rectTransform.offsetMin = new Vector2(36, down ? 0 : 4);
        label.rectTransform.offsetMax = new Vector2(-36, down ? -4 : 0);
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
