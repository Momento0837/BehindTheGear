using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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
            ApplyFont(statRows[i], ui.UiFont);
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

        for (int i = 0; i < filters.Length; i++)
        {
            TMP_Text label = filters[i] != null ? filters[i].GetComponent<TMP_Text>() : null;
            if (label != null)
            {
                ApplyFont(label, ui.UiFont);
                label.text = ui.CategoryNameForBinding(i);
            }
        }
        ApplyFont(capacity, ui.UiFont);
        ApplyFont(details, ui.UiFont);
        ApplyFont(statPoint, ui.UiFont);
        ApplyFont(statMessage, ui.UiFont);
        for (int i = 0; i < 5; i++)
        {
            Transform row = stats != null ? stats.Find($"Stats{i + 1:00}") : null;
            TMP_Text koreanName = row != null ? row.Find("Kor")?.GetComponent<TMP_Text>() : null;
            if (koreanName != null)
            {
                ApplyFont(koreanName, ui.UiFont);
                koreanName.text = ui.StatNameForBinding(i);
            }
        }
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
        List<Transform> sources = new();
        foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
        {
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
                () => ui.ShowStatDetailsForBinding(statIndex));
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
        Vector2 basePosition, float verticalOffset, System.Action beforeReveal)
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
        hover.Configure(target, requiredIcon, basePosition, verticalOffset, beforeReveal);
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
        if (font != null) text.font = font;
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

    private static void ApplyFont(TMP_Text text, TMP_FontAsset font)
    {
        if (text != null && font != null) text.font = font;
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

    public void Configure(GameObject revealTarget, Image contentIcon, Vector2 authoredPosition,
        float sourceVerticalOffset, System.Action revealAction)
    {
        target = revealTarget;
        requiredIcon = contentIcon;
        targetRect = revealTarget != null ? revealTarget.transform as RectTransform : null;
        basePosition = authoredPosition;
        verticalOffset = sourceVerticalOffset;
        beforeReveal = revealAction;
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

    public void OnPointerEnter(PointerEventData eventData) => SetVisible(true);
    public void OnPointerExit(PointerEventData eventData) => SetVisible(false);
    private void OnMouseEnter() => SetVisible(true);
    private void OnMouseExit() => SetVisible(false);
    private void OnEnable() => SetVisible(false);
    private void OnDisable() => SetVisible(false);

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
            if (targetRect != null) targetRect.anchoredPosition = basePosition + Vector2.up * verticalOffset;
            beforeReveal?.Invoke();
        }
        if (target != null && target.activeSelf != visible) target.SetActive(visible);
    }
}
