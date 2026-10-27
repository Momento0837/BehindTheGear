using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Connects the authored Inventory_Canvas prefab hierarchy to the existing inventory controller.</summary>
public static class InventoryCanvasAdapter
{
    private const string DesignedCanvasName = "Inventory_Canvas";

    public static bool TryBind(InventoryUI ui)
    {
        if (ui == null) return false;

        Canvas designedCanvas = null;
        foreach (Canvas candidate in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (candidate.gameObject.scene != ui.gameObject.scene || candidate.name != DesignedCanvasName) continue;
            designedCanvas = candidate;
            break;
        }

        if (designedCanvas == null) return false;
        Transform root = designedCanvas.transform;
        Transform infoEquip = root.Find("Info_Equip");
        Transform infoStat = root.Find("Info_Stat");
        DisableRaycasts(infoEquip);
        DisableRaycasts(infoStat);
        Transform content = root.Find("BG/Inventory/Inventory/Viewport/Content");
        Button close = Find<Button>(root, "BG/Inventory/Close");
        TMP_Text capacity = Find<TMP_Text>(root, "BG/Tabs/Count");
        TMP_Text details = Find<TMP_Text>(root, "Info_Equip/Equip_Desc");
        TMP_Text statPoint = Find<TMP_Text>(root, "Info_Stat/Stat_Name_Eng");
        TMP_Text statMessage = Find<TMP_Text>(root, "Info_Stat/Stat_Desc");
        if (content == null || close == null || capacity == null || details == null)
        {
            Debug.LogError("[InventoryUI] Inventory_Canvas hierarchy is incomplete; the legacy UI remains active.", designedCanvas);
            return false;
        }

        List<InventorySlotUI> slots = new(35);
        for (int row = 1; row <= 7; row++)
        {
            for (int column = 1; column <= 5; column++)
            {
                Image background = Find<Image>(content, $"{row}Line/{column}Slot");
                if (background == null)
                {
                    Debug.LogError($"[InventoryUI] Inventory_Canvas slot {row}:{column} is missing; the legacy UI remains active.", designedCanvas);
                    return false;
                }

                Image icon = EnsureImage(background.transform, "Bound Item Icon", new Vector2(.14f, .18f), new Vector2(.86f, .90f));
                TMP_Text amount = EnsureAmountText(background.transform, ui.UiFont);
                InventorySlotUI slot = background.GetComponent<InventorySlotUI>();
                if (slot == null) slot = background.gameObject.AddComponent<InventorySlotUI>();
                slot.Initialize(ui, slots.Count, icon, amount, false, true);
                slots.Add(slot);
            }
        }

        Button[] filters =
        {
            Find<Button>(root, "BG/Tabs/Tab1_BG/Tab1"),
            Find<Button>(root, "BG/Tabs/Tab2_BG/Tab2"),
            Find<Button>(root, "BG/Tabs/Tab3_BG/Tab3"),
            Find<Button>(root, "BG/Tabs/Tab4_BG/Tab4")
        };

        Transform stats = root.Find("BG/Player/Stats");
        TMP_Text[] statRows = new TMP_Text[5];
        Button[] statButtons = new Button[5];
        for (int i = 0; i < 5; i++)
        {
            Transform row = stats != null ? stats.Find($"Stats{i + 1:00}") : null;
            statRows[i] = row != null ? row.Find("Stat")?.GetComponent<TMP_Text>() : null;
            statButtons[i] = row != null ? row.Find("Plus")?.GetComponent<Button>() : null;
            Button minus = row != null ? row.Find("Minus")?.GetComponent<Button>() : null;
            if (minus != null) minus.interactable = false;
        }

        string[] equipmentPaths =
        {
            "BG/Player/Equipments/Equipment_Acc1",
            "BG/Player/Equipments/Equipment_Chest",
            "BG/Player/Equipments/Equipment_Neck",
            "BG/Player/Equipments/Equipment_Head",
            "BG/Player/Equipments/Equipment_Pants",
            "BG/Player/Equipments/Equipment_Acc2"
        };
        Image[] equipmentIcons = new Image[equipmentPaths.Length];
        Button[] equipmentButtons = new Button[equipmentPaths.Length];
        List<Transform> equipmentSources = new(equipmentPaths.Length);
        for (int i = 0; i < equipmentPaths.Length; i++)
        {
            Image frame = Find<Image>(root, equipmentPaths[i]);
            if (frame == null) continue;
            equipmentIcons[i] = EnsureImage(frame.transform, "Bound Equipment Icon", new Vector2(.12f, .12f), new Vector2(.88f, .88f));
            equipmentButtons[i] = frame.GetComponent<Button>();
            if (equipmentButtons[i] == null) equipmentButtons[i] = frame.gameObject.AddComponent<Button>();
            equipmentButtons[i].targetGraphic = frame;
            equipmentSources.Add(frame.transform);
        }

        BindEquipmentHovers(equipmentSources, equipmentIcons, infoEquip != null ? infoEquip.gameObject : null, ui);
        BindStatSquareHovers(root, infoStat != null ? infoStat.gameObject : null, ui);
        if (infoStat != null) infoStat.gameObject.SetActive(false);
        if (infoEquip != null) infoEquip.gameObject.SetActive(false);

        Image dragIcon = EnsureImage(root, "Bound Drag Icon", new Vector2(.5f, .5f), new Vector2(.5f, .5f));
        dragIcon.rectTransform.sizeDelta = new Vector2(64f, 64f);
        dragIcon.raycastTarget = false;
        dragIcon.gameObject.SetActive(false);

        Button exit = Find<Button>(root, "BG/Inventory/Exit");
        ui.BindDesignedCanvas(
            designedCanvas,
            root.gameObject,
            close,
            exit != null ? new[] { exit } : System.Array.Empty<Button>(),
            slots.ToArray(),
            capacity,
            details,
            dragIcon,
            filters,
            root.Find("BG/Player")?.gameObject,
            statPoint,
            statMessage,
            statRows,
            statButtons,
            equipmentButtons,
            equipmentIcons);

        // Authored labels, fonts and material presets belong to the prefab.
        return true;
    }

