using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class InventorySlotUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private InventoryUI owner;
    [SerializeField] private int index;
    [SerializeField] private Image icon;
    [SerializeField] private Image background;
    [SerializeField] private TMP_Text amount;
    private Color normal;

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
        normal = background.color;
    }

    public void Refresh(PlayerInventory.Slot slot)
    {
        icon.enabled = !slot.IsEmpty;
        icon.sprite = slot.IsEmpty ? null : slot.Item.Icon;
        amount.text = slot.IsEmpty || slot.Item.MaxStack == 1 ? "" : slot.Amount.ToString();
    }

    public void OnBeginDrag(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left) owner.BeginDrag(index, data); }
    public void OnDrag(PointerEventData data) => owner.Drag(data);
    public void OnEndDrag(PointerEventData data) => owner.CancelDrag();
    public void OnDrop(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left) owner.Drop(index); }
    public void OnPointerEnter(PointerEventData data)
    {
        background.color = new Color(1f, .82f, .43f);
        owner.ShowDetails(index);
    }
    public void OnPointerExit(PointerEventData data)
    {
        background.color = normal;
        owner.ShowDetails(-1);
    }
    private void OnDisable() { if (background != null) background.color = normal; }
}
