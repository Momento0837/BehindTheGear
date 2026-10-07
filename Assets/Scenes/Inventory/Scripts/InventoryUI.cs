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
    [SerializeField] private TMP_Text[] equipmentRows;
    [SerializeField] private Button[] equipmentUnequipButtons;

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
    private const float InventoryContentX = 28f;
    private const float InventoryContentWidth = 432f;
    private const float InfoRowY = -72f;
    private const float FilterTabsY = -112f;
    private const float SlotViewportY = -154f;
    private const float SlotViewportHeight = 278f;
    private const float DetailTextY = -444f;
    private const float QuickSlotY = -526f;
    private const float PairedWindowGap = 16f;
    private const float SlotPadding = 8f;
    private const float SlotSpacing = 8f;
    private const float MinSlotSpacing = 2f;
    private const float MinSlotSize = 32f;
    private const float MaxSlotSize = 82f;

    public bool HasSceneUI => canvas != null;
    public bool CanCreateSceneUI => inventory != null;
    public Canvas SceneCanvas => canvas;
    public GameObject Window => window;
    public Button ShortcutButton => shortcutButton;
    public GameObject EquipmentStatsPanel => equipmentWindow;

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
        if (equipmentWindow != null) equipmentWindow.SetActive(false);
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
        return (window != null && window.activeSelf) || (equipmentWindow != null && equipmentWindow.activeSelf);
    }

    private void CaptureEquipmentSoloLayout()
    {
        if (hasEquipmentSoloLayout || equipmentWindow == null) return;

        RectTransform equipmentRect = (RectTransform)equipmentWindow.transform;
        equipmentSoloAnchorMin = equipmentRect.anchorMin;
        equipmentSoloAnchorMax = equipmentRect.anchorMax;
        equipmentSoloPivot = equipmentRect.pivot;
        equipmentSoloPosition = equipmentRect.anchoredPosition;
        hasEquipmentSoloLayout = true;
    }

    private void RestoreEquipmentSoloLayout()
    {
        if (!hasEquipmentSoloLayout || equipmentWindow == null) return;

        RectTransform equipmentRect = (RectTransform)equipmentWindow.transform;
        equipmentRect.anchorMin = equipmentSoloAnchorMin;
        equipmentRect.anchorMax = equipmentSoloAnchorMax;
        equipmentRect.pivot = equipmentSoloPivot;
        equipmentRect.anchoredPosition = equipmentSoloPosition;
    }

    private void ArrangeEquipmentBesideInventory()
    {
        if (window == null || equipmentWindow == null) return;

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
        capacityText.text = $"가방 {used} / {inventory.Capacity}     퀘스트 {questUsed} / {inventory.QuestCapacity}     필터: {GetFilterLabel(currentFilter)}";
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
        inventory.Move(dragSource, target);
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
            inventory.UseSlot(index, inventory.gameObject);
            return;
        }

        InventoryItemDefinition item = slot.Item;
        if (item.IsEquipment && inventory.EquipFromSlot(index))
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

        Image panel = Box("Inventory Window", root.transform, new Vector2(488, 568), new Color(.68f, .40f, .19f));
        window = panel.gameObject;
        TopRight(panel.rectTransform, new Vector2(-24, -70));
        Shadow shadow = window.AddComponent<Shadow>();
        shadow.effectColor = new Color(.12f, .08f, .04f, .35f);
        shadow.effectDistance = new Vector2(5, -7);
        Image inside = Box("Parchment", panel.transform, new Vector2(480, 560), Cream);
        Stretch(inside.rectTransform, new Vector2(4, 4), new Vector2(-4, -4));
        Image header = Box("Header", panel.transform, new Vector2(480, 60), new Color(.94f, .62f, .22f));
        TopLeft(header.rectTransform, new Vector2(4, -4));
        Label("Title", header.transform, "INVENTORY / 인벤토리", new Vector2(388, 56), new Vector2(18, 0), 24);
        closeButton = MakeButton("Close", header.transform, "X", new Vector2(36, 36), new Color(.77f, .36f, .16f), Color.white);
        TopRight((RectTransform)closeButton.transform, new Vector2(-12, -12));
        capacityText = Label("Capacity", panel.transform, "", new Vector2(432, 32), new Vector2(28, -72), 17);

        Image viewport = Box("Slot Viewport", panel.transform, new Vector2(432, 300), new Color(.82f, .73f, .56f));
        TopLeft(viewport.rectTransform, new Vector2(28, -154));
        viewport.gameObject.AddComponent<RectMask2D>();
        RectTransform content = new GameObject("Slots", typeof(RectTransform)).GetComponent<RectTransform>();
        content.SetParent(viewport.transform, false);
        int slotCount = Mathf.Max(BaseSlotCount, inventory.Capacity, inventory.QuestCapacity);
        content.sizeDelta = new Vector2(432, Mathf.CeilToInt(slotCount / 6f) * 72 - 8);
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
            Image cell = Box($"Slot {i + 1:00}", content, new Vector2(64, 64), new Color(.98f, .94f, .83f));
            TopLeft(cell.rectTransform, new Vector2(i % 6 * 72, -(i / 6) * 72));
            Image icon = Box("Item Icon", cell.transform, new Vector2(42, 42), Color.white);
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            icon.enabled = false;
            TMP_Text amount = Label("Amount", cell.transform, "", new Vector2(56, 22), new Vector2(4, -40), 16);
            amount.alignment = TextAlignmentOptions.BottomRight;
            amount.color = Color.white;
            slots[i] = cell.gameObject.AddComponent<InventorySlotUI>();
            slots[i].Initialize(this, i, icon, amount);
        }

        detailText = Label("Item Details", panel.transform, "", new Vector2(432, 68), new Vector2(28, -468), 16);
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
        if (Application.isPlaying)
        {
            HideChildPanel(parent, "Equipment Panel");
            HideChildPanel(parent, "Stats Panel");
        }

        if (filterButtons == null || filterButtons.Length != 5 || filterButtons[0] == null)
        {
            filterButtons = new Button[5];
            string[] labels = { "전체", "잡화", "소비", "장비", "퀘스트" };
            for (int i = 0; i < filterButtons.Length; i++)
            {
                filterButtons[i] = MakeButton("Filter " + labels[i], parent, labels[i], new Vector2(76, 30), new Color(.45f, .30f, .18f), Cream);
                TopLeft((RectTransform)filterButtons[i].transform, new Vector2(28 + i * 82, -114));
            }
        }

        if (quickSlotText == null)
        {
            quickSlotText = Label("Quick Slots", parent, "", new Vector2(432, 32), new Vector2(28, -536), 15);
        }
    }

    private void ArrangeInventoryWindowLayout()
    {
        if (window == null) return;

        if (capacityText != null)
        {
            capacityText.rectTransform.sizeDelta = new Vector2(InventoryContentWidth, 32f);
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
            detailText.rectTransform.sizeDelta = new Vector2(InventoryContentWidth, 70f);
            TopLeft(detailText.rectTransform, new Vector2(InventoryContentX, DetailTextY));
        }

        if (quickSlotText != null)
        {
            quickSlotText.rectTransform.sizeDelta = new Vector2(InventoryContentWidth, 28f);
            TopLeft(quickSlotText.rectTransform, new Vector2(InventoryContentX, QuickSlotY));
        }
    }

    private void ArrangeFilterTabs()
    {
        if (filterButtons == null) return;

        const float spacing = 8f;
        float width = (InventoryContentWidth - spacing * 4f) / 5f;
        for (int i = 0; i < filterButtons.Length; i++)
        {
            if (filterButtons[i] == null) continue;

            RectTransform rect = (RectTransform)filterButtons[i].transform;
            rect.sizeDelta = new Vector2(width, 30f);
            TopLeft(rect, new Vector2(InventoryContentX + i * (width + spacing), FilterTabsY));
        }
    }

    private void EnsureEquipmentStatsWindow(bool hideAfterCreate = true)
    {
        if (canvas == null) return;

        if (equipmentWindow == null)
        {
            Transform existing = canvas.transform.Find("Equipment Stats Panel");
            if (existing == null) existing = canvas.transform.Find("Equipment Stats Window");
            equipmentWindow = existing != null ? existing.gameObject : CreateEquipmentStatsWindow();
            equipmentWindow.name = "Equipment Stats Panel";
        }

        if (equipmentCloseButton == null)
        {
            Transform close = equipmentWindow.transform.Find("Header/Close");
            if (close != null) equipmentCloseButton = close.GetComponent<Button>();
        }

        if (IsInInventoryWindow(statPointText))
        {
            statPointText = null;
            statMessageText = null;
            statRows = null;
            statUpgradeButtons = null;
        }

        if (equipmentRows != null && equipmentRows.Length > 0 && IsInInventoryWindow(equipmentRows[0]))
        {
            equipmentRows = null;
            equipmentUnequipButtons = null;
        }

        if (equipmentRows == null || equipmentRows.Length != 3 || equipmentRows[0] == null
            || equipmentUnequipButtons == null || equipmentUnequipButtons.Length != 3 || equipmentUnequipButtons[0] == null)
        {
            CreateEquipmentPanel(equipmentWindow.transform);
        }

        if (statPointText == null || statRows == null || statRows.Length != 5 || statRows[0] == null
            || statUpgradeButtons == null || statUpgradeButtons.Length != 5 || statUpgradeButtons[0] == null || statMessageText == null)
        {
            CreateStatsPanel(equipmentWindow.transform);
        }

        ArrangeStatsPanelLayout();
        if (hideAfterCreate) equipmentWindow.SetActive(false);
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
        Transform old = parent.Find("Equipment Panel");
        if (old != null) DestroyUiObject(old.gameObject);

        Image equipmentPanel = Box("Equipment Panel", parent, new Vector2(320, 132), new Color(.82f, .76f, .68f));
        TopLeft(equipmentPanel.rectTransform, new Vector2(20, -84));
        Label("Equipment Title", equipmentPanel.transform, "장비 장착", new Vector2(280, 26), new Vector2(16, -10), 18);

        equipmentRows = new TMP_Text[3];
        equipmentUnequipButtons = new Button[3];
        string[] labels = { "무기", "방어구", "장신구" };
        for (int i = 0; i < equipmentRows.Length; i++)
        {
            equipmentRows[i] = Label("Equipment Row " + i, equipmentPanel.transform, labels[i] + ": -", new Vector2(210, 24), new Vector2(16, -48 - i * 28), 14);
            equipmentUnequipButtons[i] = MakeButton("Unequip Equipment " + i, equipmentPanel.transform, "해제", new Vector2(54, 24), new Color(.55f, .32f, .22f), Color.white);
            TopLeft((RectTransform)equipmentUnequipButtons[i].transform, new Vector2(248, -48 - i * 28));
        }
    }

    private void CreateStatsPanel(Transform parent)
    {
        Transform old = parent.Find("Stats Panel");
        if (old != null) DestroyUiObject(old.gameObject);

        Image statPanel = Box("Stats Panel", parent, new Vector2(320, 250), new Color(.88f, .80f, .64f));
        TopLeft(statPanel.rectTransform, new Vector2(20, -232));
        Label("Stats Title", statPanel.transform, "스탯 강화", new Vector2(96, 26), new Vector2(16, -10), 18);
        statPointText = Label("Stat Points", statPanel.transform, "", new Vector2(280, 24), new Vector2(16, -42), 14);
        statRows = new TMP_Text[5];
        statUpgradeButtons = new Button[5];
        for (int i = 0; i < 5; i++)
        {
            statRows[i] = Label("Stat Row " + i, statPanel.transform, "", new Vector2(220, 34), new Vector2(16, -70 - i * 34), 13);
            statRows[i].textWrappingMode = TextWrappingModes.Normal;
            statUpgradeButtons[i] = MakeButton("Upgrade Stat " + i, statPanel.transform, "+", new Vector2(30, 30), new Color(.35f, .50f, .31f), Color.white);
            TopLeft((RectTransform)statUpgradeButtons[i].transform, new Vector2(270, -70 - i * 34));
        }

        statMessageText = Label("Stat Result", statPanel.transform, "", new Vector2(186, 30), new Vector2(118, -8), 12);
        statMessageText.textWrappingMode = TextWrappingModes.Normal;
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
            title.text = "스탯 강화";
            title.rectTransform.sizeDelta = new Vector2(96, 26);
            TopLeft(title.rectTransform, new Vector2(16, -10));
        }

        if (statMessageText != null)
        {
            statMessageText.fontSize = 12;
            statMessageText.alignment = TextAlignmentOptions.MidlineRight;
            statMessageText.textWrappingMode = TextWrappingModes.Normal;
            statMessageText.rectTransform.sizeDelta = new Vector2(186, 30);
            TopLeft(statMessageText.rectTransform, new Vector2(118, -8));
        }

        if (statPointText != null)
        {
            statPointText.rectTransform.sizeDelta = new Vector2(280, 24);
            TopLeft(statPointText.rectTransform, new Vector2(16, -42));
        }

        if (statRows != null)
        {
            for (int i = 0; i < statRows.Length; i++)
            {
                if (statRows[i] == null) continue;
                statRows[i].rectTransform.sizeDelta = new Vector2(220, 34);
                TopLeft(statRows[i].rectTransform, new Vector2(16, -70 - i * 34));
            }
        }

        if (statUpgradeButtons == null) return;
        for (int i = 0; i < statUpgradeButtons.Length; i++)
        {
            if (statUpgradeButtons[i] == null) continue;
            RectTransform rect = (RectTransform)statUpgradeButtons[i].transform;
            rect.sizeDelta = new Vector2(30, 30);
            TopLeft(rect, new Vector2(270, -70 - i * 34));
        }
    }

    private void ApplyStaticLabels()
    {
        SetButtonLabel(shortcutButton, "가방 [I]");

        TMP_Text inventoryTitle = window != null ? window.transform.Find("Header/Title")?.GetComponent<TMP_Text>() : null;
        if (inventoryTitle != null) inventoryTitle.text = "INVENTORY / 인벤토리";

        TMP_Text equipmentTitle = equipmentWindow != null ? equipmentWindow.transform.Find("Header/Title")?.GetComponent<TMP_Text>() : null;
        if (equipmentTitle != null) equipmentTitle.text = "EQUIPMENT / STATS";

        ApplyDetailTextStyle();
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
            statUpgradeButtons[0].onClick.AddListener(() => Upgrade(PersonalStatType.Evasion));
            statUpgradeButtons[1].onClick.AddListener(() => Upgrade(PersonalStatType.Stealth));
            statUpgradeButtons[2].onClick.AddListener(() => Upgrade(PersonalStatType.Attack));
            statUpgradeButtons[3].onClick.AddListener(() => Upgrade(PersonalStatType.Defense));
            statUpgradeButtons[4].onClick.AddListener(() => Upgrade(PersonalStatType.Persuasion));
        }

        if (equipmentUnequipButtons != null && equipmentUnequipButtons.Length >= 3)
        {
            equipmentUnequipButtons[0].onClick.AddListener(() => Unequip(InventoryItemDefinition.EquipmentSlot.Weapon));
            equipmentUnequipButtons[1].onClick.AddListener(() => Unequip(InventoryItemDefinition.EquipmentSlot.Armor));
            equipmentUnequipButtons[2].onClick.AddListener(() => Unequip(InventoryItemDefinition.EquipmentSlot.Accessory));
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
        PlayerInventory.Slot slot = inventory.GetSlot(index);
        return SlotMatchesFilter(slot) ? slot : null;
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

        statPointText.text = $"강화 포인트: {stats.AvailablePoints}     편차 제한: {stats.StatSpreadLimit}";
        for (int i = 0; i < statRows.Length; i++)
        {
            PersonalStatType type = (PersonalStatType)i;
            statRows[i].text = $"{PlayerStats.GetDisplayName(type)} {stats.GetValue(type)}\n{stats.GetEffectSummary(type)}";
        }
    }

    private void RefreshEquipment()
    {
        if (equipmentRows == null || equipmentRows.Length < 3) return;

        SetEquipmentRow(0, InventoryItemDefinition.EquipmentSlot.Weapon);
        SetEquipmentRow(1, InventoryItemDefinition.EquipmentSlot.Armor);
        SetEquipmentRow(2, InventoryItemDefinition.EquipmentSlot.Accessory);
    }

    private void SetEquipmentRow(int index, InventoryItemDefinition.EquipmentSlot slot)
    {
        if (index < 0 || index >= equipmentRows.Length || equipmentRows[index] == null) return;

        InventoryItemDefinition item = inventory.GetEquipped(slot);
        equipmentRows[index].text = $"{GetEquipmentSlotLabel(slot)}: {(item != null ? item.DisplayName : "-")}";
        if (equipmentUnequipButtons != null && index < equipmentUnequipButtons.Length && equipmentUnequipButtons[index] != null)
            equipmentUnequipButtons[index].interactable = item != null;
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
        content.sizeDelta = area;

        SlotGrid grid = CalculateSlotGrid(slotCount, area);
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
        int bestRows = 1;
        int bestColumns = Mathf.Max(1, slotCount);
        float bestCellSize = MinSlotSize;
        float bestScore = float.NegativeInfinity;
        float targetAspect = Mathf.Max(.1f, area.x / Mathf.Max(1f, area.y));

        for (int rows = 1; rows <= slotCount; rows++)
        {
            int minimumColumns = Mathf.CeilToInt(slotCount / (float)rows);
            int maximumColumns = Mathf.Min(slotCount, minimumColumns + 3);
            for (int columns = minimumColumns; columns <= maximumColumns; columns++)
            {
                float spacing = CalculateSlotSpacing(area, columns, rows);
                float availableWidth = area.x - SlotPadding * 2f - spacing * (columns - 1);
                float availableHeight = area.y - SlotPadding * 2f - spacing * (rows - 1);
                if (availableWidth <= 0f || availableHeight <= 0f) continue;

                float rawCellSize = Mathf.Min(availableWidth / columns, availableHeight / rows);
                if (rawCellSize <= 0f) continue;

                float cellSize = Mathf.Floor(Mathf.Min(rawCellSize, MaxSlotSize));
                int emptyCells = columns * rows - slotCount;
                float gridWidth = columns * cellSize + (columns - 1) * spacing + SlotPadding * 2f;
                float gridHeight = rows * cellSize + (rows - 1) * spacing + SlotPadding * 2f;
                float widthUsage = Mathf.Clamp01(gridWidth / Mathf.Max(1f, area.x));
                float heightUsage = Mathf.Clamp01(gridHeight / Mathf.Max(1f, area.y));
                float areaUsage = widthUsage * heightUsage;
                float gridAspect = columns / (float)rows;
                float aspectPenalty = Mathf.Abs(Mathf.Log(gridAspect / targetAspect)) * 20f;
                float emptyPenalty = emptyCells * 2f;
                float smallCellPenalty = cellSize < MinSlotSize ? (MinSlotSize - cellSize) * 2f : 0f;
                float largeCellPenalty = cellSize > 68f ? (cellSize - 68f) * .35f : 0f;
                float score =
                    cellSize * .8f
                    + widthUsage * 35f
                    + heightUsage * 20f
                    + areaUsage * 25f
                    - aspectPenalty
                    - emptyPenalty
                    - smallCellPenalty
                    - largeCellPenalty;

                if (score <= bestScore) continue;
                bestScore = score;
                bestRows = rows;
                bestColumns = columns;
                bestCellSize = cellSize;
            }
        }

        return new SlotGrid(bestColumns, bestRows, bestCellSize, CalculateSlotSpacing(area, bestColumns, bestRows));
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
            _ => "장비"
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