    public static void RefreshStatHoverBindings(InventoryUI ui)
    {
        if (ui == null || ui.Window == null) return;
        Transform root = ui.Window.transform;
        Transform infoStat = root.Find("Info_Stat");
        if (infoStat == null) return;
        DisableRaycasts(infoStat);
        BindStatSquareHovers(root, infoStat.gameObject, ui);
        infoStat.gameObject.SetActive(false);
    }

    private static void BindStatSquareHovers(Transform root, GameObject target, InventoryUI ui)
    {
        if (root == null || target == null) return;
        InventoryStatTooltipCatalog catalog = InventoryStatTooltipCatalog.Load();
        List<Transform> sources = new();
        foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            if (candidate.name == "Question_Interraction" || candidate.name == "Quest_Interraction")
                sources.Add(candidate);
        bool hasQuestionAreas = sources.Count > 0;
        foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
        {
            if (hasQuestionAreas) break;
            if (candidate == root) continue;
            bool namedSquare = candidate.name.IndexOf("Square", System.StringComparison.OrdinalIgnoreCase) >= 0;
            bool rootStatImage = candidate.parent == root
                && candidate.GetComponent<Image>() != null
                && (candidate.name == "Image" || candidate.name.StartsWith("Image (", System.StringComparison.Ordinal));
            if (!namedSquare && !rootStatImage) continue;
            sources.Add(candidate);
        }

        sources.Sort((left, right) => GetTargetLocalY(right, target).CompareTo(GetTargetLocalY(left, target)));
        Vector2 basePosition = ((RectTransform)target.transform).anchoredPosition;
        float referenceY = sources.Count > 0 ? GetTargetLocalY(sources[0], target) : 0f;
        for (int i = 0; i < sources.Count; i++)
        {
            int statIndex = i;
            BindHover(sources[i].gameObject, target, null, basePosition,
                GetTargetLocalY(sources[i], target) - referenceY,
                () => catalog.Show(target.transform, statIndex), true);
        }
    }

