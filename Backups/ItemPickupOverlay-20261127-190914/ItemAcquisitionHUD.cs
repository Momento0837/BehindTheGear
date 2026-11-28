using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Item notices that merge by item ID and stack between the hotbar and tutorial.</summary>
public sealed class ItemAcquisitionHUD : MonoBehaviour
{
    private const float RowHeight = 64f;
    private const float AuthoredHeight = 152f;
    private const float RowGap = 8f;
    private const float EntrySeconds = .4f;
    private const float ReorderSeconds = .35f;
    private const float HoldSeconds = 3f;
    private const float FadeSeconds = .45f;
    private const float ScreenMargin = 24f;
    private const float ReservedTutorialHalfHeight = 132f;

    private sealed class Notice
    {
        public string itemId;
        public string displayName;
        public int amount;
        public RectTransform rect;
        public TMP_Text label;
        public CanvasGroup opacity;
        public Vector2 moveFrom, moveTo;
        public float moveTime, moveDuration;
        public float age, alphaFrom, revealTime;
        public float naturalWidth;
    }

    // Index zero is always the most recently acquired item, at the bottom of the stack.
    private readonly List<Notice> notices = new();
    private readonly List<RectTransform> hudBounds = new();
    private readonly List<RectTransform> tutorialBounds = new();
    private readonly Vector3[] corners = new Vector3[4];
    private Canvas canvas;
    private CanvasGroup visibility;
    private RectTransform viewport;
    private Image sourceImage;
    private TMP_Text sourceLabel;
    private float horizontalPadding;
    private float currentRowHeight = RowHeight;
    private float nextBoundsRefresh;
    private Vector2 lastViewportSize;

    public static ItemAcquisitionHUD Create(Transform owner, Image background, TMP_Text label, float padding)
    {
        var root = new GameObject("Item Acquisition Notifications", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        root.transform.SetParent(owner, false);
        var hud = root.AddComponent<ItemAcquisitionHUD>();
        hud.Initialize(background, label, padding);
        return hud;
    }

    private void Initialize(Image background, TMP_Text label, float padding)
    {
        sourceImage = background;
        sourceLabel = label;
        horizontalPadding = Mathf.Max(24f, padding);
        canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // The inventory is order 50; acquisition notices belong with the gameplay HUD.
        canvas.overrideSorting = true;
        canvas.sortingOrder = 20;
        var scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        canvas.scaleFactor = Mathf.Max(.001f, Mathf.Min(Screen.width / 1920f, Screen.height / 1080f));
        visibility = GetComponent<CanvasGroup>();
        visibility.interactable = visibility.blocksRaycasts = false;
        var area = new GameObject("Pickup Stack Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport = area.GetComponent<RectTransform>();
        viewport.SetParent(transform, false);
        viewport.anchorMin = viewport.anchorMax = viewport.pivot = Vector2.zero;
        Canvas.ForceUpdateCanvases();
        RefreshBoundsSources();
        UpdateLayout();
    }

    public void Show(InventoryItemDefinition item, int amount)
    {
        if (item == null || amount <= 0 || viewport == null || sourceImage == null || sourceLabel == null) return;
        if (Time.unscaledTime >= nextBoundsRefresh) RefreshBoundsSources();
        UpdateLayout();
        string itemId = string.IsNullOrWhiteSpace(item.ItemId) ? $"instance:{item.GetInstanceID()}" : item.ItemId;
        Notice notice = notices.Find(existing => existing.itemId == itemId);
        bool isNew = notice == null;
        if (isNew)
        {
            notice = CreateNotice();
            notice.itemId = itemId;
        }
        else notices.Remove(notice);
        notice.amount += amount;
        notice.displayName = item.DisplayName;
        notice.age = 0f;
        notice.alphaFrom = notice.opacity.alpha;
        notice.revealTime = 0f;
        notice.label.text = notice.amount > 1
            ? $"{notice.displayName} {notice.amount}개 획득"
            : $"{notice.displayName} 획득";
        SizeNotice(notice);
        notices.Insert(0, notice);
        if (isNew)
            notice.rect.anchoredPosition = new Vector2(-notice.rect.rect.width * notice.rect.localScale.x - ScreenMargin,
                currentRowHeight * .5f);
        Reorder(isNew ? EntrySeconds : ReorderSeconds);
    }

    public void Clear()
    {
        foreach (var notice in notices)
            if (notice.rect != null) Release(notice.rect.gameObject);
        notices.Clear();
    }

    private Notice CreateNotice()
    {
        var root = new GameObject("Acquired Item", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        var rect = root.GetComponent<RectTransform>();
        rect.SetParent(viewport, false);
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0f, .5f);
        var image = root.GetComponent<Image>();
        image.sprite = sourceImage.sprite;
        image.color = sourceImage.color;
        image.material = sourceImage.material;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = sourceImage.pixelsPerUnitMultiplier;
        image.raycastTarget = false;
        image.maskable = true;
        var opacity = root.GetComponent<CanvasGroup>();
        opacity.alpha = 0f;
        opacity.blocksRaycasts = opacity.interactable = false;
        var text = new GameObject("Item Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        text.transform.SetParent(rect, false);
        var label = text.GetComponent<TextMeshProUGUI>();
        label.font = sourceLabel.font;
        label.fontSharedMaterial = sourceLabel.fontSharedMaterial;
        label.fontStyle = sourceLabel.fontStyle;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        label.maskable = true;
        label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = new Vector2(.5f, .5f);
        label.rectTransform.anchoredPosition = Vector2.zero;
        return new Notice { rect = rect, label = label, opacity = opacity };
    }

    private void SizeNotice(Notice notice)
    {
        float scale = currentRowHeight / AuthoredHeight;
        notice.rect.localScale = Vector3.one * scale;
        // Preserve the Info's decorative end caps/height while keeping its text readable in compact rows.
        notice.label.enableAutoSizing = false;
        notice.label.fontSize = sourceLabel.fontSize / scale;
        float textWidth = notice.label.GetPreferredValues(notice.label.text, Mathf.Infinity, Mathf.Infinity).x;
        notice.naturalWidth = textWidth + horizontalPadding;
        float width = Mathf.Min(notice.naturalWidth, (viewport.rect.width - ScreenMargin) / scale);
        notice.rect.sizeDelta = new Vector2(width, AuthoredHeight);
        notice.label.rectTransform.sizeDelta = new Vector2(Mathf.Max(1f, width - horizontalPadding), AuthoredHeight);
    }

    private void Reorder(float duration)
    {
        for (int i = 0; i < notices.Count; i++)
        {
            var notice = notices[i];
            notice.moveFrom = notice.rect.anchoredPosition;
            notice.moveTo = new Vector2(ScreenMargin, currentRowHeight * .5f + i * (currentRowHeight + RowGap));
            notice.moveTime = 0f;
            notice.moveDuration = duration;
        }
    }

    private void LateUpdate()
    {
        if (canvas == null || viewport == null || notices.Count == 0) return;
        if (Time.unscaledTime >= nextBoundsRefresh) RefreshBoundsSources();
        UpdateLayout();
        bool hidden = SceneFadeTransition.IsRunning || CutsceneVideoSequence.IsAnyVisible;
        visibility.alpha = hidden ? 0f : 1f;
        if (hidden) return;
        AdvanceNotices(Time.unscaledDeltaTime);
    }

    private void AdvanceNotices(float delta)
    {
        bool removed = false;
        for (int i = notices.Count - 1; i >= 0; i--)
        {
            var notice = notices[i];
            notice.age += delta;
            notice.moveTime += delta;
            notice.revealTime += delta;
            float move = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(notice.moveTime / notice.moveDuration));
            notice.rect.anchoredPosition = Vector2.LerpUnclamped(notice.moveFrom, notice.moveTo, move);
            float fade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((notice.age - HoldSeconds) / FadeSeconds));
            float reveal = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(notice.revealTime / .15f));
            notice.opacity.alpha = Mathf.Lerp(notice.alphaFrom, 1f, reveal) * (1f - fade);
            if (notice.age < HoldSeconds + FadeSeconds) continue;
            Release(notice.rect.gameObject);
            notices.RemoveAt(i);
            removed = true;
        }
        if (removed) Reorder(ReorderSeconds);
    }

