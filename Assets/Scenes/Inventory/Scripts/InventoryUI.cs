using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Uses the editable scene canvas without resetting its layout when entering Play mode.</summary>
[DefaultExecutionOrder(-100)]
public sealed class InventoryUI : MonoBehaviour
{
    private enum Filter { All, Misc, Consumable, Equipment, Quest }

    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private PlayerStats stats;
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private TMP_FontAsset detailFont;
    [SerializeField] private TextAsset displayTextJson;
    [Header("Scene UI references (created by the editor setup)")]
    [SerializeField] private Canvas canvas;
    [SerializeField] private GameObject window;
    [SerializeField] private Button shortcutButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button[] additionalCloseButtons;
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
    private int hoveredDisplaySlot = -1;
    private PlayerControlLock inventoryControlLock;
    private bool holdsInventoryLock;
    private Image inventoryDimmer;
    private float dimmerElapsed;
    private const float DimmerFadeDuration = .25f;
    private static int gameplaySuppressedFrame = -1;
    private static int escapeHandledFrame = -1;
    public static bool GameplayInputSuppressedThisFrame => gameplaySuppressedFrame == Time.frameCount;
    public static bool EscapeHandledThisFrame => escapeHandledFrame == Time.frameCount;
    private float toastUntil;
    private bool toastResourceWarningLogged;
    private bool ready;
    private bool ownsRuntimeCanvas;
    private Filter currentFilter;
    private bool hasEquipmentSoloLayout;
    private bool usesDesignedCanvas;
    private Canvas legacyCanvas;
    private InventoryUITextCatalog textCatalog;
    private readonly System.Collections.Generic.Dictionary<TMP_Text, string> authoredText = new();
    private RectTransform authoredEquipmentGroup;
    private Vector3 authoredEquipmentPosition;
    private RectTransform authoredPlayerPanel;
    private Vector3 authoredPlayerPanelPosition;
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

    public void BindDesignedCanvas(
        Canvas designedCanvas,
        GameObject designedWindow,
        Button primaryCloseButton,
        Button[] extraCloseButtons,
        InventorySlotUI[] designedSlots,
        TMP_Text designedCapacityText,
        TMP_Text designedDetailText,
        Image designedDragIcon,
        Button[] designedFilterButtons,
        GameObject designedEquipmentWindow,
        TMP_Text designedStatPointText,
        TMP_Text designedStatMessageText,
        TMP_Text[] designedStatRows,
        Button[] designedStatButtons,
        Button[] designedEquipmentButtons,
        Image[] designedEquipmentIcons)
    {
        if (designedCanvas == null || designedWindow == null || primaryCloseButton == null
            || designedSlots == null || designedSlots.Length == 0) return;

        legacyCanvas = canvas != designedCanvas ? canvas : null;
        canvas = designedCanvas;
        window = designedWindow;
        shortcutButton = null;
        closeButton = primaryCloseButton;
        additionalCloseButtons = extraCloseButtons;
        slots = designedSlots;
        capacityText = designedCapacityText;
        detailText = designedDetailText;
        toast = null;
        dragIcon = designedDragIcon;
        filterButtons = designedFilterButtons;
        quickSlotText = null;
        equipmentWindow = designedEquipmentWindow;
        equipmentCloseButton = null;
        statPointText = designedStatPointText;
        statMessageText = designedStatMessageText;
        statRows = designedStatRows;
        statUpgradeButtons = designedStatButtons;
        statBarFills = System.Array.Empty<Image>();
        equipmentRows = System.Array.Empty<TMP_Text>();
        equipmentUnequipButtons = designedEquipmentButtons;
        equipmentSlotIcons = designedEquipmentIcons;
        usesDesignedCanvas = true;
        authoredEquipmentGroup = designedCanvas.transform.Find("BG/Player/Equipments") as RectTransform;
        if (authoredEquipmentGroup != null)
        {
            authoredEquipmentPosition = authoredEquipmentGroup.anchoredPosition3D;
            authoredPlayerPanel = authoredEquipmentGroup.parent as RectTransform;
            if (authoredPlayerPanel != null) authoredPlayerPanelPosition = authoredPlayerPanel.anchoredPosition3D;
        }
        foreach (TMP_Text label in designedCanvas.GetComponentsInChildren<TMP_Text>(true))
            authoredText[label] = label.text;

        if (legacyCanvas != null) legacyCanvas.gameObject.SetActive(false);
    }

