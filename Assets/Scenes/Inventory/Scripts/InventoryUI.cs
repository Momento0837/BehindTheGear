using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Uses the editable scene canvas without resetting its layout when entering Play mode.</summary>
public sealed class InventoryUI : MonoBehaviour
{
    private enum Filter { All, Misc, Consumable, Equipment, Quest }

    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private PlayerStats stats;
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private TMP_FontAsset detailFont;
    [Header("Scene UI references (created by the editor setup)")]
    [SerializeField] private Canvas canvas;
    [SerializeField] private GameObject window;
    [SerializeField] private Button shortcutButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private InventorySlotUI[] slots;
    [SerializeField] private TMP_Text capacityText;
    [SerializeField] private TMP_Text detailText;
    [SerializeField] private TMP_Text toast;
    [SerializeField] private Image dragIcon;
    [Header("Inventory UI")]
    [SerializeField] private Button[] filterButtons;
    [SerializeField] private TMP_Text quickSlotText;
    [Header("Equipment / Stats UI")]
    [SerializeField] private GameObject equipmentWindow;
    [SerializeField] private Button equipmentCloseButton;
    [SerializeField] private TMP_Text statPointText;
    [SerializeField] private TMP_Text statMessageText;
    [SerializeField] private TMP_Text[] statRows;
    [SerializeField] private Button[] statUpgradeButtons;
    [SerializeField] private Image[] statBarFills;
    [SerializeField] private TMP_Text[] equipmentRows;
    [SerializeField] private Button[] equipmentUnequipButtons;
    [SerializeField] private Image[] equipmentSlotIcons;

    private int dragSource = -1;
    private float toastUntil;
    private bool ready;
    private bool ownsRuntimeCanvas;
    private Filter currentFilter;
    private bool hasEquipmentSoloLayout;
    private Vector2 equipmentSoloAnchorMin;
    private Vector2 equipmentSoloAnchorMax;
    private Vector2 equipmentSoloPivot;
    private Vector2 equipmentSoloPosition;
    private static readonly Color Ink = new(.25f, .19f, .15f);
    private static readonly Color Cream = new(1f, .97f, .88f);
    private const int BaseSlotCount = 15;
    private const float WindowWidth = 1040f;
    private const float WindowHeight = 604f;
    private const float LeftPanelX = 22f;
    private const float LeftPanelY = -78f;
    private const float LeftPanelWidth = 286f;
    private const float LeftPanelHeight = 492f;
    private const float CategoryX = 326f;
    private const float CategoryY = -92f;
    private const float CategoryWidth = 114f;
    private const float InventoryContentX = 462f;
    private const float InventoryContentWidth = 538f;
    private const float InfoRowY = -82f;
    private const float FilterTabsY = -92f;
    private const float SlotViewportY = -124f;
    private const float SlotViewportHeight = 356f;
    private const float DetailTextY = -492f;
    private const float QuickSlotY = -542f;
    private const float PairedWindowGap = 16f;
    private const float SlotPadding = 8f;
    private const float SlotSpacing = 8f;
    private const float MinSlotSpacing = 2f;
    private const float MinSlotSize = 70f;
    private const float MaxSlotSize = 92f;

    public bool HasSceneUI => canvas != null;
    public bool CanCreateSceneUI => inventory != null;
    public Canvas SceneCanvas => canvas;
    public GameObject Window => window;
    public Button ShortcutButton => shortcutButton;
    public GameObject EquipmentStatsPanel => equipmentWindow;
    public TMP_FontAsset UiFont => font;

    private void Awake()
    {
        if (inventory == null)
        {
            Debug.LogError("InventoryUI requires a PlayerInventory reference.", this);
            enabled = false;
            return;
        }

        if (stats == null) stats = inventory.Stats != null ? inventory.Stats : inventory.GetComponent<PlayerStats>();
        if (stats == null) stats = inventory.gameObject.AddComponent<PlayerStats>();

        if (canvas == null)
        {
            CreateSceneUI();
            ownsRuntimeCanvas = true;
        }

        if (window == null || shortcutButton == null || closeButton == null || capacityText == null
            || detailText == null || toast == null || dragIcon == null || slots == null || slots.Length == 0)
        {
            Debug.LogError("[InventoryUI] Scene UI references are missing. Check this component in the Inspector.", this);
            enabled = false;
            return;
        }

        EnsureEventSystem();
        EnsureInventoryExtras();
        EnsureEquipmentStatsWindow();
        CaptureEquipmentSoloLayout();
        ArrangeInventoryWindowLayout();
        ApplyStaticLabels();
        EnsureToastStyle();
        MatchSlotCount();
        for (int i = 0; i < slots.Length; i++) slots[i].Bind(this, i);
        AddButtonListeners();
        ready = true;
        Refresh();
        CloseInventory();
        CloseEquipmentStats();
        toast.gameObject.SetActive(false);
        CancelDrag();
    }

    private void OnEnable()
    {
        if (!ready) return;
        inventory.Changed += Refresh;
        inventory.ItemCollected += Collected;
        inventory.ItemUsed += Used;
        inventory.InventoryFull += Full;
        if (stats != null) stats.Changed += Refresh;
        shortcutButton.onClick.AddListener(ToggleInventoryFromButton);
        closeButton.onClick.AddListener(CloseInventoryFromButton);
        if (equipmentCloseButton != null) equipmentCloseButton.onClick.AddListener(CloseEquipmentStatsFromButton);
        if (canvas != null) { canvas.gameObject.SetActive(true); Refresh(); }
    }

    private void OnDisable()
    {
        if (inventory != null)
        {
            inventory.Changed -= Refresh;
            inventory.ItemCollected -= Collected;
            inventory.ItemUsed -= Used;
            inventory.InventoryFull -= Full;
        }

        if (stats != null) stats.Changed -= Refresh;
        if (shortcutButton != null) shortcutButton.onClick.RemoveListener(ToggleInventoryFromButton);
        if (closeButton != null) closeButton.onClick.RemoveListener(CloseInventoryFromButton);
        if (equipmentCloseButton != null) equipmentCloseButton.onClick.RemoveListener(CloseEquipmentStatsFromButton);
        CancelDrag();
        if (!Application.isPlaying) return;

        if (window != null) window.SetActive(false);
        if (!IsEmbeddedEquipmentWindow() && equipmentWindow != null) equipmentWindow.SetActive(false);
        if (ownsRuntimeCanvas && canvas != null) canvas.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (ownsRuntimeCanvas && canvas != null) Destroy(canvas.gameObject);
    }

    private void Update()
    {
        if (!ready) return;
        if (Keyboard.current?.iKey.wasPressedThisFrame == true) ToggleInventory();
        if (Keyboard.current?.eKey.wasPressedThisFrame == true) ToggleEquipmentStats();
        if (Keyboard.current?.escapeKey.wasPressedThisFrame == true && IsAnyWindowOpen()) CloseAllWindows();
        if (Keyboard.current?.digit1Key.wasPressedThisFrame == true) UseQuickSlot(0);
        if (Keyboard.current?.digit2Key.wasPressedThisFrame == true) UseQuickSlot(1);
        if (Keyboard.current?.digit3Key.wasPressedThisFrame == true) UseQuickSlot(2);
        if (toast.gameObject.activeSelf && Time.unscaledTime >= toastUntil) toast.gameObject.SetActive(false);
    }

