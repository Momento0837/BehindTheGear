using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class InventorySlotUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [SerializeField] private InventoryUI owner;
    [SerializeField] private int index;
    [SerializeField] private Image icon;
    [SerializeField] private Image background;
    [SerializeField] private TMP_Text amount;
    private Color normal;
    private bool hasNormal;
    private static readonly Color DefaultNormal = new(.98f, .94f, .83f);
    private static readonly Color Hover = new(1f, .82f, .43f);
    private static readonly Color Amount = new(.08f, .06f, .04f);

    private void Awake() => CacheBackground();

    public void Initialize(InventoryUI ui, int slotIndex, Image iconImage, TMP_Text amountText)
    {
        owner = ui;
        index = slotIndex;
        icon = iconImage;
        amount = amountText;
        CacheBackground();
    }

    public void Bind(InventoryUI ui, int slotIndex)
    {
        owner = ui;
        index = slotIndex;
        CacheBackground();
    }

    private void CacheBackground()
    {
        if (background == null) background = GetComponent<Image>();
        if (background == null || hasNormal) return;

        normal = IsHoverColor(background.color) ? DefaultNormal : background.color;
        hasNormal = true;
    }

    public void Refresh(PlayerInventory.Slot slot)
    {
        bool empty = slot == null || slot.IsEmpty;
        icon.enabled = !empty;
        icon.sprite = empty ? null : slot.Item.Icon;
        amount.text = empty || slot.Item.MaxStack == 1 ? "" : slot.Amount.ToString();
        amount.color = Amount;
    }

    public void ResetVisualState()
    {
        CacheBackground();
        if (background != null) background.color = normal;
    }

    public void SetVisualSize(float slotSize)
    {
        RectTransform rect = (RectTransform)transform;
        rect.sizeDelta = Vector2.one * slotSize;

        if (icon != null)
        {
            icon.rectTransform.sizeDelta = Vector2.one * Mathf.Max(24f, slotSize * .66f);
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = icon.rectTransform.pivot = Vector2.one * .5f;
            icon.rectTransform.anchoredPosition = Vector2.zero;
        }

        if (amount != null)
        {
            amount.rectTransform.sizeDelta = new Vector2(Mathf.Max(24f, slotSize - 8f), 22f);
            amount.rectTransform.anchorMin = amount.rectTransform.anchorMax = amount.rectTransform.pivot = new Vector2(0f, 1f);
            amount.rectTransform.anchoredPosition = new Vector2(4f, -(slotSize - 24f));
        }
    }

    public void OnBeginDrag(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left) owner.BeginDrag(index, data); }
    public void OnDrag(PointerEventData data) => owner.Drag(data);
    public void OnEndDrag(PointerEventData data) => owner.CancelDrag();
    public void OnDrop(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left) owner.Drop(index); }
    public void OnPointerClick(PointerEventData data)
    {
        if (data.button == PointerEventData.InputButton.Left && data.clickCount >= 2) owner.DoubleClick(index);
    }
    public void OnPointerEnter(PointerEventData data)
    {
        background.color = Hover;
        owner.ShowDetails(index);
    }
    public void OnPointerExit(PointerEventData data)
    {
        ResetVisualState();
        owner.ShowDetails(-1);
    }
    private void OnDisable() => ResetVisualState();

    private static bool IsHoverColor(Color color)
    {
        const float tolerance = .01f;
        return Mathf.Abs(color.r - Hover.r) < tolerance
            && Mathf.Abs(color.g - Hover.g) < tolerance
            && Mathf.Abs(color.b - Hover.b) < tolerance;
    }
}