    internal string StatNameForBinding(int index)
    {
        PersonalStatType type = GetUiStatType(index);
        return textCatalog != null ? textCatalog.StatName(type, GetUiStatLabel(index)) : GetUiStatLabel(index);
    }

    internal string CategoryNameForBinding(int index)
    {
        Filter filter = GetDesignedFilter(index);
        return GetFilterLabel(filter);
    }

    internal void ShowStatDetailsForBinding(int index)
    {
        if (stats == null || index < 0 || index >= 5) return;
        PersonalStatType type = GetUiStatType(index);
        if (statPointText != null) statPointText.text = $"{StatNameForBinding(index)} {stats.GetValue(type)}";
        if (statMessageText != null)
            statMessageText.text = textCatalog != null
                ? textCatalog.StatDescription(type, stats.GetEffectSummary(type))
                : stats.GetEffectSummary(type);
    }

    internal void ShowEquipmentDetailsForBinding(int index)
    {
        if (detailText == null || index < 0 || index >= 6) return;
        InventoryItemDefinition item = inventory.GetEquipped(GetEquipmentSlotForIndex(index));
        if (item == null) return;
        if (usesDesignedCanvas)
        {
            detailText.text = GetItemDescription(item);
            return;
        }
        ApplyDetailTextStyle();
        detailText.text = FormatUi("equipmentTooltipFormat", "<size=105%>{0}</size>\n{1}",
            GetItemName(item), GetItemDescription(item) + GetEquipmentEffectDescription(item));
    }

    private bool RequiresDesignedCanvas() =>
        gameObject.scene.name == "UITest" || gameObject.scene.name == "InventoryTestScene";

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

        textCatalog = InventoryUITextCatalog.Load(displayTextJson);
        bool designedCanvasBound = InventoryCanvasAdapter.TryBind(this);

        if (canvas == null)
        {
            if (RequiresDesignedCanvas())
            {
                Debug.LogError(designedCanvasBound
                    ? "[InventoryUI] Inventory_Canvas binding did not assign its Canvas."
                    : $"[InventoryUI] Inventory_Canvas binding failed. Legacy inventory UI creation is disabled in {gameObject.scene.name}.", this);
                enabled = false;
                return;
            }
            CreateSceneUI();
            ownsRuntimeCanvas = true;
        }

        if (window == null || closeButton == null || capacityText == null
            || detailText == null || dragIcon == null || slots == null || slots.Length == 0)
        {
            Debug.LogError("[InventoryUI] Scene UI references are missing. Check this component in the Inspector.", this);
            enabled = false;
            return;
        }

