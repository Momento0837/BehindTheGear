using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Scene-owned current location badge, updated for same-scene map travel too.</summary>
public sealed class LocationHUD : MonoBehaviour
{
    public string CurrentLocation => label != null ? label.text : string.Empty;
    public GameObject CanvasRoot => gameObject;
    private TMP_Text label;
    private CanvasGroup visibility;
    private float elapsed;
    private bool presenting;
    private const float FadeInSeconds = .4f;
    private const float HoldSeconds = 2.5f;
    private const float FadeOutSeconds = .6f;

    public static LocationHUD Create()
    {
        var root = new GameObject("Current Location UI", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup), typeof(LocationHUD));
        var hud = root.GetComponent<LocationHUD>();
        hud.Initialize();
        return hud;
    }

    private void Awake() => Initialize();

    private void Initialize()
    {
        if (label != null) return;
        visibility = GetComponent<CanvasGroup>();
        visibility.alpha = 0f;
        visibility.interactable = false;
        visibility.blocksRaycasts = false;
        var canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = .5f;

        var panel = new GameObject("Location Badge", typeof(RectTransform), typeof(Image));
        var rect = panel.GetComponent<RectTransform>();
        rect.SetParent(transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 1f);
        // Keep the arrival banner below the authored HP/MP bars at the top of the screen.
        rect.anchoredPosition = new Vector2(0, -84);
        rect.sizeDelta = new Vector2(260, 60);
        var background = panel.GetComponent<Image>();
        background.color = new Color(.035f, .045f, .055f, .82f);
        background.raycastTarget = false;

        var accent = new GameObject("Top Accent", typeof(RectTransform), typeof(Image));
        var accentRect = accent.GetComponent<RectTransform>();
        accentRect.SetParent(panel.transform, false);
        accentRect.anchorMin = new Vector2(0, 1);
        accentRect.anchorMax = Vector2.one;
        accentRect.pivot = new Vector2(.5f, 1);
        accentRect.sizeDelta = new Vector2(0, 2);
        accent.GetComponent<Image>().color = new Color(.72f, .59f, .36f);
        accent.GetComponent<Image>().raycastTarget = false;

        var text = new GameObject("Location Name", typeof(RectTransform), typeof(TextMeshProUGUI));
        var textRect = text.GetComponent<RectTransform>();
        textRect.SetParent(panel.transform, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12, 4);
        textRect.offsetMax = new Vector2(-12, -4);
        label = text.GetComponent<TextMeshProUGUI>();
        var style = Resources.Load<LocationHUDStyle>("Story/LocationHUDStyle");
        label.font = style != null && style.font != null ? style.font : TMP_Settings.defaultFontAsset;
        label.fontSize = 28;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(.96f, .93f, .85f);
        label.raycastTarget = false;
    }

    public void SetLocation(string locationName)
    {
        label.text = locationName;
        elapsed = 0f;
        visibility.alpha = 0f;
        presenting = !string.IsNullOrWhiteSpace(locationName);
    }

    private void Update()
    {
        // Start the notice after the scene's black transition has cleared.
        if (!SceneFadeTransition.IsRunning) Tick(Time.unscaledDeltaTime);
    }

    private void Tick(float deltaTime)
    {
        if (!presenting) return;
        elapsed += Mathf.Max(0f, deltaTime);
        if (elapsed < FadeInSeconds)
            visibility.alpha = Mathf.SmoothStep(0f, 1f, elapsed / FadeInSeconds);
        else if (elapsed < FadeInSeconds + HoldSeconds)
            visibility.alpha = 1f;
        else
        {
            float progress = (elapsed - FadeInSeconds - HoldSeconds) / FadeOutSeconds;
            visibility.alpha = Mathf.SmoothStep(1f, 0f, progress);
            if (progress >= 1f) presenting = false;
        }
    }

    private void OnDisable()
    {
        presenting = false;
        if (visibility != null) visibility.alpha = 0f;
    }
}