    private void OnApplicationFocus(bool focused) { if (!focused) CancelDrag(); }
    private void OnRectTransformDimensionsChange() { if (ready) ApplySlotLayout(GetDisplayedSlotCount()); }
    private void ToggleInventoryFromButton() { PlayerInputReader.SuppressPointerForCurrentFrame(); ToggleInventory(); }
    private void CloseInventoryFromButton() { PlayerInputReader.SuppressPointerForCurrentFrame(); CloseInventory(); }
    private void CloseEquipmentStatsFromButton() { PlayerInputReader.SuppressPointerForCurrentFrame(); CloseEquipmentStats(); }

    private void ToggleInventory()
    {
        if (window.activeSelf)
        {
            CloseInventory();
            return;
        }

        Refresh();
        window.SetActive(true);
        if (equipmentWindow != null && equipmentWindow.activeSelf) ArrangeEquipmentBesideInventory();
    }

    private void ToggleEquipmentStats()
    {
        if (IsEmbeddedEquipmentWindow())
        {
            ToggleInventory();
            return;
        }

        if (equipmentWindow != null && equipmentWindow.activeSelf)
        {
            CloseEquipmentStats();
            return;
        }

        Refresh();
        if (equipmentWindow == null) return;

        equipmentWindow.SetActive(true);
        if (window != null && window.activeSelf) ArrangeEquipmentBesideInventory();
        else RestoreEquipmentSoloLayout();
    }

    private void CloseInventory()
    {
        CancelDrag();
        ShowDetails(-1);
        if (window != null) window.SetActive(false);
        if (equipmentWindow != null && equipmentWindow.activeSelf) RestoreEquipmentSoloLayout();
    }

    private void CloseEquipmentStats()
    {
        if (IsEmbeddedEquipmentWindow()) return;
        if (equipmentWindow != null) equipmentWindow.SetActive(false);
        RestoreEquipmentSoloLayout();
    }

    private void CloseAllWindows()
    {
        CloseInventory();
        CloseEquipmentStats();
    }

    private bool IsAnyWindowOpen()
    {
        return (window != null && window.activeSelf) || (!IsEmbeddedEquipmentWindow() && equipmentWindow != null && equipmentWindow.activeSelf);
    }

    private void CaptureEquipmentSoloLayout()
    {
        if (hasEquipmentSoloLayout || equipmentWindow == null || IsEmbeddedEquipmentWindow()) return;

        RectTransform equipmentRect = (RectTransform)equipmentWindow.transform;
        equipmentSoloAnchorMin = equipmentRect.anchorMin;
        equipmentSoloAnchorMax = equipmentRect.anchorMax;
        equipmentSoloPivot = equipmentRect.pivot;
        equipmentSoloPosition = equipmentRect.anchoredPosition;
        hasEquipmentSoloLayout = true;
    }

    private void RestoreEquipmentSoloLayout()
    {
        if (!hasEquipmentSoloLayout || equipmentWindow == null || IsEmbeddedEquipmentWindow()) return;

        RectTransform equipmentRect = (RectTransform)equipmentWindow.transform;
        equipmentRect.anchorMin = equipmentSoloAnchorMin;
        equipmentRect.anchorMax = equipmentSoloAnchorMax;
        equipmentRect.pivot = equipmentSoloPivot;
        equipmentRect.anchoredPosition = equipmentSoloPosition;
    }

    private void ArrangeEquipmentBesideInventory()
    {
        if (window == null || equipmentWindow == null) return;
        if (IsEmbeddedEquipmentWindow()) return;

        CaptureEquipmentSoloLayout();

        RectTransform inventoryRect = (RectTransform)window.transform;
        RectTransform equipmentRect = (RectTransform)equipmentWindow.transform;
        equipmentRect.anchorMin = inventoryRect.anchorMin;
        equipmentRect.anchorMax = inventoryRect.anchorMax;
        equipmentRect.pivot = inventoryRect.pivot;

        float inventoryWidth = GetUsableRectSize(inventoryRect).x;
        float equipmentWidth = GetUsableRectSize(equipmentRect).x;
        Vector2 target = inventoryRect.anchoredPosition + new Vector2(-(inventoryWidth + PairedWindowGap), 0f);

        RectTransform canvasRect = canvas != null ? (RectTransform)canvas.transform : null;
        if (canvasRect != null)
        {
            float canvasWidth = canvasRect.rect.width > 1f ? canvasRect.rect.width : 1280f;
            float leftEdge = target.x - equipmentWidth;
            float minLeftEdge = -canvasWidth + PairedWindowGap;
            if (leftEdge < minLeftEdge) target.x += minLeftEdge - leftEdge;
        }

        equipmentRect.anchoredPosition = target;
    }

    private bool IsEmbeddedEquipmentWindow()
    {
        return equipmentWindow != null && window != null && equipmentWindow.transform.IsChildOf(window.transform);
    }

    private void Refresh()
    {
        if (slots == null) return;
        MatchSlotCount();
        int displayedSlotCount = GetDisplayedSlotCount();
        ArrangeInventoryWindowLayout();
        ApplySlotLayout(displayedSlotCount);
        int used = CountUsed(false);
        int questUsed = CountUsed(true);
        for (int i = 0; i < slots.Length; i++)
        {
            bool visible = i < displayedSlotCount;
            slots[i].gameObject.SetActive(visible);
            if (!visible) continue;

            slots[i].ResetVisualState();
            slots[i].Refresh(GetDisplayedSlot(i));
        }
        capacityText.text = $"가방 {used} / {inventory.Capacity}     퀘스트 {questUsed} / {inventory.QuestCapacity}     분류: {GetFilterLabel(currentFilter)}";
        RefreshFilterButtons();
        RefreshQuickSlots();
        RefreshStats();
        RefreshEquipment();
        ShowDetails(-1);
    }

    private void Collected(InventoryItemDefinition item, int amount) => Notify($"{item.DisplayName} +{amount} | [I] 인벤토리");
    private void Used(InventoryItemDefinition item) => Notify($"{item.DisplayName}을 사용했습니다.");
    private void Full() => Notify("인벤토리가 가득 찼습니다.");

    private void Notify(string message)
    {
        toast.text = message;
        toast.gameObject.SetActive(true);
        toastUntil = Time.unscaledTime + 3.5f;
    }

    public void ShowDetails(int index)
    {
        ApplyDetailTextStyle();
        PlayerInventory.Slot slot = GetDisplayedSlot(index);
        detailText.text = slot == null || slot.IsEmpty
            ? "아이템 위에 마우스를 올리면 이름, 카테고리, 설명이 표시됩니다.\n소비 아이템은 더블클릭 또는 숫자키 1~3으로 사용할 수 있습니다."
            : $"<size=105%>{slot.Item.DisplayName}</size>  x{slot.Amount}\n카테고리: {slot.Item.GetCategoryLabel()}\n{slot.Item.Description}";
    }

