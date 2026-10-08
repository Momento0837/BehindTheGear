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
    [SerializeField] private TMP_Text itemName;
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
        EnsureNameLabel();
        ApplyOwnerFont();
    }

    public void Bind(InventoryUI ui, int slotIndex)
    {
        owner = ui;
        index = slotIndex;
        CacheBackground();
        EnsureNameLabel();
        ApplyOwnerFont();
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
        if (itemName != null) itemName.text = empty ? "" : slot.Item.DisplayName;
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
            icon.rectTransform.sizeDelta = Vector2.one * Mathf.Max(24f, slotSize * .48f);
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = icon.rectTransform.pivot = new Vector2(.5f, .63f);
            icon.rectTransform.anchoredPosition = Vector2.zero;
        }

        if (amount != null)
        {
            amount.rectTransform.sizeDelta = new Vector2(Mathf.Max(24f, slotSize - 8f), 22f);
            amount.rectTransform.anchorMin = amount.rectTransform.anchorMax = amount.rectTransform.pivot = new Vector2(0f, 1f);
            amount.rectTransform.anchoredPosition = new Vector2(4f, -(slotSize - 24f));
        }

        if (itemName != null)
        {
            itemName.rectTransform.sizeDelta = new Vector2(Mathf.Max(24f, slotSize - 8f), 20f);
            itemName.rectTransform.anchorMin = itemName.rectTransform.anchorMax = itemName.rectTransform.pivot = new Vector2(.5f, 0f);
            itemName.rectTransform.anchoredPosition = new Vector2(0f, 4f);
            itemName.fontSize = Mathf.Clamp(slotSize * .13f, 9f, 12f);
        }
    }

    private void EnsureNameLabel()
    {
        if (itemName != null) return;

        Transform found = transform.Find("Item Name");
        if (found != null) itemName = found.GetComponent<TMP_Text>();
        if (itemName != null) return;

        GameObject labelObject = new("Item Name", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(transform, false);
        itemName = labelObject.GetComponent<TMP_Text>();
        itemName.raycastTarget = false;
        itemName.alignment = TextAlignmentOptions.Center;
        itemName.color = Amount;
        itemName.fontStyle = FontStyles.Normal;
        itemName.fontWeight = FontWeight.Regular;
        itemName.textWrappingMode = TextWrappingModes.NoWrap;
        itemName.overflowMode = TextOverflowModes.Ellipsis;
        ApplyOwnerFont();
    }

    private void ApplyOwnerFont()
    {
        if (owner == null || owner.UiFont == null || itemName == null) return;
        itemName.font = owner.UiFont;
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