    private static void BindEquipmentHovers(List<Transform> sources, Image[] icons, GameObject target, InventoryUI ui)
    {
        if (target == null || sources.Count == 0) return;
        Vector2 basePosition = ((RectTransform)target.transform).anchoredPosition;
        float referenceY = float.MinValue;
        foreach (Transform source in sources) referenceY = Mathf.Max(referenceY, GetTargetLocalY(source, target));
        for (int i = 0; i < sources.Count; i++)
        {
            Transform source = sources[i];
            int equipmentIndex = System.Array.FindIndex(ui == null ? System.Array.Empty<Image>() : icons,
                icon => icon != null && icon.transform.parent == source);
            if (equipmentIndex < 0) continue;
            BindHover(source.gameObject, target, icons[equipmentIndex], basePosition,
                GetTargetLocalY(source, target) - referenceY,
                () => ui.ShowEquipmentDetailsForBinding(equipmentIndex));
        }
    }

    private static float GetTargetLocalY(Transform source, GameObject target)
    {
        RectTransform parent = target.transform.parent as RectTransform;
        return parent != null ? parent.InverseTransformPoint(source.position).y : source.position.y;
    }

    private static void BindHover(GameObject source, GameObject target, Image requiredIcon,
        Vector2 basePosition, float verticalOffset, System.Action beforeReveal, bool followPointer = false)
    {
        if (source == null || target == null) return;
        Image sourceImage = source.GetComponent<Image>();
        GameObject hoverSource = source;
        if (sourceImage != null)
        {
            sourceImage.raycastTarget = true;
        }
        else if (source.transform is RectTransform sourceRect)
        {
            Transform existing = sourceRect.Find("Bound Hover Area");
            Image hitArea = existing != null ? existing.GetComponent<Image>() : null;
            if (hitArea == null)
            {
                GameObject created = new("Bound Hover Area", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                created.layer = source.layer;
                created.transform.SetParent(sourceRect, false);
                hitArea = created.GetComponent<Image>();
            }
            RectTransform hitRect = hitArea.rectTransform;
            hitRect.anchorMin = Vector2.zero;
            hitRect.anchorMax = Vector2.one;
            hitRect.offsetMin = Vector2.zero;
            hitRect.offsetMax = Vector2.zero;
            hitArea.color = new Color(1f, 1f, 1f, 0f);
            hitArea.raycastTarget = true;
            hitArea.enabled = true;
            hoverSource = hitArea.gameObject;
        }

        InventoryHoverReveal hover = hoverSource.GetComponent<InventoryHoverReveal>();
        if (hover == null) hover = hoverSource.AddComponent<InventoryHoverReveal>();
        hover.Configure(target, requiredIcon, basePosition, verticalOffset, beforeReveal, followPointer);
    }

    private static T Find<T>(Transform root, string path) where T : Component
    {
        Transform target = root != null ? root.Find(path) : null;
        return target != null ? target.GetComponent<T>() : null;
    }

    private static Image EnsureImage(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
    {
        Transform existing = parent.Find(name);
        Image image = existing != null ? existing.GetComponent<Image>() : null;
        if (image == null)
        {
            GameObject created = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            created.transform.SetParent(parent, false);
            image = created.GetComponent<Image>();
        }

        RectTransform rect = image.rectTransform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        image.color = Color.white;
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.enabled = false;
        return image;
    }

    private static TMP_Text EnsureAmountText(Transform parent, TMP_FontAsset font)
    {
        Transform existing = parent.Find("Bound Amount");
        TMP_Text text = existing != null ? existing.GetComponent<TMP_Text>() : null;
        if (text != null) return text;
        if (text == null)
        {
            GameObject created = new("Bound Amount", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            created.transform.SetParent(parent, false);
            text = created.GetComponent<TMP_Text>();
        }

        RectTransform rect = text.rectTransform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, .36f);
        rect.offsetMin = new Vector2(4f, 2f);
        rect.offsetMax = new Vector2(-4f, 0f);
        TMP_Text authoredStyle = parent.GetComponentInParent<Canvas>()?.GetComponentInChildren<TMP_Text>(true);
        if (authoredStyle != null && authoredStyle != text)
        {
            text.font = authoredStyle.font;
            text.fontSharedMaterial = authoredStyle.fontSharedMaterial;
        }
        else if (font != null)
        {
            text.font = font;
            text.fontSharedMaterial = font.material;
        }
        text.fontSize = 16f;
        text.alignment = TextAlignmentOptions.BottomRight;
        text.raycastTarget = false;
        return text;
    }

    private static void DisableRaycasts(Transform panel)
    {
        if (panel == null) return;
        foreach (Graphic graphic in panel.GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;
    }

}

/// <summary>Shows a bound information panel only while the pointer is over this UI image or 2D sprite.</summary>
[DisallowMultipleComponent]
public sealed class InventoryHoverReveal : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private GameObject target;
    private Image requiredIcon;
    private RectTransform targetRect;
    private Vector2 basePosition;
    private float verticalOffset;
    private System.Action beforeReveal;
    private bool followPointer;
    private bool hovered;
    private Camera pointerCamera;

    public void Configure(GameObject revealTarget, Image contentIcon, Vector2 authoredPosition,
        float sourceVerticalOffset, System.Action revealAction, bool followsPointer = false)
    {
        target = revealTarget;
        requiredIcon = contentIcon;
        targetRect = revealTarget != null ? revealTarget.transform as RectTransform : null;
        basePosition = authoredPosition;
        verticalOffset = sourceVerticalOffset;
        beforeReveal = revealAction;
        followPointer = followsPointer;
        SetVisible(false);
        SpriteRenderer sprite = GetComponent<SpriteRenderer>();
        if (sprite != null && GetComponent<Collider2D>() == null)
        {
            BoxCollider2D hitArea = gameObject.AddComponent<BoxCollider2D>();
            if (sprite.sprite != null)
            {
                hitArea.offset = sprite.sprite.bounds.center;
                hitArea.size = sprite.sprite.bounds.size;
            }
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        pointerCamera = eventData.enterEventCamera;
        SetVisible(true);
    }
    public void OnPointerExit(PointerEventData eventData) => SetVisible(false);
    private void OnMouseEnter() => SetVisible(true);
    private void OnMouseExit() => SetVisible(false);
    private void OnEnable() => SetVisible(false);
    private void OnDisable() => SetVisible(false);

    private void LateUpdate()
    {
        if (!hovered || !followPointer || target == null || !target.activeInHierarchy) return;
        PositionAtPointer();
    }

    private void PositionAtPointer()
    {
        if (targetRect == null || Mouse.current == null || targetRect.parent is not RectTransform parent) return;
        Canvas canvas = targetRect.GetComponentInParent<Canvas>();
        Canvas rootCanvas = canvas != null ? canvas.rootCanvas : null;
        Camera camera = rootCanvas != null && rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null : (pointerCamera != null ? pointerCamera : rootCanvas != null ? rootCanvas.worldCamera : null);
        Vector2 screen = Mouse.current.position.ReadValue() + new Vector2(16f, -16f);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, camera, out Vector2 local)) return;
        // Place the authored panel's top-left at the pointer offset, preserving its pivot/size.
        Vector3 topLeft = parent.InverseTransformPoint(targetRect.TransformPoint(new Vector3(targetRect.rect.xMin, targetRect.rect.yMax)));
        targetRect.position += parent.TransformVector((Vector3)local - topLeft);
        targetRect.SetAsLastSibling();

        // Clamp the rendered corners in screen space (also supports scaled canvases).
        Vector3[] corners = new Vector3[4];
        targetRect.GetWorldCorners(corners);
        Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
        foreach (Vector3 corner in corners)
        {
            Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, corner);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        Vector2 correction = new(
            min.x < 8f ? 8f - min.x : Mathf.Min(0f, Screen.width - 8f - max.x),
            min.y < 8f ? 8f - min.y : Mathf.Min(0f, Screen.height - 8f - max.y));
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen + correction, camera, out Vector2 clamped))
            targetRect.position += parent.TransformVector(clamped - local);
    }

    public static void HideAll(GameObject root)
    {
        if (root == null) return;
        foreach (InventoryHoverReveal hover in root.GetComponentsInChildren<InventoryHoverReveal>(true))
            hover.SetVisible(false);
    }

    private void SetVisible(bool visible)
    {
        if (visible && requiredIcon != null
            && (!requiredIcon.enabled || requiredIcon.sprite == null || !requiredIcon.gameObject.activeInHierarchy))
            visible = false;
        if (visible)
        {
            if (!followPointer && targetRect != null) targetRect.anchoredPosition = basePosition + Vector2.up * verticalOffset;
            beforeReveal?.Invoke();
        }
        hovered = visible;
        if (target != null && target.activeSelf != visible) target.SetActive(visible);
        if (visible && followPointer) PositionAtPointer();
    }
}

