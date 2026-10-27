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
    private bool generateNameLabel = true;
    private bool useLockedAlpha;
    private bool locked;
    private bool ownsNameLabel;
    private Material registrationFlashMaterial;
    private Material originalIconMaterial;
    private float registrationFlashStartedAt;
    private bool registrationFlashing;
    private static readonly int FlashAmount = Shader.PropertyToID("_FlashAmount");
    private static readonly Color DefaultNormal = new(.98f, .94f, .83f);
    private static readonly Color Hover = new(1f, .82f, .43f);
    private static readonly Color Amount = new(.08f, .06f, .04f);

    private void Awake() => CacheBackground();

    public void Initialize(InventoryUI ui, int slotIndex, Image iconImage, TMP_Text amountText, bool createNameLabel = true, bool applyLockedAlpha = false)
    {
        owner = ui;
        index = slotIndex;
        icon = iconImage;
        amount = amountText;
        generateNameLabel = createNameLabel;
        useLockedAlpha = applyLockedAlpha;
        CacheBackground();
        if (generateNameLabel) EnsureNameLabel();
        ApplyOwnerFont();
    }

    public void Bind(InventoryUI ui, int slotIndex)
    {
        owner = ui;
        index = slotIndex;
        CacheBackground();
        if (generateNameLabel) EnsureNameLabel();
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
        if (registrationFlashing && (empty || icon.sprite != slot.Item.Icon)) StopRegistrationFlash();
        icon.enabled = !empty;
        icon.sprite = empty ? null : slot.Item.Icon;
        amount.text = empty || slot.Item.MaxStack == 1 ? "" : slot.Amount.ToString();
        amount.color = Amount;
        if (itemName != null) itemName.text = empty ? "" : slot.Item.DisplayName;
    }

    public void PlayRegistrationFlash()
    {
        if (icon == null || !icon.enabled || icon.sprite == null) return;
        if (registrationFlashMaterial == null)
        {
            Shader shader = Shader.Find("BehindTheGear/UI/HotbarItemFlash");
            if (shader == null)
            {
                Debug.LogWarning("[InventoryUI] Registration flash shader is missing.", this);
                return;
            }
            registrationFlashMaterial = new Material(shader) { name = "Inventory Item Registration Flash" };
        }
        if (!registrationFlashing) originalIconMaterial = icon.material;
        icon.material = registrationFlashMaterial;
        registrationFlashStartedAt = Time.unscaledTime;
        registrationFlashing = true;
        SetRegistrationFlashAmount(1f);
    }

    private void LateUpdate()
    {
        if (!registrationFlashing) return;
        float progress = Mathf.Clamp01((Time.unscaledTime - registrationFlashStartedAt) / .3f);
        SetRegistrationFlashAmount(1f - Mathf.SmoothStep(0f, 1f, progress));
        if (progress >= 1f) StopRegistrationFlash();
    }

    private void StopRegistrationFlash()
    {
        if (!registrationFlashing) return;
        SetRegistrationFlashAmount(0f);
        registrationFlashing = false;
        if (icon != null) icon.material = originalIconMaterial;
    }

    private void SetRegistrationFlashAmount(float value)
    {
        if (registrationFlashMaterial == null) return;
        registrationFlashMaterial.SetFloat(FlashAmount, value);
        // A UI Mask may render a stencil-material copy instead of the assigned material.
        Material rendered = icon != null ? icon.materialForRendering : null;
        if (rendered != null && rendered != registrationFlashMaterial && rendered.HasProperty(FlashAmount))
            rendered.SetFloat(FlashAmount, value);
    }

    private void OnDestroy()
    {
        StopRegistrationFlash();
        if (registrationFlashMaterial == null) return;
        if (Application.isPlaying) Destroy(registrationFlashMaterial);
        else DestroyImmediate(registrationFlashMaterial);
    }

    public void ResetVisualState()
    {
        CacheBackground();
        if (background != null)
            background.color = useLockedAlpha
                ? locked ? new Color(1f, 1f, 1f, .35f) : Color.white
                : normal;
    }

    public void SetLocked(bool value)
    {
        locked = value;
        ResetVisualState();
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
        ownsNameLabel = true;
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
        if (!ownsNameLabel || owner == null || owner.UiFont == null || itemName == null) return;
        itemName.font = owner.UiFont;
    }

    public void OnBeginDrag(PointerEventData data) { if (!locked && data.button == PointerEventData.InputButton.Left) owner.BeginDrag(index, data); }
    public void OnDrag(PointerEventData data) { if (!locked) owner.Drag(data); }
    public void OnEndDrag(PointerEventData data) => owner.CancelDrag();
    public void OnDrop(PointerEventData data) { if (!locked && data.button == PointerEventData.InputButton.Left) owner.Drop(index); }
    public void OnPointerClick(PointerEventData data)
    {
        if (!locked && data.button == PointerEventData.InputButton.Left && data.clickCount >= 2) owner.DoubleClick(index);
    }
    public void OnPointerEnter(PointerEventData data)
    {
        if (locked) return;
        owner.SetHoveredSlot(index, true);
        background.color = Hover;
        owner.ShowDetails(index);
    }
    public void OnPointerExit(PointerEventData data)
    {
        owner.SetHoveredSlot(index, false);
        ResetVisualState();
        owner.ShowDetails(-1);
    }
    private void OnDisable()
    {
        StopRegistrationFlash();
        if (owner != null) owner.SetHoveredSlot(index, false);
        ResetVisualState();
    }

    private static bool IsHoverColor(Color color)
    {
        const float tolerance = .01f;
        return Mathf.Abs(color.r - Hover.r) < tolerance
            && Mathf.Abs(color.g - Hover.g) < tolerance
            && Mathf.Abs(color.b - Hover.b) < tolerance;
    }
}
