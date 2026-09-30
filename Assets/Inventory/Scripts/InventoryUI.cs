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
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private TMP_FontAsset font;
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
    private int dragSource = -1;
    private float toastUntil;
    private bool ready;
    private bool ownsRuntimeCanvas;
    private static readonly Color Ink = new(.25f, .19f, .15f);
    private static readonly Color Cream = new(1f, .97f, .88f);

    public bool HasSceneUI => canvas != null;
    public bool CanCreateSceneUI => inventory != null;
    public Canvas SceneCanvas => canvas;
    public GameObject Window => window;
    public Button ShortcutButton => shortcutButton;

    private void Awake()
    {
        if (inventory == null)
        {
            Debug.LogError("InventoryUI requires a PlayerInventory reference.", this);
            enabled = false;
            return;
        }
        // Existing scenes are migrated by InventoryUIEditor. Keep runtime-only spawning supported.
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
        MatchSlotCount();
        for (int i = 0; i < slots.Length; i++) slots[i].Bind(this, i);
        ready = true;
        Refresh();
        window.SetActive(false);
        toast.gameObject.SetActive(false);
        CancelDrag();
    }

    private void OnEnable()
    {
        if (!ready) return;
        inventory.Changed += Refresh;
        inventory.ItemCollected += Collected;
        inventory.InventoryFull += Full;
        shortcutButton.onClick.AddListener(ToggleFromButton);
        closeButton.onClick.AddListener(CloseFromButton);
        if (canvas != null) { canvas.gameObject.SetActive(true); Refresh(); }
    }

    private void OnDisable()
    {
        if (inventory != null)
        {
            inventory.Changed -= Refresh;
            inventory.ItemCollected -= Collected;
            inventory.InventoryFull -= Full;
        }
        if (shortcutButton != null) shortcutButton.onClick.RemoveListener(ToggleFromButton);
        if (closeButton != null) closeButton.onClick.RemoveListener(CloseFromButton);
        CancelDrag();
        if (window != null) window.SetActive(false);
        if (canvas != null) canvas.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (ownsRuntimeCanvas && canvas != null) Destroy(canvas.gameObject);
    }

    private void Update()
    {
        if (!ready) return;
        if (Keyboard.current?.iKey.wasPressedThisFrame == true) Toggle();
        if (Keyboard.current?.escapeKey.wasPressedThisFrame == true && window.activeSelf) Close();
        if (toast.gameObject.activeSelf && Time.unscaledTime >= toastUntil) toast.gameObject.SetActive(false);
    }

    private void OnApplicationFocus(bool focused) { if (!focused) CancelDrag(); }
    private void ToggleFromButton() { PlayerInputReader.SuppressPointerForCurrentFrame(); Toggle(); }
    private void CloseFromButton() { PlayerInputReader.SuppressPointerForCurrentFrame(); Close(); }
    private void Toggle() { if (window.activeSelf) Close(); else { Refresh(); window.SetActive(true); } }
    private void Close() { CancelDrag(); ShowDetails(-1); window.SetActive(false); }

    private void Refresh()
    {
        if (slots == null) return;
        int used = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            PlayerInventory.Slot slot = inventory.GetSlot(i);
            slots[i].Refresh(slot);
            if (!slot.IsEmpty) used++;
        }
        capacityText.text = $"전체 아이템                 {used} / {inventory.Capacity} 칸";
        ShowDetails(-1);
    }

    private void Collected(InventoryItemDefinition item, int amount) => Notify($"{item.DisplayName}  +{amount}   |   [I] 인벤토리");
    private void Full() => Notify("인벤토리가 가득 찼습니다. 남은 아이템은 바닥에 유지됩니다.");
    private void Notify(string message)
    {
        toast.text = message;
        toast.gameObject.SetActive(true);
        toastUntil = Time.unscaledTime + 3.5f;
    }

    public void ShowDetails(int index)
    {
        PlayerInventory.Slot slot = inventory.GetSlot(index);
        detailText.text = slot == null || slot.IsEmpty
            ? "아이템을 드래그해서 원하는 칸에 놓으세요.\n같은 아이템은 합쳐지고, 다른 아이템은 교환됩니다."
            : $"<b>{slot.Item.DisplayName}</b>  x{slot.Amount}\n{slot.Item.Description}";
    }

    public void BeginDrag(int index, PointerEventData data)
    {
        PlayerInventory.Slot slot = inventory.GetSlot(index);
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
        if (dragSource < 0) return;
        inventory.Move(dragSource, target);
        CancelDrag();
        ShowDetails(target);
    }

    public void CancelDrag()
    {
        dragSource = -1;
        if (dragIcon != null) dragIcon.gameObject.SetActive(false);
    }

    private static void EnsureEventSystem()
    {
        EventSystem events = FindFirstObjectByType<EventSystem>();
        if (events == null) events = new GameObject("Inventory EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
        if (events.GetComponent<BaseInputModule>() == null)
            events.gameObject.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
    }

    /// <summary>Creates missing UI once. The editor records it with Undo and saves it with the scene.</summary>
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
        Label("Title", header.transform, "INVENTORY  /  인벤토리", new Vector2(388, 56), new Vector2(18, 0), 24);
        closeButton = MakeButton("Close", header.transform, "X", new Vector2(36, 36), new Color(.77f, .36f, .16f), Color.white);
        TopRight((RectTransform)closeButton.transform, new Vector2(-12, -12));
        capacityText = Label("Capacity", panel.transform, "", new Vector2(432, 32), new Vector2(28, -72), 18);

        Image viewport = Box("Slot Viewport", panel.transform, new Vector2(432, 352), new Color(.82f, .73f, .56f));
        TopLeft(viewport.rectTransform, new Vector2(28, -112));
        viewport.gameObject.AddComponent<RectMask2D>();
        RectTransform content = new GameObject("Slots", typeof(RectTransform)).GetComponent<RectTransform>();
        content.SetParent(viewport.transform, false);
        int slotCount = inventory.ConfiguredCapacity;
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
            slots[i] = cell.gameObject.AddComponent<InventorySlotUI>();
            slots[i].Initialize(this, i, icon, amount);
        }
        detailText = Label("Item Details", panel.transform, "", new Vector2(432, 68), new Vector2(28, -478), 16);
        detailText.alignment = TextAlignmentOptions.TopLeft;
        detailText.textWrappingMode = TextWrappingModes.Normal;

        detailText.text = "아이템을 드래그해서 원하는 칸에 놓으세요.\n같은 아이템은 합쳐지고, 다른 아이템은 교환됩니다.";
        capacityText.text = $"전체 아이템                 0 / {slotCount} 칸";

        shortcutButton = MakeButton("Inventory Shortcut", root.transform, "가방  [I]", new Vector2(144, 42), new Color(.29f, .21f, .15f), Cream);
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
    }

    private void MatchSlotCount()
    {
        int count = inventory.Capacity;
        int previousCount = slots.Length;
        if (count == previousCount) return;
        RectTransform content = (RectTransform)slots[0].transform.parent;
        for (int i = count; i < previousCount; i++) slots[i].gameObject.SetActive(false);
        System.Array.Resize(ref slots, count);
        for (int i = previousCount; i < count; i++)
        {
            slots[i] = Instantiate(slots[0], content);
            slots[i].name = $"Slot {i + 1:00}";
            TopLeft((RectTransform)slots[i].transform, new Vector2(i % 6 * 72, -(i / 6) * 72));
        }
        content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.CeilToInt(count / 6f) * 72 - 8);
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
        TMP_Text label = Label("Label", image.transform, text, size, Vector2.zero, 20);
        // Keep the caption centered when the scene button is resized.
        Stretch(label.rectTransform, Vector2.zero, Vector2.zero);
        label.alignment = TextAlignmentOptions.Center;
        label.color = foreground;
        return button;
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