/// <summary>Question areas 1..5, from top to bottom. Descriptions are limited to 26 text elements.</summary>
[System.Serializable]
public sealed class InventoryStatTooltipCatalog
{
    [System.Serializable]
    public sealed class Entry
    {
        public string Stat_Name_Kor;
        public string Stat_Name_Eng;
        public string Stat_Desc;
    }

    public Entry[] stats;

    public static InventoryStatTooltipCatalog Load()
    {
        TextAsset json = Resources.Load<TextAsset>("Inventory/stat_tooltips");
        if (json != null)
        {
            try
            {
                var catalog = JsonUtility.FromJson<InventoryStatTooltipCatalog>(json.text);
                if (catalog != null && catalog.stats != null && catalog.stats.Length == 5) return catalog;
            }
            catch (System.ArgumentException exception) { Debug.LogWarning("[InventoryUI] stat_tooltips.json: " + exception.Message); }
        }
        Debug.LogWarning("[InventoryUI] stat_tooltips.json requires five stats; using defaults.");
        string[] names = { "회피", "은신", "공격", "방어", "설득" };
        string[] english = { "(Eva)", "(Ste)", "(Atk)", "(Def)", "(Pers)" };
        var fallback = new InventoryStatTooltipCatalog { stats = new Entry[5] };
        for (int i = 0; i < 5; i++) fallback.stats[i] = new Entry { Stat_Name_Kor = names[i], Stat_Name_Eng = english[i], Stat_Desc = names[i] };
        return fallback;
    }

    public void Show(Transform panel, int index)
    {
        if (stats == null || index < 0 || index >= stats.Length || stats[index] == null) return;
        Entry entry = stats[index];
        SetText(panel, "Stat_Name_Kor", entry.Stat_Name_Kor);
        SetText(panel, "Stat_Name_Eng", entry.Stat_Name_Eng);
        string description = entry.Stat_Desc ?? string.Empty;
        int[] elements = System.Globalization.StringInfo.ParseCombiningCharacters(description);
        if (elements.Length > 26) description = description.Substring(0, elements[26]);
        SetText(panel, "Stat_Desc", description);
    }

    private static void SetText(Transform panel, string name, string value)
    {
        TMP_Text text = panel.Find(name)?.GetComponent<TMP_Text>();
        if (text == null) return;
        text.richText = false;
        if (text.text != value) text.text = value ?? string.Empty;
    }
}