    public void BeginDrag(int index, PointerEventData data)
    {
        if (currentFilter == Filter.Quest) return;
        PlayerInventory.Slot slot = GetDisplayedSlot(index);
        if (slot == null || slot.IsEmpty) return;
        dragSource = index;
        dragIcon.sprite = slot.Item.Icon;
        dragIcon.gameObject.SetActive(true);
        Drag(data);
    }

    public void Drag(PointerEventData data)
    {
        if (dragSource < 0) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)canvas.transform, data.position, null, out Vector2 position);
        dragIcon.rectTransform.anchoredPosition = position;
    }

    public void Drop(int target)
    {
        if (dragSource < 0 || currentFilter == Filter.Quest) return;
        int sourceSlot = GetInventorySlotIndexForDisplay(dragSource);
        int targetSlot = GetInventorySlotIndexForDisplay(target);
        if (sourceSlot >= 0 && targetSlot >= 0) inventory.Move(sourceSlot, targetSlot);
        CancelDrag();
        ResetSlotHighlights();
        ShowDetails(target);
    }

    public void DoubleClick(int index)
    {
        if (currentFilter == Filter.Quest) return;
        PlayerInventory.Slot slot = GetDisplayedSlot(index);
        if (slot == null || slot.IsEmpty) return;
        if (slot.Item.IsConsumable)
        {
            int slotIndex = GetInventorySlotIndexForDisplay(index);
            if (slotIndex >= 0) inventory.UseSlot(slotIndex, inventory.gameObject);
            return;
        }

        InventoryItemDefinition item = slot.Item;
        int equipmentSlotIndex = GetInventorySlotIndexForDisplay(index);
        if (item.IsEquipment && equipmentSlotIndex >= 0 && inventory.EquipFromSlot(equipmentSlotIndex))
        {
            Notify($"{item.DisplayName} 장착 완료");
            return;
        }

        Notify(item.IsEquipment ? "장착할 수 없는 장비입니다." : "사용할 수 없는 아이템입니다.");
    }

    public void CancelDrag()
    {
        dragSource = -1;
        if (dragIcon != null) dragIcon.gameObject.SetActive(false);
        ResetSlotHighlights();
    }

    private void ResetSlotHighlights()
    {
        if (slots == null) return;
        for (int i = 0; i < slots.Length; i++)
            if (slots[i] != null) slots[i].ResetVisualState();
    }

    public void EnsureEditableSceneUI()
    {
        if (canvas == null) CreateSceneUI();
        EnsureInventoryExtras();
        EnsureEquipmentStatsWindow(false);
        ApplyStaticLabels();
        EnsureToastStyle();
        MatchSlotCount();
        ArrangeInventoryWindowLayout();
        ApplySlotLayout(GetDisplayedSlotCount());
        ShowEditablePreview();
    }

    public void ShowEditablePreview(bool showInventoryWindow = true, bool showEquipmentStatsPanel = true)
    {
        if (Application.isPlaying) return;

        if (canvas != null) canvas.gameObject.SetActive(true);
        if (window != null) window.SetActive(showInventoryWindow);
        if (equipmentWindow != null) equipmentWindow.SetActive(showEquipmentStatsPanel);
        if (toast != null) toast.gameObject.SetActive(false);
        if (dragIcon != null) dragIcon.gameObject.SetActive(false);
    }

    public void CreateSceneUI()
    {
        if (canvas != null || inventory == null) return;
        GameObject root = new("Inventory Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        SceneManager.MoveGameObjectToScene(root, gameObject.scene);
        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = .5f;

        Image panel = Box("Inventory Window", root.transform, new Vector2(WindowWidth, WindowHeight), new Color(.45f, .25f, .12f));
        window = panel.gameObject;
        panel.rectTransform.anchorMin = panel.rectTransform.anchorMax = panel.rectTransform.pivot = new Vector2(.5f, .5f);
        panel.rectTransform.anchoredPosition = Vector2.zero;
        Shadow shadow = window.AddComponent<Shadow>();
        shadow.effectColor = new Color(.12f, .08f, .04f, .35f);
        shadow.effectDistance = new Vector2(5, -7);
        Image inside = Box("Parchment", panel.transform, new Vector2(WindowWidth - 10f, WindowHeight - 10f), new Color(.86f, .76f, .57f));
        Stretch(inside.rectTransform, new Vector2(5, 5), new Vector2(-5, -5));
        Image header = Box("Header", panel.transform, new Vector2(WindowWidth - 10f, 58f), new Color(.64f, .37f, .17f));
        TopLeft(header.rectTransform, new Vector2(4, -4));
        TMP_Text title = Label("Title", header.transform, "Inventory / 인벤토리", new Vector2(560, 56), new Vector2(20, 0), 25);
        title.color = Cream;
        closeButton = MakeButton("Close", header.transform, "X", new Vector2(36, 36), new Color(.77f, .36f, .16f), Color.white);
        TopRight((RectTransform)closeButton.transform, new Vector2(-12, -12));
        capacityText = Label("Capacity", panel.transform, "", new Vector2(InventoryContentWidth, 30), new Vector2(InventoryContentX, InfoRowY), 16);

        Image viewport = Box("Slot Viewport", panel.transform, new Vector2(InventoryContentWidth, SlotViewportHeight), new Color(.58f, .43f, .28f));
        TopLeft(viewport.rectTransform, new Vector2(InventoryContentX, SlotViewportY));
        viewport.gameObject.AddComponent<RectMask2D>();
        RectTransform content = new GameObject("Slots", typeof(RectTransform)).GetComponent<RectTransform>();
        content.SetParent(viewport.transform, false);
        int slotCount = Mathf.Max(BaseSlotCount, inventory.Capacity, inventory.QuestCapacity);
        content.sizeDelta = new Vector2(InventoryContentWidth, Mathf.CeilToInt(slotCount / 5f) * 92f);
        TopLeft(content, Vector2.zero);
        ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport.rectTransform;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30;
        slots = new InventorySlotUI[slotCount];
        for (int i = 0; i < slots.Length; i++)
        {
            Image cell = Box($"Slot {i + 1:00}", content, new Vector2(86, 86), new Color(.95f, .87f, .69f));
            TopLeft(cell.rectTransform, new Vector2(i % 5 * 94, -(i / 5) * 94));
            Image icon = Box("Item Icon", cell.transform, new Vector2(42, 42), Color.white);
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            icon.enabled = false;
            TMP_Text amount = Label("Amount", cell.transform, "", new Vector2(78, 22), new Vector2(4, -58), 15);
            amount.alignment = TextAlignmentOptions.BottomRight;
            amount.color = Ink;
            slots[i] = cell.gameObject.AddComponent<InventorySlotUI>();
            slots[i].Initialize(this, i, icon, amount);
        }

        detailText = Label("Item Details", panel.transform, "", new Vector2(InventoryContentWidth, 54), new Vector2(InventoryContentX, DetailTextY), 15);
        detailText.alignment = TextAlignmentOptions.TopLeft;
        detailText.textWrappingMode = TextWrappingModes.Normal;

        shortcutButton = MakeButton("Inventory Shortcut", root.transform, "가방 [I]", new Vector2(144, 42), new Color(.29f, .21f, .15f), Cream);
        RectTransform shortcutRect = (RectTransform)shortcutButton.transform;
        shortcutRect.anchorMin = shortcutRect.anchorMax = shortcutRect.pivot = new Vector2(1, 0);
        shortcutRect.anchoredPosition = new Vector2(-24, 20);
        toast = Label("Pickup Message", root.transform, "", new Vector2(800, 44), Vector2.zero, 20);
        toast.rectTransform.anchorMin = toast.rectTransform.anchorMax = toast.rectTransform.pivot = new Vector2(.5f, 1);
        toast.rectTransform.anchoredPosition = new Vector2(0, -16);
        toast.alignment = TextAlignmentOptions.Center;
        toast.color = Cream;
        toast.outlineWidth = .2f;
        toast.outlineColor = Ink;
        toast.gameObject.SetActive(false);
        EnsureToastStyle();
        dragIcon = Box("Dragged Item", root.transform, new Vector2(48, 48), new Color(1, 1, 1, .85f));
        dragIcon.raycastTarget = false;
        dragIcon.preserveAspect = true;
        dragIcon.gameObject.SetActive(false);
        ArrangeInventoryWindowLayout();
    }

    private void EnsureInventoryExtras()
    {
        if (window == null) return;
        Transform parent = window.transform;
        EnsureFrame(parent, "Left Column Frame", new Vector2(LeftPanelWidth, LeftPanelHeight), new Vector2(LeftPanelX, LeftPanelY), new Color(.70f, .57f, .39f));
        EnsureFrame(parent, "Category Frame", new Vector2(CategoryWidth, LeftPanelHeight), new Vector2(CategoryX, LeftPanelY), new Color(.55f, .36f, .22f));
        EnsureFrame(parent, "Inventory Frame", new Vector2(InventoryContentWidth + 18f, LeftPanelHeight), new Vector2(InventoryContentX - 9f, LeftPanelY), new Color(.72f, .60f, .43f));

        if (filterButtons == null || filterButtons.Length != 5 || filterButtons[0] == null)
        {
            filterButtons = new Button[5];
            string[] labels = { "전체", "잡화", "소비", "장비", "퀘스트" };
            for (int i = 0; i < filterButtons.Length; i++)
            {
                filterButtons[i] = MakeButton("Filter " + labels[i], parent, labels[i], new Vector2(94, 42), new Color(.45f, .30f, .18f), Cream);
                TopLeft((RectTransform)filterButtons[i].transform, new Vector2(CategoryX + 10f, CategoryY - i * 50f));
            }
        }

        if (quickSlotText == null)
        {
            quickSlotText = Label("Quick Slots", parent, "", new Vector2(InventoryContentWidth, 26), new Vector2(InventoryContentX, QuickSlotY), 14);
        }
    }

    private void ArrangeInventoryWindowLayout()
    {
        if (window == null) return;

        RectTransform windowRect = (RectTransform)window.transform;
        windowRect.sizeDelta = new Vector2(WindowWidth, WindowHeight);
        windowRect.anchorMin = windowRect.anchorMax = windowRect.pivot = new Vector2(.5f, .5f);

        if (capacityText != null)
        {
            capacityText.rectTransform.sizeDelta = new Vector2(InventoryContentWidth, 30f);
            TopLeft(capacityText.rectTransform, new Vector2(InventoryContentX, InfoRowY));
        }

        ArrangeFilterTabs();

        RectTransform viewport = GetSlotViewport();
        if (viewport != null)
        {
            viewport.sizeDelta = new Vector2(InventoryContentWidth, SlotViewportHeight);
            TopLeft(viewport, new Vector2(InventoryContentX, SlotViewportY));

            RectTransform content = GetSlotContent();
            if (content != null)
            {
                content.anchorMin = content.anchorMax = content.pivot = new Vector2(0f, 1f);
                content.anchoredPosition = Vector2.zero;
                content.sizeDelta = viewport.rect.size.x > 1f
                    ? viewport.rect.size
                    : new Vector2(InventoryContentWidth, SlotViewportHeight);
            }
        }

        if (detailText != null)
        {
            detailText.rectTransform.sizeDelta = new Vector2(InventoryContentWidth, 56f);
            TopLeft(detailText.rectTransform, new Vector2(InventoryContentX, DetailTextY));
        }

        if (quickSlotText != null)
        {
            quickSlotText.rectTransform.sizeDelta = new Vector2(InventoryContentWidth, 26f);
            TopLeft(quickSlotText.rectTransform, new Vector2(InventoryContentX, QuickSlotY));
        }
    }

    private void ArrangeFilterTabs()
    {
        if (filterButtons == null) return;

        for (int i = 0; i < filterButtons.Length; i++)
        {
            if (filterButtons[i] == null) continue;

            RectTransform rect = (RectTransform)filterButtons[i].transform;
            rect.sizeDelta = new Vector2(94f, 42f);
            TopLeft(rect, new Vector2(CategoryX + 10f, FilterTabsY - i * 50f));
        }
    }

    private void RefreshFilterButtons()
    {
        if (filterButtons == null) return;

        for (int i = 0; i < filterButtons.Length; i++)
        {
            if (filterButtons[i] == null) continue;
            bool selected = (int)currentFilter == i;
            Image image = filterButtons[i].targetGraphic as Image;
            if (image != null) image.color = selected ? new Color(.87f, .60f, .25f) : new Color(.45f, .30f, .18f);
            TMP_Text label = filterButtons[i].GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                label.color = selected ? Ink : Cream;
                label.fontStyle = FontStyles.Normal;
                label.fontWeight = FontWeight.Regular;
            }
        }
    }

    private void EnsureEquipmentStatsWindow(bool hideAfterCreate = true)
    {
        if (window == null) return;

        if (equipmentWindow != null && !equipmentWindow.transform.IsChildOf(window.transform))
        {
            equipmentWindow.SetActive(false);
            equipmentWindow = null;
            equipmentCloseButton = null;
        }

        Transform existing = window.transform.Find("Character Equipment Panel");
        if (existing == null)
        {
            CreateEquipmentPanel(window.transform);
            existing = window.transform.Find("Character Equipment Panel");
        }

        equipmentWindow = existing != null ? existing.gameObject : equipmentWindow;

        if (equipmentRows == null || equipmentRows.Length != 6 || equipmentRows[0] == null || !equipmentRows[0].transform.IsChildOf(equipmentWindow.transform)
            || equipmentUnequipButtons == null || equipmentUnequipButtons.Length != 6 || equipmentUnequipButtons[0] == null
            || equipmentSlotIcons == null || equipmentSlotIcons.Length != 6 || equipmentSlotIcons[0] == null)
        {
            CreateEquipmentPanel(window.transform);
        }

        if (statPointText == null || statRows == null || statRows.Length != 5 || statRows[0] == null
            || statUpgradeButtons == null || statUpgradeButtons.Length != 5 || statUpgradeButtons[0] == null
            || statBarFills == null || statBarFills.Length != 5 || statBarFills[0] == null || statMessageText == null)
        {
            CreateStatsPanel(window.transform);
        }

        ArrangeStatsPanelLayout();
        if (equipmentWindow != null) equipmentWindow.SetActive(true);
    }

    private GameObject CreateEquipmentStatsWindow()
    {
        Image panel = Box("Equipment Stats Panel", canvas.transform, new Vector2(360, 520), new Color(.68f, .40f, .19f));
        TopRight(panel.rectTransform, new Vector2(-24, -70));
        Shadow shadow = panel.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(.12f, .08f, .04f, .35f);
        shadow.effectDistance = new Vector2(5, -7);
        Image inside = Box("Parchment", panel.transform, new Vector2(352, 512), Cream);
        Stretch(inside.rectTransform, new Vector2(4, 4), new Vector2(-4, -4));
        Image header = Box("Header", panel.transform, new Vector2(352, 60), new Color(.94f, .62f, .22f));
        TopLeft(header.rectTransform, new Vector2(4, -4));
        Label("Title", header.transform, "EQUIPMENT / STATS", new Vector2(268, 56), new Vector2(18, 0), 22);
        equipmentCloseButton = MakeButton("Close", header.transform, "X", new Vector2(36, 36), new Color(.77f, .36f, .16f), Color.white);
        TopRight((RectTransform)equipmentCloseButton.transform, new Vector2(-12, -12));
        return panel.gameObject;
    }

    private void CreateEquipmentPanel(Transform parent)
    {
        Transform old = parent.Find("Character Equipment Panel");
        if (old != null) DestroyUiObject(old.gameObject);

        Image equipmentPanel = Box("Character Equipment Panel", parent, new Vector2(LeftPanelWidth, 292), new Color(.78f, .65f, .46f));
        equipmentWindow = equipmentPanel.gameObject;
        TopLeft(equipmentPanel.rectTransform, new Vector2(LeftPanelX, LeftPanelY));
        Label("Equipment Title", equipmentPanel.transform, "장비", new Vector2(80, 26), new Vector2(14, -10), 18);

        Image preview = Box("Player Preview", equipmentPanel.transform, new Vector2(122, 176), new Color(.50f, .36f, .24f));
        TopLeft(preview.rectTransform, new Vector2(82, -52));
        TMP_Text previewText = Label("Preview Label", preview.transform, "PLAYER", new Vector2(112, 22), new Vector2(5, -142), 13);
        previewText.alignment = TextAlignmentOptions.Center;
        previewText.color = Cream;
        Image body = Box("Preview Silhouette", preview.transform, new Vector2(46, 88), new Color(.24f, .17f, .12f, .88f));
        body.rectTransform.anchorMin = body.rectTransform.anchorMax = body.rectTransform.pivot = new Vector2(.5f, .5f);
        body.rectTransform.anchoredPosition = new Vector2(0, 18);
        Image head = Box("Preview Head", preview.transform, new Vector2(36, 36), new Color(.31f, .22f, .16f, .9f));
        head.rectTransform.anchorMin = head.rectTransform.anchorMax = head.rectTransform.pivot = new Vector2(.5f, .5f);
        head.rectTransform.anchoredPosition = new Vector2(0, 74);

        equipmentRows = new TMP_Text[6];
        equipmentUnequipButtons = new Button[6];
        equipmentSlotIcons = new Image[6];
        string[] labels = { "무기", "방어구", "장신구", "머리", "신발", "기타" };
        Vector2[] positions =
        {
            new(14, -52), new(14, -140), new(14, -228),
            new(214, -52), new(214, -140), new(214, -228)
        };
        for (int i = 0; i < equipmentRows.Length; i++)
        {
            Button slotButton = MakeButton("Equipment Slot " + i, equipmentPanel.transform, "", new Vector2(58, 58), new Color(.34f, .23f, .15f), Cream);
            TopLeft((RectTransform)slotButton.transform, positions[i]);
            equipmentUnequipButtons[i] = slotButton;

            Image icon = Box("Item Icon", slotButton.transform, new Vector2(38, 38), new Color(1f, 1f, 1f, .9f));
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            icon.enabled = false;
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = icon.rectTransform.pivot = new Vector2(.5f, .5f);
            icon.rectTransform.anchoredPosition = new Vector2(0, 6);
            equipmentSlotIcons[i] = icon;

            equipmentRows[i] = Label("Equipment Row " + i, slotButton.transform, labels[i], new Vector2(56, 18), new Vector2(1, -39), 10);
            equipmentRows[i].alignment = TextAlignmentOptions.Center;
            equipmentRows[i].color = Cream;
        }
    }

    private void CreateStatsPanel(Transform parent)
    {
        Transform old = parent.Find("Stats Panel");
        if (old != null) DestroyUiObject(old.gameObject);

        Image statPanel = Box("Stats Panel", parent, new Vector2(LeftPanelWidth, 184), new Color(.84f, .73f, .55f));
        TopLeft(statPanel.rectTransform, new Vector2(LeftPanelX, -386));
        Label("Stats Title", statPanel.transform, "스탯", new Vector2(80, 24), new Vector2(14, -8), 18);
        statPointText = Label("Stat Points", statPanel.transform, "", new Vector2(172, 22), new Vector2(96, -10), 12);
        statPointText.alignment = TextAlignmentOptions.MidlineRight;
        statRows = new TMP_Text[5];
        statUpgradeButtons = new Button[5];
        statBarFills = new Image[5];
        for (int i = 0; i < 5; i++)
        {
            statRows[i] = Label("Stat Row " + i, statPanel.transform, "", new Vector2(72, 20), new Vector2(14, -42 - i * 26), 12);
            Image barBack = Box("Stat Bar Back " + i, statPanel.transform, new Vector2(132, 12), new Color(.43f, .29f, .18f));
            TopLeft(barBack.rectTransform, new Vector2(88, -46 - i * 26));
            Image barFill = Box("Stat Bar Fill " + i, barBack.transform, new Vector2(10, 12), new Color(.93f, .66f, .28f));
            Stretch(barFill.rectTransform, Vector2.zero, new Vector2(-90, 0));
            statBarFills[i] = barFill;
            statUpgradeButtons[i] = MakeButton("Upgrade Stat " + i, statPanel.transform, "+", new Vector2(24, 22), new Color(.35f, .50f, .31f), Color.white);
            TopLeft((RectTransform)statUpgradeButtons[i].transform, new Vector2(236, -42 - i * 26));
        }

        statMessageText = Label("Stat Result", statPanel.transform, "", new Vector2(252, 20), new Vector2(14, -164), 11);
        statMessageText.textWrappingMode = TextWrappingModes.NoWrap;
        statMessageText.overflowMode = TextOverflowModes.Ellipsis;
        ArrangeStatsPanelLayout();
    }

    private void ArrangeStatsPanelLayout()
    {
        Transform statPanel = statPointText != null ? statPointText.transform.parent : null;
        if (statPanel == null && statMessageText != null) statPanel = statMessageText.transform.parent;
        if (statPanel == null) return;

        TMP_Text title = statPanel.Find("Stats Title")?.GetComponent<TMP_Text>();
        if (title != null)
        {
            title.text = "스탯";
            title.rectTransform.sizeDelta = new Vector2(80, 24);
            TopLeft(title.rectTransform, new Vector2(14, -8));
        }

        if (statMessageText != null)
        {
            statMessageText.fontSize = 11;
            statMessageText.alignment = TextAlignmentOptions.MidlineLeft;
            statMessageText.textWrappingMode = TextWrappingModes.NoWrap;
            statMessageText.rectTransform.sizeDelta = new Vector2(252, 20);
            TopLeft(statMessageText.rectTransform, new Vector2(14, -164));
        }

        if (statPointText != null)
        {
            statPointText.rectTransform.sizeDelta = new Vector2(172, 22);
            statPointText.alignment = TextAlignmentOptions.MidlineRight;
            TopLeft(statPointText.rectTransform, new Vector2(96, -10));
        }

        if (statRows != null)
        {
            for (int i = 0; i < statRows.Length; i++)
            {
                if (statRows[i] == null) continue;
                statRows[i].rectTransform.sizeDelta = new Vector2(72, 20);
                TopLeft(statRows[i].rectTransform, new Vector2(14, -42 - i * 26));
            }
        }

        if (statUpgradeButtons == null) return;
        for (int i = 0; i < statUpgradeButtons.Length; i++)
        {
            if (statUpgradeButtons[i] == null) continue;
            RectTransform rect = (RectTransform)statUpgradeButtons[i].transform;
            rect.sizeDelta = new Vector2(24, 22);
            TopLeft(rect, new Vector2(236, -42 - i * 26));
        }
    }

    private void ApplyStaticLabels()
    {
        SetButtonLabel(shortcutButton, "가방 [I]");

        TMP_Text inventoryTitle = window != null ? window.transform.Find("Header/Title")?.GetComponent<TMP_Text>() : null;
        if (inventoryTitle != null) inventoryTitle.text = "Inventory / 인벤토리";

        TMP_Text equipmentTitle = equipmentWindow != null ? equipmentWindow.transform.Find("Header/Title")?.GetComponent<TMP_Text>() : null;
        if (equipmentTitle != null) equipmentTitle.text = "EQUIPMENT / STATS";

        ApplyDetailTextStyle();
    }

    private void EnsureToastStyle()
    {
        if (toast == null) return;

        Transform background = toast.transform.parent != null ? toast.transform.parent.Find("Pickup Message Back") : null;
        if (background != null) DestroyUiObject(background.gameObject);

        if (font != null) toast.font = font;
        toast.fontSize = 20;
        toast.fontStyle = FontStyles.Normal;
        toast.fontWeight = FontWeight.Regular;
        toast.alignment = TextAlignmentOptions.Center;
        toast.color = Cream;
        toast.outlineWidth = .2f;
        toast.outlineColor = Ink;
    }

    private void AddButtonListeners()
    {
        if (filterButtons != null && filterButtons.Length >= 5)
        {
            filterButtons[0].onClick.AddListener(() => SetFilter(Filter.All));
            filterButtons[1].onClick.AddListener(() => SetFilter(Filter.Misc));
            filterButtons[2].onClick.AddListener(() => SetFilter(Filter.Consumable));
            filterButtons[3].onClick.AddListener(() => SetFilter(Filter.Equipment));
            filterButtons[4].onClick.AddListener(() => SetFilter(Filter.Quest));
        }

        if (statUpgradeButtons != null && statUpgradeButtons.Length >= 5)
        {
            statUpgradeButtons[0].onClick.AddListener(() => Upgrade(GetUiStatType(0)));
            statUpgradeButtons[1].onClick.AddListener(() => Upgrade(GetUiStatType(1)));
            statUpgradeButtons[2].onClick.AddListener(() => Upgrade(GetUiStatType(2)));
            statUpgradeButtons[3].onClick.AddListener(() => Upgrade(GetUiStatType(3)));
            statUpgradeButtons[4].onClick.AddListener(() => Upgrade(GetUiStatType(4)));
        }

        if (equipmentUnequipButtons != null && equipmentUnequipButtons.Length >= 6)
        {
            equipmentUnequipButtons[0].onClick.AddListener(() => Unequip(InventoryItemDefinition.EquipmentSlot.Weapon));
            equipmentUnequipButtons[1].onClick.AddListener(() => Unequip(InventoryItemDefinition.EquipmentSlot.Armor));
            equipmentUnequipButtons[2].onClick.AddListener(() => Unequip(InventoryItemDefinition.EquipmentSlot.Accessory));
            equipmentUnequipButtons[3].onClick.AddListener(() => Unequip(InventoryItemDefinition.EquipmentSlot.Head));
            equipmentUnequipButtons[4].onClick.AddListener(() => Unequip(InventoryItemDefinition.EquipmentSlot.Shoes));
            equipmentUnequipButtons[5].onClick.AddListener(() => Unequip(InventoryItemDefinition.EquipmentSlot.Other));
        }
    }

    private void SetFilter(Filter filter)
    {
        currentFilter = filter;
        CancelDrag();
        Refresh();
    }

    private void Upgrade(PersonalStatType type)
    {
        if (stats == null)
        {
            SetStatMessage("PlayerStats 컴포넌트가 필요합니다.");
            return;
        }

        stats.TryUpgrade(type, out string message);
        SetStatMessage(message);
        Refresh();
    }

    private void Unequip(InventoryItemDefinition.EquipmentSlot slot)
    {
        if (inventory.Unequip(slot))
        {
            Notify($"{GetEquipmentSlotLabel(slot)} 해제 완료");
            Refresh();
            return;
        }

        Notify("해제할 장비가 없거나 가방이 가득 찼습니다.");
    }

    private void UseQuickSlot(int index)
    {
        if (inventory.UseQuickSlot(index, inventory.gameObject)) return;
        Notify($"퀵슬롯 {index + 1}에 사용할 소비 아이템이 없습니다.");
    }

    private PlayerInventory.Slot GetDisplayedSlot(int index)
    {
        if (index < 0) return null;
        if (currentFilter == Filter.Quest) return inventory.GetQuestSlot(index);
        int inventoryIndex = GetInventorySlotIndexForDisplay(index);
        return inventoryIndex >= 0 ? inventory.GetSlot(inventoryIndex) : null;
    }

    private int GetInventorySlotIndexForDisplay(int displayIndex)
    {
        if (displayIndex < 0) return -1;
        if (currentFilter == Filter.All) return displayIndex < inventory.Capacity ? displayIndex : -1;

        int logicalIndex = 0;
        for (int i = 0; i < inventory.Capacity; i++)
        {
            PlayerInventory.Slot slot = inventory.GetSlot(i);
            if (slot == null || slot.IsEmpty || !SlotMatchesFilter(slot)) continue;
            if (logicalIndex == displayIndex) return i;
            logicalIndex++;
        }

        for (int i = 0; i < inventory.Capacity; i++)
        {
            PlayerInventory.Slot slot = inventory.GetSlot(i);
            if (slot == null || !slot.IsEmpty) continue;
            if (logicalIndex == displayIndex) return i;
            logicalIndex++;
        }

        return -1;
    }

    private bool SlotMatchesFilter(PlayerInventory.Slot slot)
    {
        if (slot == null || slot.IsEmpty) return true;
        return currentFilter switch
        {
            Filter.Misc => slot.Item.Category == InventoryItemDefinition.ItemCategory.Misc,
            Filter.Consumable => slot.Item.Category == InventoryItemDefinition.ItemCategory.Consumable,
            Filter.Equipment => slot.Item.Category == InventoryItemDefinition.ItemCategory.Equipment,
            _ => true
        };
    }

    private int CountUsed(bool quest)
    {
        int count = quest ? inventory.QuestCapacity : inventory.Capacity;
        int used = 0;
        for (int i = 0; i < count; i++)
        {
            PlayerInventory.Slot slot = quest ? inventory.GetQuestSlot(i) : inventory.GetSlot(i);
            if (slot != null && !slot.IsEmpty) used++;
        }
        return used;
    }

    private void RefreshQuickSlots()
    {
        if (quickSlotText == null) return;
        string text = "퀵슬롯  ";
        for (int i = 0; i < inventory.QuickSlotCount; i++)
        {
            InventoryItemDefinition item = inventory.GetQuickSlot(i);
            text += $"[{i + 1}] {(item != null ? item.DisplayName : "-")}  ";
        }
        quickSlotText.text = text;
    }

    private void RefreshStats()
    {
        if (statPointText == null || statRows == null) return;
        if (stats == null)
        {
            statPointText.text = "PlayerStats 없음";
            return;
        }

        statPointText.text = $"POINT {stats.AvailablePoints}";
        for (int i = 0; i < statRows.Length; i++)
        {
            PersonalStatType type = GetUiStatType(i);
            int value = stats.GetValue(type);
            statRows[i].text = $"{GetUiStatLabel(i)} {value}";
            if (statBarFills != null && i < statBarFills.Length && statBarFills[i] != null)
            {
                RectTransform fill = statBarFills[i].rectTransform;
                fill.anchorMin = Vector2.zero;
                fill.anchorMax = new Vector2(Mathf.Clamp01(value / Mathf.Max(1f, stats.StatSpreadLimit + 5f)), 1f);
                fill.offsetMin = Vector2.zero;
                fill.offsetMax = Vector2.zero;
            }
        }
    }

    private void RefreshEquipment()
    {
        if (equipmentRows == null || equipmentRows.Length < 6) return;

        SetEquipmentRow(0, InventoryItemDefinition.EquipmentSlot.Weapon);
        SetEquipmentRow(1, InventoryItemDefinition.EquipmentSlot.Armor);
        SetEquipmentRow(2, InventoryItemDefinition.EquipmentSlot.Accessory);
        SetEquipmentRow(3, InventoryItemDefinition.EquipmentSlot.Head);
        SetEquipmentRow(4, InventoryItemDefinition.EquipmentSlot.Shoes);
        SetEquipmentRow(5, InventoryItemDefinition.EquipmentSlot.Other);
    }

    private void SetEquipmentRow(int index, InventoryItemDefinition.EquipmentSlot slot)
    {
        if (index < 0 || index >= equipmentRows.Length || equipmentRows[index] == null) return;

        InventoryItemDefinition item = inventory.GetEquipped(slot);
        equipmentRows[index].text = item != null ? item.DisplayName : GetEquipmentSlotLabel(slot);
        if (equipmentUnequipButtons != null && index < equipmentUnequipButtons.Length && equipmentUnequipButtons[index] != null)
            equipmentUnequipButtons[index].interactable = item != null;
        if (equipmentSlotIcons != null && index < equipmentSlotIcons.Length && equipmentSlotIcons[index] != null)
        {
            equipmentSlotIcons[index].enabled = item != null;
            equipmentSlotIcons[index].sprite = item != null ? item.Icon : null;
        }
    }

    private void SetStatMessage(string message)
    {
        if (statMessageText != null) statMessageText.text = message;
        if (!string.IsNullOrEmpty(message)) Notify(message);
    }

    private void ApplyDetailTextStyle()
    {
        if (detailText == null) return;

        if (detailFont != null) detailText.font = detailFont;
        detailText.fontStyle = FontStyles.Normal;
        detailText.fontWeight = FontWeight.Regular;
        detailText.richText = true;
    }

    private void MatchSlotCount()
    {
        int count = Mathf.Max(inventory.Capacity, inventory.QuestCapacity);
        int previousCount = slots.Length;
        if (count == previousCount) return;
        RectTransform content = (RectTransform)slots[0].transform.parent;
        for (int i = count; i < previousCount; i++) slots[i].gameObject.SetActive(false);
        ArrayUtilityResize(ref slots, count);
        for (int i = previousCount; i < count; i++)
        {
            slots[i] = Instantiate(slots[0], content);
            slots[i].name = $"Slot {i + 1:00}";
            TopLeft((RectTransform)slots[i].transform, new Vector2(i % 6 * 72, -(i / 6) * 72));
        }

        for (int i = 0; i < count; i++)
        {
            slots[i].gameObject.SetActive(true);
            slots[i].Bind(this, i);
        }
    }

    private static void ArrayUtilityResize(ref InventorySlotUI[] target, int size) => System.Array.Resize(ref target, size);

    private int GetDisplayedSlotCount()
    {
        return currentFilter == Filter.Quest ? inventory.QuestCapacity : inventory.Capacity;
    }

    private void ApplySlotLayout(int slotCount)
    {
        if (slotCount <= 0 || slots == null || slots.Length == 0 || slots[0] == null) return;

        RectTransform content = GetSlotContent();
        if (content == null) return;

        RectTransform viewport = GetSlotViewport();
        Vector2 area = GetUsableRectSize(viewport != null ? viewport : content);
        content.anchorMin = content.anchorMax = content.pivot = new Vector2(0f, 1f);
        content.anchoredPosition = Vector2.zero;
        SlotGrid grid = CalculateSlotGrid(slotCount, area);
        float contentHeight = Mathf.Max(area.y, grid.Rows * grid.CellSize + (grid.Rows - 1) * grid.Spacing + SlotPadding * 2f);
        content.sizeDelta = new Vector2(area.x, contentHeight);
        GridLayoutGroup layout = EnsureSlotGridLayoutGroup(content);
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = grid.Columns;
        layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
        layout.startAxis = GridLayoutGroup.Axis.Horizontal;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.spacing = Vector2.one * grid.Spacing;
        layout.padding = new RectOffset(
            Mathf.RoundToInt(SlotPadding),
            Mathf.RoundToInt(SlotPadding),
            Mathf.RoundToInt(SlotPadding),
            Mathf.RoundToInt(SlotPadding));
        layout.cellSize = Vector2.one * grid.CellSize;

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null) continue;
            bool visible = i < slotCount;
            slots[i].gameObject.SetActive(visible);
            if (!visible) continue;

            slots[i].SetVisualSize(grid.CellSize);
            slots[i].Bind(this, i);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
    }

    private static SlotGrid CalculateSlotGrid(int slotCount, Vector2 area)
    {
        int columns = area.x >= 560f ? 6 : 5;
        columns = Mathf.Clamp(columns, 1, Mathf.Max(1, slotCount));
        int rows = Mathf.CeilToInt(slotCount / (float)columns);
        float spacing = SlotSpacing;
        float availableWidth = area.x - SlotPadding * 2f - spacing * (columns - 1);
        float cellSize = Mathf.Clamp(Mathf.Floor(availableWidth / columns), MinSlotSize, MaxSlotSize);
        return new SlotGrid(columns, rows, cellSize, spacing);
    }

    private static float CalculateSlotSpacing(Vector2 area, int columns, int rows)
    {
        float widthSpacing = columns > 1
            ? (area.x - SlotPadding * 2f - columns * MinSlotSize) / (columns - 1)
            : SlotSpacing;
        float heightSpacing = rows > 1
            ? (area.y - SlotPadding * 2f - rows * MinSlotSize) / (rows - 1)
            : SlotSpacing;
        return Mathf.Clamp(Mathf.Min(SlotSpacing, widthSpacing, heightSpacing), MinSlotSpacing, SlotSpacing);
    }

    private static Vector2 GetUsableRectSize(RectTransform rect)
    {
        if (rect == null) return new Vector2(432f, 300f);
        Vector2 size = rect.rect.size;
        if (size.x <= 1f) size.x = Mathf.Abs(rect.sizeDelta.x);
        if (size.y <= 1f) size.y = Mathf.Abs(rect.sizeDelta.y);
        if (size.x <= 1f) size.x = 432f;
        if (size.y <= 1f) size.y = 300f;
        return size;
    }

    private RectTransform GetSlotContent()
    {
        return slots != null && slots.Length > 0 && slots[0] != null
            ? slots[0].transform.parent as RectTransform
            : null;
    }

    private RectTransform GetSlotViewport()
    {
        RectTransform content = GetSlotContent();
        if (content != null && content.parent is RectTransform viewport) return viewport;

        Transform found = window != null ? window.transform.Find("Slot Viewport") : null;
        return found as RectTransform;
    }

    private GridLayoutGroup EnsureSlotGridLayoutGroup(RectTransform content)
    {
        ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
        if (fitter != null) DestroyUiComponent(fitter);

        HorizontalOrVerticalLayoutGroup linearLayout = content.GetComponent<HorizontalOrVerticalLayoutGroup>();
        if (linearLayout != null) DestroyUiComponent(linearLayout);

        GridLayoutGroup grid = content.GetComponent<GridLayoutGroup>();
        if (grid == null) grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.enabled = true;
        return grid;
    }

    private readonly struct SlotGrid
    {
        public readonly int Columns;
        public readonly int Rows;
        public readonly float CellSize;
        public readonly float Spacing;

        public SlotGrid(int columns, int rows, float cellSize, float spacing)
        {
            Columns = columns;
            Rows = rows;
            CellSize = cellSize;
            Spacing = spacing;
        }
    }

    private bool IsInInventoryWindow(Component component)
    {
        return component != null && window != null && component.transform.IsChildOf(window.transform);
    }

    private static void HideChildPanel(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child != null) child.gameObject.SetActive(false);
    }

    private static void EnsureFrame(Transform parent, string name, Vector2 size, Vector2 position, Color color)
    {
        Transform existing = parent.Find(name);
        Image image = existing != null ? existing.GetComponent<Image>() : null;
        if (image == null) image = Box(name, parent, size, color);

        image.color = color;
        image.raycastTarget = false;
        image.rectTransform.sizeDelta = size;
        TopLeft(image.rectTransform, position);
        Transform parchment = parent.Find("Parchment");
        image.transform.SetSiblingIndex(parchment != null ? parchment.GetSiblingIndex() + 1 : 0);
    }

    private static void DestroyUiObject(GameObject obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }

    private static void DestroyUiComponent(Component component)
    {
        if (component == null) return;
        if (Application.isPlaying) Destroy(component);
        else DestroyImmediate(component);
    }

    private static string GetFilterLabel(Filter filter)
    {
        return filter switch
        {
            Filter.Misc => "잡화",
            Filter.Consumable => "소비",
            Filter.Equipment => "장비",
            Filter.Quest => "퀘스트",
            _ => "전체"
        };
    }

    private static string GetEquipmentSlotLabel(InventoryItemDefinition.EquipmentSlot slot)
    {
        return slot switch
        {
            InventoryItemDefinition.EquipmentSlot.Weapon => "무기",
            InventoryItemDefinition.EquipmentSlot.Armor => "방어구",
            InventoryItemDefinition.EquipmentSlot.Accessory => "장신구",
            InventoryItemDefinition.EquipmentSlot.Head => "머리",
            InventoryItemDefinition.EquipmentSlot.Shoes => "신발",
            InventoryItemDefinition.EquipmentSlot.Other => "기타",
            _ => "장비"
        };
    }

    // The visible RPG labels map onto the project-specific stat model used by PlayerStats.
    private static PersonalStatType GetUiStatType(int index)
    {
        return index switch
        {
            0 => PersonalStatType.Persuasion,
            1 => PersonalStatType.Evasion,
            2 => PersonalStatType.Attack,
            3 => PersonalStatType.Defense,
            4 => PersonalStatType.Stealth,
            _ => PersonalStatType.Attack
        };
    }

    private static string GetUiStatLabel(int index)
    {
        return index switch
        {
            0 => "PRS",
            1 => "EVA",
            2 => "ATK",
            3 => "DEF",
            4 => "STL",
            _ => "STAT"
        };
    }

    private static void EnsureEventSystem()
    {
        EventSystem events = FindFirstObjectByType<EventSystem>();
        if (events == null) events = new GameObject("Inventory EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
        if (events.GetComponent<BaseInputModule>() == null)
            events.gameObject.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
    }

    private static Image Box(string name, Transform parent, Vector2 size, Color color)
    {
        GameObject obj = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        obj.transform.SetParent(parent, false);
        Image image = obj.GetComponent<Image>();
        image.rectTransform.sizeDelta = size;
        image.color = color;
        return image;
    }

    private TMP_Text Label(string name, Transform parent, string value, Vector2 size, Vector2 position, int fontSize)
    {
        GameObject obj = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        TMP_Text label = obj.GetComponent<TMP_Text>();
        if (font != null) label.font = font;
        label.text = value;
        label.fontSize = fontSize;
        label.color = Ink;
        label.raycastTarget = false;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.rectTransform.sizeDelta = size;
        TopLeft(label.rectTransform, position);
        return label;
    }

    private Button MakeButton(string name, Transform parent, string text, Vector2 size, Color background, Color foreground)
    {
        Image image = Box(name, parent, size, background);
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        Navigation navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;
        TMP_Text label = Label("Label", image.transform, text, size, Vector2.zero, 18);
        Stretch(label.rectTransform, Vector2.zero, Vector2.zero);
        label.alignment = TextAlignmentOptions.Center;
        label.color = foreground;
        return button;
    }

    private static void SetButtonLabel(Button button, string text)
    {
        if (button == null) return;
        TMP_Text label = button.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = text;
    }

    private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = min;
        rect.offsetMax = max;
    }

    private static void TopLeft(RectTransform rect, Vector2 position)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = position;
    }

    private static void TopRight(RectTransform rect, Vector2 position)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        rect.anchoredPosition = position;
    }
}