        EnsureEventSystem();
        if (!usesDesignedCanvas)
        {
            EnsureInventoryExtras();
            EnsureEquipmentStatsWindow();
        }
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
        if (toast != null) toast.gameObject.SetActive(false);
        CancelDrag();
    }

    private void OnEnable()
    {
        if (!ready) return;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnInventorySceneLoaded;
        inventory.Changed += Refresh;
        inventory.ItemCollected += Collected;
        inventory.ItemUsed += Used;
        inventory.InventoryFull += Full;
        if (stats != null) stats.Changed += Refresh;
        if (shortcutButton != null) shortcutButton.onClick.AddListener(ToggleInventoryFromButton);
        closeButton.onClick.AddListener(CloseInventoryFromButton);
        if (additionalCloseButtons != null)
            foreach (Button button in additionalCloseButtons)
                if (button != null) button.onClick.AddListener(CloseInventoryFromButton);
        if (equipmentCloseButton != null) equipmentCloseButton.onClick.AddListener(CloseEquipmentStatsFromButton);
        if (canvas != null)
        {
            if (canvas.gameObject != window) canvas.gameObject.SetActive(true);
            Refresh();
        }
    }

    private void OnDisable()
    {
        SetInventoryModal(false);
        hoveredDisplaySlot = -1;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnInventorySceneLoaded;
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
        if (additionalCloseButtons != null)
            foreach (Button button in additionalCloseButtons)
                if (button != null) button.onClick.RemoveListener(CloseInventoryFromButton);
        if (equipmentCloseButton != null) equipmentCloseButton.onClick.RemoveListener(CloseEquipmentStatsFromButton);
        CancelDrag();
        if (!Application.isPlaying) return;

        if (window != null) window.SetActive(false);
        if (!IsEmbeddedEquipmentWindow() && equipmentWindow != null) equipmentWindow.SetActive(false);
        if (ownsRuntimeCanvas && canvas != null) canvas.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (!usesDesignedCanvas || authoredEquipmentGroup == null) return;
        // Preserve this scene instance's authored positions; never apply a hard-coded offset.
        if (authoredPlayerPanel != null && authoredPlayerPanel.anchoredPosition3D != authoredPlayerPanelPosition)
            authoredPlayerPanel.anchoredPosition3D = authoredPlayerPanelPosition;
        if (authoredEquipmentGroup.anchoredPosition3D != authoredEquipmentPosition)
            authoredEquipmentGroup.anchoredPosition3D = authoredEquipmentPosition;
    }

    private void OnInventorySceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (!usesDesignedCanvas || canvas == null || GetComponent<PersistentPlayer2D>() == null
            || gameObject.scene == scene) return;
        // The persistent player's bound canvas remains authoritative during travel.
        DontDestroyOnLoad(canvas.transform.root.gameObject);
        foreach (Canvas candidate in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (candidate != canvas && candidate.gameObject.scene == scene
                && (candidate.name == "Inventory_Canvas" || candidate.name == "Inventory Canvas"))
                candidate.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        SetInventoryModal(false);
        if (inventoryDimmer != null) Destroy(inventoryDimmer.gameObject);
        if (ownsRuntimeCanvas && canvas != null) Destroy(canvas.gameObject);
    }

    private void Update()
    {
        if (!ready) return;
        SetInventoryModal(window != null && window.activeInHierarchy);
        if (Keyboard.current?.iKey.wasPressedThisFrame == true) ToggleInventory();
        if (Keyboard.current?.escapeKey.wasPressedThisFrame == true && IsAnyWindowOpen())
        {
            escapeHandledFrame = Time.frameCount;
            CloseAllWindows();
        }
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) HandleQuickSlotKey(0);
            if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) HandleQuickSlotKey(1);
            if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) HandleQuickSlotKey(2);
        }
        if (toast != null && toast.gameObject.activeSelf && Time.unscaledTime >= toastUntil) toast.gameObject.SetActive(false);
        if (holdsInventoryLock && inventoryDimmer != null)
        {
            dimmerElapsed += Time.unscaledDeltaTime;
            Color color = inventoryDimmer.color;
            color.a = (211f / 255f) * Mathf.Clamp01(dimmerElapsed / DimmerFadeDuration);
            inventoryDimmer.color = color;
        }
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
        SetInventoryModal(true);
        if (usesDesignedCanvas)
        {
            InventoryCanvasAdapter.RefreshStatHoverBindings(this);
            InventoryHoverReveal.HideAll(window);
        }
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
        SetInventoryModal(false);
        hoveredDisplaySlot = -1;
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

    private void SetInventoryModal(bool open)
    {
        if (!Application.isPlaying) return;
        if (open) gameplaySuppressedFrame = Time.frameCount;
        if (open == holdsInventoryLock) return;
        holdsInventoryLock = open;
        gameplaySuppressedFrame = Time.frameCount;
        if (open)
        {
            inventoryControlLock = inventory != null ? inventory.GetComponent<PlayerControlLock>() : null;
            inventoryControlLock?.Acquire();
            EnsureInventoryDimmer();
            dimmerElapsed = 0f;
            if (inventoryDimmer != null)
            {
                inventoryDimmer.color = new Color(40f / 255f, 20f / 255f, 20f / 255f, 0f);
                inventoryDimmer.gameObject.SetActive(true);
            }
        }
        else
        {
            inventoryControlLock?.Release();
            inventoryControlLock = null;
            if (inventoryDimmer != null)
            {
                Color color = inventoryDimmer.color;
                color.a = 0f;
                inventoryDimmer.color = color;
                inventoryDimmer.gameObject.SetActive(false);
            }
        }
    }

    private void EnsureInventoryDimmer()
    {
        if (inventoryDimmer != null || canvas == null) return;
        GameObject backdrop = new("Inventory Dimmer", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        backdrop.transform.SetParent(canvas.transform, false);
        backdrop.transform.SetAsFirstSibling();
        RectTransform rect = (RectTransform)backdrop.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        inventoryDimmer = backdrop.GetComponent<Image>();
        inventoryDimmer.raycastTarget = true;
    }

    private void CaptureEquipmentSoloLayout()
    {
        if (usesDesignedCanvas) return;
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
        if (usesDesignedCanvas) return;
        if (!hasEquipmentSoloLayout || equipmentWindow == null || IsEmbeddedEquipmentWindow()) return;

        RectTransform equipmentRect = (RectTransform)equipmentWindow.transform;
        equipmentRect.anchorMin = equipmentSoloAnchorMin;
        equipmentRect.anchorMax = equipmentSoloAnchorMax;
        equipmentRect.pivot = equipmentSoloPivot;
        equipmentRect.anchoredPosition = equipmentSoloPosition;
    }

    private void ArrangeEquipmentBesideInventory()
    {
        if (usesDesignedCanvas) return;
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
            bool visible = usesDesignedCanvas || i < displayedSlotCount;
            slots[i].gameObject.SetActive(visible);
            if (!visible) continue;

            bool unlocked = i < (currentFilter == Filter.Quest ? inventory.QuestCapacity : inventory.Capacity);
            slots[i].SetLocked(!unlocked);
            slots[i].ResetVisualState();
            slots[i].Refresh(unlocked ? GetDisplayedSlot(i) : null);
        }
        if (usesDesignedCanvas) RefreshAuthoredNumbers(capacityText, used, inventory.Capacity);
        else capacityText.text = FormatUi(
            "capacityFormat",
            "가방 {0} / {1}     퀘스트 {2} / {3}     분류: {4}",
            used,
            inventory.Capacity,
            questUsed,
            inventory.QuestCapacity,
            GetFilterLabel(currentFilter));
        RefreshFilterButtons();
        RefreshQuickSlots();
        RefreshStats();
        RefreshEquipment();
        ShowDetails(-1);
    }

    private void Collected(InventoryItemDefinition item, int amount) => Notify($"{GetItemName(item)} +{amount} | {UiText("inventoryHint", "[I] 인벤토리")}");
    private void Used(InventoryItemDefinition item) => Notify($"{GetItemName(item)}을 사용했습니다.");
    private void Full() => Notify("인벤토리가 가득 찼습니다.");

    private void Notify(string message)
    {
        if (toast == null)
        {
            if (!usesDesignedCanvas && detailText != null) detailText.text = message;
            return;
        }
        toast.text = message;
        toast.gameObject.SetActive(true);
        toastUntil = Time.unscaledTime + 3.5f;
    }

    public void ShowDetails(int index)
    {
        if (usesDesignedCanvas)
        {
            // Equip_Desc is an authored description field, not a replacement inventory layout.
            PlayerInventory.Slot selected = GetDisplayedSlot(index);
            if (detailText != null) detailText.text = selected == null || selected.IsEmpty
                ? AuthoredText(detailText) : GetItemDescription(selected.Item);
            return;
        }
        ApplyDetailTextStyle();
        PlayerInventory.Slot slot = GetDisplayedSlot(index);
        detailText.text = slot == null || slot.IsEmpty
            ? UiText("emptyTooltip", "아이템 위에 마우스를 올리면 설명을 확인할 수 있습니다.")
            : FormatUi(
                "tooltipFormat",
                "<size=105%>{0}</size>  x{1}\n카테고리: {2}\n{3}",
                GetItemName(slot.Item),
                slot.Amount,
                GetItemCategoryName(slot.Item),
                GetItemDescription(slot.Item) + GetEquipmentEffectDescription(slot.Item));
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
            Notify($"{GetItemName(item)} 장착 완료");
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
        if (usesDesignedCanvas) return;
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
        if (RequiresDesignedCanvas())
        {
            if (!InventoryCanvasAdapter.TryBind(this))
                Debug.LogError($"[InventoryUI] Place the authored Inventory_Canvas prefab in {gameObject.scene.name}. Legacy UI creation is disabled.", this);
            return;
        }
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
        if (usesDesignedCanvas) return;

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
        if (usesDesignedCanvas) return;

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
            bool selected = usesDesignedCanvas
                ? currentFilter == GetDesignedFilter(i)
                : (int)currentFilter == i;
            if (usesDesignedCanvas) continue;
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
        if (!ownsRuntimeCanvas) return;
        SetButtonLabel(shortcutButton, "가방 [I]");

        TMP_Text inventoryTitle = window != null ? window.transform.Find("Header/Title")?.GetComponent<TMP_Text>() : null;
        if (inventoryTitle != null) inventoryTitle.text = "Inventory / 인벤토리";

        TMP_Text equipmentTitle = equipmentWindow != null ? equipmentWindow.transform.Find("Header/Title")?.GetComponent<TMP_Text>() : null;
        if (equipmentTitle != null) equipmentTitle.text = "EQUIPMENT / STATS";

        ApplyDetailTextStyle();
    }

    private void EnsureToastStyle()
    {
        if (!ownsRuntimeCanvas) return;
        if (toast == null) return;

        Transform background = toast.transform.parent != null ? toast.transform.parent.Find("Pickup Message Back") : null;
        if (background != null) DestroyUiObject(background.gameObject);

        // A disabled TMP object can retain its font while its material instance is gone.
        // Assigning the same font again is a no-op in TMP, so repair the material explicitly.
        TMP_FontAsset toastFont = font != null ? font : toast.font;
        Material toastMaterial = toast.fontSharedMaterial;
        if (toastFont == null || toastFont.atlasTexture == null)
        {
            WarnMissingToastResource("Font Asset or font atlas texture");
            return;
        }

        if (toastMaterial == null)
            toastMaterial = toastFont.material;

        if (toastMaterial == null || toastMaterial.shader == null
            || !toastMaterial.HasProperty("_MainTex")
            || toastMaterial.GetTexture("_MainTex") != toastFont.atlasTexture
            || !toastMaterial.HasProperty("_OutlineWidth") || !toastMaterial.HasProperty("_OutlineColor"))
        {
            WarnMissingToastResource("compatible Font Material (atlas / outline shader properties)");
            return;
        }

        if (toast.font != toastFont) toast.font = toastFont;
        if (toast.fontSharedMaterial != toastMaterial) toast.fontSharedMaterial = toastMaterial;
        toastResourceWarningLogged = false;
        toast.fontSize = 20;
        toast.fontStyle = FontStyles.Normal;
        toast.fontWeight = FontWeight.Regular;
        toast.alignment = TextAlignmentOptions.Center;
        toast.color = Cream;
        if (!Mathf.Approximately(toast.outlineWidth, .2f) || !toast.outlineColor.Equals((Color32)Ink))
        {
            // TMP owns and reuses this instance; never write to the shared font/preset asset.
            // Establish it before the outline setters, including on inactive text objects.
            _ = toast.fontMaterial;
            if (!Mathf.Approximately(toast.outlineWidth, .2f)) toast.outlineWidth = .2f;
            if (!toast.outlineColor.Equals((Color32)Ink)) toast.outlineColor = Ink;
        }
    }

    private void WarnMissingToastResource(string missing)
    {
        if (toastResourceWarningLogged) return;
        toastResourceWarningLogged = true;
        Debug.LogWarning($"[InventoryUI] '{toast.name}' in scene '{toast.gameObject.scene.path}': missing {missing}. "
            + "Check InventoryUI.Font and the toast TMP Font Asset / Material Preset in the Inspector.", toast);
    }

    private void AddButtonListeners()
    {
        if (usesDesignedCanvas && filterButtons != null && filterButtons.Length >= 4)
        {
            filterButtons[0].onClick.AddListener(() => SetFilter(Filter.Misc));
            filterButtons[1].onClick.AddListener(() => SetFilter(Filter.Consumable));
            filterButtons[2].onClick.AddListener(() => SetFilter(Filter.Equipment));
            filterButtons[3].onClick.AddListener(() => SetFilter(Filter.Quest));
        }
        else if (filterButtons != null && filterButtons.Length >= 5)
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

    public void SetHoveredSlot(int index, bool hovered)
    {
        if (hovered) hoveredDisplaySlot = index;
        else if (hoveredDisplaySlot == index) hoveredDisplaySlot = -1;
    }

    private void HandleQuickSlotKey(int index)
    {
        if (window != null && window.activeInHierarchy)
        {
            if (hoveredDisplaySlot < 0 || dragSource >= 0 || currentFilter == Filter.Quest) return;
            int displayIndex = hoveredDisplaySlot;
            int inventoryIndex = GetInventorySlotIndexForDisplay(displayIndex);
            if (inventory.AssignQuickSlot(inventoryIndex, index)
                && slots != null && displayIndex < slots.Length && slots[displayIndex] != null)
                slots[displayIndex].PlayRegistrationFlash();
            return;
        }
        if (IsAnyWindowOpen() || GameplayInputSuppressedThisFrame) return;
        var health = inventory.GetComponent<PlayerHealth>();
        var input = inventory.GetComponent<PlayerInputReader>();
        if ((health != null && health.IsDead) || (input != null && !input.isActiveAndEnabled)) return;
        if (inventory.GetQuickSlotCooldownFill(index) > 0f) return;
        UseQuickSlot(index);
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
        string text = string.Empty;
        for (int i = 0; i < inventory.QuickSlotCount; i++)
        {
            InventoryItemDefinition item = inventory.GetQuickSlot(i);
            text += $"[{i + 1}] {(item != null ? GetItemName(item) : "-")}  ";
        }
        quickSlotText.text = FormatUi("quickSlotFormat", "퀵슬롯 {0}", text);
    }

    private string AuthoredText(TMP_Text label)
    {
        if (!authoredText.TryGetValue(label, out string template))
            authoredText[label] = template = label.text;
        return template;
    }

    private void RefreshAuthoredNumbers(TMP_Text label, params int[] values)
    {
        if (label == null) return;
        int index = 0;
        // Skip TMP markup so numbers in size/color tags are never treated as game values.
        label.text = System.Text.RegularExpressions.Regex.Replace(AuthoredText(label), @"<[^>]*>|\d+", match =>
            match.Value.StartsWith("<") || index >= values.Length
                ? match.Value : values[index++].ToString());
    }

    private void RefreshStats()
    {
        if (usesDesignedCanvas)
        {
            if (stats == null || statRows == null) return;
            for (int i = 0; i < statRows.Length; i++)
                RefreshAuthoredNumbers(statRows[i], stats.GetValue(GetUiStatType(i)));
            return;
        }
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
            statRows[i].text = $"{StatNameForBinding(i)} {value}";
            if (statBarFills != null && i < statBarFills.Length && statBarFills[i] != null)
            {
                RectTransform fill = statBarFills[i].rectTransform;
                fill.anchorMin = Vector2.zero;
                fill.anchorMax = new Vector2(Mathf.Clamp01(value / Mathf.Max(1f, stats.StatSpreadLimit + 5f)), 1f);
                fill.offsetMin = Vector2.zero;
                fill.offsetMax = Vector2.zero;
            }
        }

        if (statMessageText != null && string.IsNullOrEmpty(statMessageText.text))
        {
            PersonalStatType first = GetUiStatType(0);
            statMessageText.text = textCatalog != null
                ? textCatalog.StatDescription(first, stats.GetEffectSummary(first))
                : stats.GetEffectSummary(first);
        }
    }

    private void RefreshEquipment()
    {
        SetEquipmentRow(0, InventoryItemDefinition.EquipmentSlot.Weapon);
        SetEquipmentRow(1, InventoryItemDefinition.EquipmentSlot.Armor);
        SetEquipmentRow(2, InventoryItemDefinition.EquipmentSlot.Accessory);
        SetEquipmentRow(3, InventoryItemDefinition.EquipmentSlot.Head);
        SetEquipmentRow(4, InventoryItemDefinition.EquipmentSlot.Shoes);
        SetEquipmentRow(5, InventoryItemDefinition.EquipmentSlot.Other);
    }

    private static InventoryItemDefinition.EquipmentSlot GetEquipmentSlotForIndex(int index)
    {
        return index switch
        {
            0 => InventoryItemDefinition.EquipmentSlot.Weapon,
            1 => InventoryItemDefinition.EquipmentSlot.Armor,
            2 => InventoryItemDefinition.EquipmentSlot.Accessory,
            3 => InventoryItemDefinition.EquipmentSlot.Head,
            4 => InventoryItemDefinition.EquipmentSlot.Shoes,
            _ => InventoryItemDefinition.EquipmentSlot.Other
        };
    }

    private void SetEquipmentRow(int index, InventoryItemDefinition.EquipmentSlot slot)
    {
        InventoryItemDefinition item = inventory.GetEquipped(slot);
        if (equipmentRows != null && index >= 0 && index < equipmentRows.Length && equipmentRows[index] != null)
            equipmentRows[index].text = item != null ? GetItemName(item) : GetEquipmentSlotLabel(slot);
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
        if (!usesDesignedCanvas && statMessageText != null) statMessageText.text = message;
        if (!string.IsNullOrEmpty(message)) Notify(message);
    }

    private void ApplyDetailTextStyle()
    {
        if (detailText == null || !ownsRuntimeCanvas) return;
        if (usesDesignedCanvas)
        {
            detailText.richText = true;
            return;
        }

        if (detailFont != null) detailText.font = detailFont;
        detailText.fontStyle = FontStyles.Normal;
        detailText.fontWeight = FontWeight.Regular;
        detailText.richText = true;
    }

    private void MatchSlotCount()
    {
        if (usesDesignedCanvas) return;
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
        if (usesDesignedCanvas) return slots != null ? slots.Length : 0;
        return currentFilter == Filter.Quest ? inventory.QuestCapacity : inventory.Capacity;
    }

    private void ApplySlotLayout(int slotCount)
    {
        if (usesDesignedCanvas) return;
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

    private static Filter GetDesignedFilter(int index)
    {
        return index switch
        {
            0 => Filter.Misc,
            1 => Filter.Consumable,
            2 => Filter.Equipment,
            3 => Filter.Quest,
            _ => Filter.All
        };
    }

    private string GetFilterLabel(Filter filter)
    {
        string fallback = filter switch
        {
            Filter.Misc => "잡화",
            Filter.Consumable => "소비",
            Filter.Equipment => "장비",
            Filter.Quest => "퀘스트",
            _ => "전체"
        };
        return textCatalog != null ? textCatalog.CategoryName(filter.ToString(), fallback) : fallback;
    }

    private string GetEquipmentSlotLabel(InventoryItemDefinition.EquipmentSlot slot)
    {
        string fallback = slot switch
        {
            InventoryItemDefinition.EquipmentSlot.Weapon => "무기",
            InventoryItemDefinition.EquipmentSlot.Armor => "방어구",
            InventoryItemDefinition.EquipmentSlot.Accessory => "장신구",
            InventoryItemDefinition.EquipmentSlot.Head => "머리",
            InventoryItemDefinition.EquipmentSlot.Shoes => "신발",
            InventoryItemDefinition.EquipmentSlot.Other => "기타",
            _ => "장비"
        };
        return textCatalog != null ? textCatalog.EquipmentSlotName(slot, fallback) : fallback;
    }

    private string GetItemName(InventoryItemDefinition item)
    {
        return textCatalog != null ? textCatalog.ItemName(item) : item != null ? item.DisplayName : string.Empty;
    }

    private string GetItemDescription(InventoryItemDefinition item)
    {
        return textCatalog != null ? textCatalog.ItemDescription(item) : item != null ? item.Description : string.Empty;
    }

    private string GetItemCategoryName(InventoryItemDefinition item)
    {
        if (item == null) return string.Empty;
        string fallback = item.GetCategoryLabel();
        return textCatalog != null ? textCatalog.CategoryName(item.Category, fallback) : fallback;
    }

    private string GetEquipmentEffectDescription(InventoryItemDefinition item)
    {
        if (item == null || !item.IsEquipment || !item.HasStatModifiers || item.StatModifiers == null) return string.Empty;

        string result = string.Empty;
        foreach (ItemStatModifier modifier in item.StatModifiers)
        {
            if (modifier.Increase <= 0) continue;
            string statName = textCatalog != null
                ? textCatalog.StatName(modifier.StatType, PlayerStats.GetDisplayName(modifier.StatType))
                : PlayerStats.GetDisplayName(modifier.StatType);
            result += "\n" + FormatUi("equipmentEffectFormat", "{0} +{1}", statName, modifier.Increase);
        }
        return result;
    }

    private string UiText(string id, string fallback)
    {
        return textCatalog != null ? textCatalog.Ui(id, fallback) : fallback;
    }

    private string FormatUi(string id, string fallback, params object[] values)
    {
        string format = UiText(id, fallback);
        try { return string.Format(format, values); }
        catch (System.FormatException)
        {
            Debug.LogWarning($"[InventoryUI] Invalid display format '{id}' in inventory_ui_text.json.", this);
            return string.Format(fallback, values);
        }
    }

    // The visible RPG labels map onto the project-specific stat model used by PlayerStats.
    private PersonalStatType GetUiStatType(int index)
    {
        if (usesDesignedCanvas)
            return index switch
            {
                0 => PersonalStatType.Evasion,
                1 => PersonalStatType.Stealth,
                2 => PersonalStatType.Attack,
                3 => PersonalStatType.Defense,
                4 => PersonalStatType.Persuasion,
                _ => PersonalStatType.Attack
            };
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
