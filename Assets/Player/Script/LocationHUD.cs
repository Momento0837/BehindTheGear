using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Queued location and acquisition notices using the authored Info image.</summary>
public sealed class LocationHUD : MonoBehaviour
{
    public string CurrentLocation { get; private set; } = string.Empty;
    public GameObject CanvasRoot => panel != null ? panel.gameObject : gameObject;
    private TMP_Text label;
    private RectTransform panel;
    private CanvasGroup visibility;
    private Vector2 restingPosition;
    private float horizontalPadding;
    private float targetWidth;
    private float elapsed;
    private bool presenting;
    private readonly Queue<string> notices = new();
    public void Dismiss()
    {
        notices.Clear(); presenting = false; elapsed = 0;
        if (visibility != null) visibility.alpha = 0;
    }
    private void OnDisable() => Dismiss();
    private const float HeightInSeconds = .2f;
    private const float WidthInSeconds = .3f;
    private const float FadeInSeconds = HeightInSeconds + WidthInSeconds;
    private const float HoldSeconds = 2.5f;
    private const float FadeOutSeconds = .6f;

    public static LocationHUD Create()
    {
        var root = new GameObject("Info Popup Controller");
        var hud = root.AddComponent<LocationHUD>();
        hud.Initialize();
        return hud;
    }

    private void Initialize()
    {
        var images = FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var background = images.FirstOrDefault(i => i.name == "Info" &&
            i.GetComponentInParent<Canvas>() is { isActiveAndEnabled: true });
        if (background == null)
        {
            var canvas = FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .FirstOrDefault(c => c.name == "InGame_Canvas" && c.isActiveAndEnabled);
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 10;
                var scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
            }
            var template = Resources.Load<GameObject>("UI/InfoPopup");
            if (template == null)
            {
                Debug.LogError("[Info] Missing authored InfoPopup prefab.", this);
                return;
            }
            background = Instantiate(template, canvas.transform, false).GetComponent<Image>();
            background.name = "Info";
        }
        panel = background.rectTransform;
        panel.gameObject.SetActive(true);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, 1f);
        // Match the previous badge's top offset in its 1280x720 reference canvas.
        var rootCanvas = background.canvas.rootCanvas;
        float oldScale = Mathf.Pow(Screen.width / 1280f, .5f) * Mathf.Pow(Screen.height / 720f, .5f);
        float localScale = oldScale / Mathf.Max(.001f, rootCanvas.scaleFactor);
        panel.anchoredPosition = new Vector2(0f, -84f * localScale);
        restingPosition = panel.anchoredPosition;
        background.raycastTarget = false;
        background.type = Image.Type.Sliced;
        visibility = panel.GetComponent<CanvasGroup>();
        if (visibility == null) visibility = panel.gameObject.AddComponent<CanvasGroup>();
        visibility.alpha = 0f;
        visibility.interactable = visibility.blocksRaycasts = false;
        label = panel.GetComponentInChildren<TMP_Text>(true);
        if (label == null)
        {
            var text = new GameObject("Info Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            text.transform.SetParent(panel, false);
            label = text.GetComponent<TextMeshProUGUI>();
            var style = Resources.Load<LocationHUDStyle>("Story/LocationHUDStyle");
            label.font = style != null && style.font != null ? style.font : TMP_Settings.defaultFontAsset;
            label.fontSize = 28;
            // Keep text inside the supplied decorative end caps, using its actual UI pixel scale.
            float unitsPerPixel = 1f / (background.pixelsPerUnit * background.pixelsPerUnitMultiplier);
            var border = background.sprite != null ? background.sprite.border : Vector4.zero;
            horizontalPadding = Mathf.Max(24f, (border.x + border.z) * unitsPerPixel);
        }
        else horizontalPadding = Mathf.Max(0f, panel.rect.width - label.rectTransform.rect.width);
        label.color = Color.white;
        label.enableAutoSizing = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        var textRect = label.rectTransform;
        textRect.anchorMin = textRect.anchorMax = textRect.pivot = new Vector2(.5f, .5f);
        textRect.anchoredPosition = Vector2.zero;
        if (panel.GetComponent<RectMask2D>() == null) panel.gameObject.AddComponent<RectMask2D>();
    }

    public static void ShowAcquired(InventoryItemDefinition item, int amount)
    {
        if (item == null || amount <= 0) return;
        var hud = FindFirstObjectByType<LocationHUD>();
        if (hud != null) hud.Enqueue(amount > 1 ? $"{item.DisplayName} ×{amount} 획득" : $"{item.DisplayName} 획득");
    }

    public void SetLocation(string locationName)
    {
        CurrentLocation = locationName;
        Enqueue(locationName);
    }

    private void Enqueue(string text)
    {
        if (label == null || string.IsNullOrWhiteSpace(text)) return;
        notices.Enqueue(text);
        if (!presenting) BeginNext();
    }

    private void BeginNext()
    {
        if (notices.Count == 0) { presenting = false; return; }
        label.text = notices.Dequeue();
        float textWidth = label.GetPreferredValues(label.text, Mathf.Infinity, Mathf.Infinity).x;
        targetWidth = textWidth + horizontalPadding;
        label.rectTransform.sizeDelta = new Vector2(textWidth, 152f);
        panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 1f);
        panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 0f);
        panel.anchoredPosition = restingPosition;
        visibility.alpha = 0f;
        elapsed = 0f;
        presenting = true;
    }

    private void Update()
    {
        if (!presenting || panel == null || !panel.gameObject.activeInHierarchy || SceneFadeTransition.IsRunning) return;
        elapsed += Time.unscaledDeltaTime;
        if (elapsed < FadeInSeconds)
        {
            float heightProgress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / HeightInSeconds));
            float widthProgress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - HeightInSeconds) / WidthInSeconds));
            panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Lerp(1f, targetWidth, widthProgress));
            panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 152f * heightProgress);
            visibility.alpha = heightProgress;
        }
        else
        {
            panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetWidth);
            panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 152f);
            float progress = Mathf.Clamp01((elapsed - FadeInSeconds - HoldSeconds) / FadeOutSeconds);
            visibility.alpha = 1f - Mathf.SmoothStep(0f, 1f, progress);
            panel.anchoredPosition = restingPosition + Vector2.down * (20f * progress);
            if (progress >= 1f) { presenting = false; BeginNext(); }
        }
    }
}