    private void RefreshBoundsSources()
    {
        nextBoundsRefresh = Time.unscaledTime + .5f;
        hudBounds.Clear();
        tutorialBounds.Clear();
        var vitals = FindFirstObjectByType<InGameVitalsHUD>();
        if (vitals != null)
        {
            foreach (string name in new[] { "HP_Frame", "MP_Frame", "HotBar_Frame" })
            {
                var root = vitals.transform.Find(name);
                if (root == null) continue;
                hudBounds.AddRange(root.GetComponentsInChildren<RectTransform>(true));
            }
        }
        foreach (var tutorial in FindObjectsByType<ControlTutorialUI>(FindObjectsSortMode.None))
        {
            if (tutorial.PresentationRoot == null) continue;
            var visual = tutorial.PresentationRoot.transform.Find("Animated Keys") as RectTransform;
            if (visual != null) tutorialBounds.Add(visual);
        }
    }

    private void UpdateLayout()
    {
        float scale = Mathf.Max(.001f, canvas.scaleFactor);
        float screenHeight = Screen.height / scale;
        float screenWidth = Screen.width / scale;
        float top = screenHeight - 230f;
        bool foundHud = false;
        foreach (var rect in hudBounds)
        {
            if (rect == null || !rect.gameObject.activeInHierarchy) continue;
            float bottom = ScreenBounds(rect).yMin / scale;
            top = foundHud ? Mathf.Min(top, bottom - 16f) : bottom - 16f;
            foundHud = true;
        }
        // Reserve the largest tutorial even between hints, preventing the stack from jumping vertically.
        float bottomEdge = screenHeight * .5f + ReservedTutorialHalfHeight + 16f;
        foreach (var rect in tutorialBounds)
        {
            if (rect == null || !rect.gameObject.activeInHierarchy || rect.anchorMin.x > .5f) continue;
            bottomEdge = Mathf.Max(bottomEdge, ScreenBounds(rect).yMax / scale + 16f);
        }
        float availableHeight = Mathf.Max(1f, top - bottomEdge);
        currentRowHeight = Mathf.Min(RowHeight, availableHeight);
        viewport.anchoredPosition = new Vector2(0f, bottomEdge);
        viewport.sizeDelta = new Vector2(ScreenMargin + Mathf.Min(620f, screenWidth * .46f), availableHeight);
        // Excess old notices leave through the upper edge of this mask, never covering the hotbar.
        if ((lastViewportSize - viewport.sizeDelta).sqrMagnitude < .01f) return;
        lastViewportSize = viewport.sizeDelta;
        foreach (var notice in notices) SizeNotice(notice);
        Reorder(ReorderSeconds);
    }

    private static void Release(GameObject target)
    {
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }

    private Rect ScreenBounds(RectTransform rect)
    {
        rect.GetWorldCorners(corners);
        var sourceCanvas = rect.GetComponentInParent<Canvas>();
        Camera camera = sourceCanvas != null && sourceCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? sourceCanvas.worldCamera : null;
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < corners.Length; i++)
        {
            Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
}