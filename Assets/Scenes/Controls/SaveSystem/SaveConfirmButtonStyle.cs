using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Presentation only for the confirmation dialog buttons. The Button keeps clicks, submit and navigation.</summary>
[RequireComponent(typeof(Button))]
public sealed class SaveConfirmButtonStyle : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler, ISubmitHandler, IMoveHandler
{
    // The dialog pre-selects Cancel for keyboard/gamepad safety; that silent default must not look hovered.
    // Selection is only shown once the player actually navigates with arrows/gamepad.
    private static bool keyboardFocusVisible;
    private Button button;
    private TMP_Text label;
    private SaveConfirmGraphic plate;
    private SaveConfirmGraphic.Tone tone;
    private bool hovered, selected, pointerDown, confirmAction;
    private float submitUntil;

    public void Configure(TMP_Text text, bool isConfirmAction)
    {
        button = GetComponent<Button>(); label = text; confirmAction = isConfirmAction;
        button.transition = Selectable.Transition.None;
        GetComponent<Image>().color = Color.clear; // Keep the rectangular raycast surface.
        var background = new GameObject("Bracketed Plate", typeof(RectTransform), typeof(SaveConfirmGraphic));
        background.transform.SetParent(transform, false); background.transform.SetAsFirstSibling();
        plate = background.GetComponent<SaveConfirmGraphic>();
        plate.Configure(SaveConfirmGraphic.Mode.Button, isConfirmAction);
        var rect = plate.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        button.targetGraphic = plate;
        label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(20, 2); label.rectTransform.offsetMax = new Vector2(-20, 0);
        label.alignment = TextAlignmentOptions.Center;
        Apply();
    }

    public void SetTone(SaveConfirmGraphic.Tone value) { tone = value; plate?.SetTone(value); Apply(); }

    private void LateUpdate() => Apply();

    private void Apply()
    {
        if (plate == null || label == null || button == null) return;
        bool down = pointerDown || Time.unscaledTime < submitUntil;
        bool focus = hovered || (selected && keyboardFocusVisible);
        plate.SetState(focus, down);
        bool danger = confirmAction && tone == SaveConfirmGraphic.Tone.Danger;
        label.color = focus ? (danger ? new Color(.96f, .76f, .67f) : new Color(.93f, .87f, .76f))
            : danger ? new Color(.88f, .66f, .58f) : new Color(.80f, .74f, .64f);
        label.rectTransform.offsetMin = new Vector2(20, down ? 0 : 2);
        label.rectTransform.offsetMax = new Vector2(-20, down ? -2 : 0);
    }

    public void OnPointerEnter(PointerEventData data) { hovered = true; keyboardFocusVisible = false; Apply(); }
    public void OnMove(AxisEventData data) { keyboardFocusVisible = true; }
    public void OnPointerExit(PointerEventData data) { hovered = false; pointerDown = false; Apply(); }
    public void OnPointerDown(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left) pointerDown = true; Apply(); }
    public void OnPointerUp(PointerEventData data) { pointerDown = false; Apply(); }
    public void OnSelect(BaseEventData data) { selected = true; Apply(); }
    public void OnDeselect(BaseEventData data) { selected = false; pointerDown = false; Apply(); }
    public void OnSubmit(BaseEventData data) { submitUntil = Time.unscaledTime + .12f; Apply(); }
    private void OnDisable() { hovered = selected = pointerDown = false; submitUntil = 0; keyboardFocusVisible = false; }
}
