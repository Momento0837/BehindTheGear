using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>Plays a cutscene, hides gameplay UI and shows hold-to-skip progress.</summary>
public sealed class CutsceneVideoSequence : MonoBehaviour
{
    [SerializeField] private VideoPlayer videoPlayer;
    [SerializeField] private GameObject[] uiRootsToHide;
    [SerializeField] private PlayerControlLock playerControlLock;
    [SerializeField] private ConversationDialogue dialogueAfterVideo;
    [SerializeField, Min(.1f)] private float skipHoldSeconds = 3f;

    public bool IsPlaying => playing;
    public void BindPlayer(PlayerControlLock value) => playerControlLock = value;
    private bool playing;
    private bool ownsLock;
    private float heldSeconds;
    private bool[] uiWasActive;
    private GameObject skipRoot;
    private TMP_Text skipText;
    private Image skipFill;
    private Texture2D skipRingTexture;
    private GameObject videoRoot;
    private RawImage videoImage;
    private AspectRatioFitter videoFit;
    private RenderTexture videoTexture;

    public void IncludeUi(GameObject root)
    {
        if (root == null) return;
        var roots = new System.Collections.Generic.List<GameObject>(uiRootsToHide ?? System.Array.Empty<GameObject>());
        if (!roots.Contains(root)) roots.Add(root);
        uiRootsToHide = roots.ToArray();
    }

    private void Awake()
    {
        if (playerControlLock == null) playerControlLock = FindFirstObjectByType<PlayerControlLock>();
        if (videoPlayer == null) return;
        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        videoPlayer.aspectRatio = VideoAspectRatio.Stretch;
        videoPlayer.loopPointReached += OnVideoFinished;
    }

    public void PlayFromInteraction()
    {
        if (playing || videoPlayer == null) return;
        if (videoPlayer.clip == null && string.IsNullOrWhiteSpace(videoPlayer.url))
        {
            Debug.LogWarning("[Cutscene] Assign a VideoClip or URL before playing.", this);
            return;
        }
        StartCoroutine(PlayRoutine());
    }

    private IEnumerator PlayRoutine()
    {
        playing = true;
        heldSeconds = 0f;
        playerControlLock?.Acquire();
        ownsLock = playerControlLock != null;
        foreach (var hud in FindObjectsByType<PlayerHealthHUD>(FindObjectsSortMode.None)) IncludeUi(hud.CanvasRoot);
        HideUiForVideo();
        CreateVideoUi();
        videoRoot.SetActive(true);
        videoImage.color = Color.clear;
        ConfigureVideoSurface(videoPlayer.clip != null ? (int)videoPlayer.clip.width : 1920,
            videoPlayer.clip != null ? (int)videoPlayer.clip.height : 1080);
        CreateSkipUi();
        skipRoot.SetActive(true);
        UpdateSkipUi();
        videoPlayer.enabled = true;
        videoPlayer.Prepare();
        float timeout = 10f;
        while (playing && !videoPlayer.isPrepared && timeout > 0f)
        {
            if (UpdateSkip()) { Finish(true); yield break; }
            timeout -= Time.unscaledDeltaTime;
            yield return null;
        }
        if (!playing) yield break;
        if (!videoPlayer.isPrepared)
        {
            Debug.LogError("[Cutscene] Video preparation timed out.", this);
            Finish(false);
            yield break;
        }
        ConfigureVideoSurface((int)videoPlayer.width, (int)videoPlayer.height);
        videoImage.color = Color.white;
        videoPlayer.Play();
        while (playing)
        {
            if (UpdateSkip()) { Finish(true); yield break; }
            yield return null;
        }
    }

    private bool UpdateSkip()
    {
        heldSeconds = Keyboard.current?.spaceKey.isPressed == true
            ? heldSeconds + Time.unscaledDeltaTime : 0f;
        UpdateSkipUi();
        return heldSeconds >= skipHoldSeconds;
    }

    private void UpdateSkipUi()
    {
        float progress = Mathf.Clamp01(heldSeconds / skipHoldSeconds);
        skipFill.fillAmount = progress;
        skipText.text = progress > 0f
            ? $"컷신 스킵 중... {Mathf.CeilToInt(progress * 100)}%"
            : $"Space {skipHoldSeconds:0.#}초 길게 누르기 · 컷신 스킵";
    }

    private void OnVideoFinished(VideoPlayer _) { if (playing) Finish(true); }

    private void Finish(bool continueDialogue)
    {
        if (!playing) return;
        playing = false;
        if (videoPlayer != null) { videoPlayer.Stop(); videoPlayer.enabled = false; }
        if (videoRoot != null) videoRoot.SetActive(false);
        ReleaseVideoTexture();
        if (skipRoot != null) skipRoot.SetActive(false);
        RestoreUiAfterVideo();
        heldSeconds = 0f;
        if (continueDialogue && dialogueAfterVideo != null) dialogueAfterVideo.Play();
        if (ownsLock) playerControlLock?.Release();
        ownsLock = false;
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        Finish(false);
    }

    private void OnDestroy()
    {
        if (videoPlayer != null) videoPlayer.loopPointReached -= OnVideoFinished;
        Finish(false);
        if (skipFill != null && skipFill.sprite != null) Destroy(skipFill.sprite);
        if (skipRingTexture != null) Destroy(skipRingTexture);
        if (skipRoot != null) Destroy(skipRoot);
        if (videoRoot != null) Destroy(videoRoot);
    }

    private void HideUiForVideo()
    {
        if (uiRootsToHide == null) return;
        uiWasActive = new bool[uiRootsToHide.Length];
        for (int i = 0; i < uiRootsToHide.Length; i++)
        {
            var root = uiRootsToHide[i];
            if (root == null) continue;
            uiWasActive[i] = root.activeSelf;
            root.SetActive(false);
        }
    }

    private void RestoreUiAfterVideo()
    {
        if (uiRootsToHide == null || uiWasActive == null) return;
        for (int i = 0; i < uiRootsToHide.Length; i++)
            if (uiRootsToHide[i] != null) uiRootsToHide[i].SetActive(uiWasActive[i]);
        uiWasActive = null;
    }

    private void CreateSkipUi()
    {
        if (skipRoot != null) return;
        skipRoot = new GameObject("Cutscene Skip UI", typeof(Canvas), typeof(CanvasScaler));
        var canvas = skipRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        var scaler = skipRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = .5f;
        var panel = new GameObject("Skip Panel", typeof(RectTransform), typeof(Image));
        var rect = panel.GetComponent<RectTransform>();
        rect.SetParent(skipRoot.transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-24, -24);
        rect.sizeDelta = new Vector2(480, 84);
        panel.GetComponent<Image>().color = new Color(0, 0, 0, .7f);
        panel.GetComponent<Image>().raycastTarget = false;
        var label = new GameObject("Skip Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        var labelRect = label.GetComponent<RectTransform>();
        labelRect.SetParent(rect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(12, 0);
        labelRect.offsetMax = new Vector2(-92, 0);
        skipText = label.GetComponent<TextMeshProUGUI>();
        var inventoryUi = FindFirstObjectByType<InventoryUI>();
        skipText.font = inventoryUi != null && inventoryUi.UiFont != null ? inventoryUi.UiFont : TMP_Settings.defaultFontAsset;
        skipText.fontSize = 22;
        skipText.alignment = TextAlignmentOptions.Center;
        skipText.raycastTarget = false;
        Sprite ringSprite = CreateSkipRingSprite();
        var track = new GameObject("Skip Ring Track", typeof(RectTransform), typeof(Image));
        var trackRect = track.GetComponent<RectTransform>();
        trackRect.SetParent(rect, false);
        trackRect.anchorMin = trackRect.anchorMax = new Vector2(1, .5f);
        trackRect.anchoredPosition = new Vector2(-46, 0);
        trackRect.sizeDelta = new Vector2(64, 64);
        var trackImage = track.GetComponent<Image>();
        trackImage.sprite = ringSprite;
        trackImage.color = new Color(1, 1, 1, .2f);
        trackImage.raycastTarget = false;
        var bar = new GameObject("Skip Ring Progress", typeof(RectTransform), typeof(Image));
        var barRect = bar.GetComponent<RectTransform>();
        barRect.SetParent(trackRect, false);
        barRect.anchorMin = Vector2.zero;
        barRect.anchorMax = Vector2.one;
        barRect.offsetMin = barRect.offsetMax = Vector2.zero;
        skipFill = bar.GetComponent<Image>();
        skipFill.sprite = ringSprite;
        skipFill.type = Image.Type.Filled;
        skipFill.fillMethod = Image.FillMethod.Radial360;
        skipFill.fillOrigin = (int)Image.Origin360.Top;
        skipFill.fillClockwise = true;
        skipFill.color = new Color(.8f, 1f, .45f);
        skipFill.raycastTarget = false;
    }

    private void CreateVideoUi()
    {
        if (videoRoot != null) return;
        videoRoot = new GameObject("Cutscene Fullscreen Video", typeof(Canvas));
        var canvas = videoRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 31900;
        var black = new GameObject("Black Background", typeof(RectTransform), typeof(Image));
        var background = black.GetComponent<RectTransform>();
        background.SetParent(videoRoot.transform, false);
        background.anchorMin = Vector2.zero;
        background.anchorMax = Vector2.one;
        background.offsetMin = background.offsetMax = Vector2.zero;
        black.GetComponent<Image>().color = Color.black;
        black.GetComponent<Image>().raycastTarget = false;
        var display = new GameObject("Video", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
        var rect = display.GetComponent<RectTransform>();
        rect.SetParent(background, false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        videoImage = display.GetComponent<RawImage>();
        videoImage.raycastTarget = false;
        videoFit = display.GetComponent<AspectRatioFitter>();
        videoFit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
    }

    private void ConfigureVideoSurface(int width, int height)
    {
        width = Mathf.Max(1, width);
        height = Mathf.Max(1, height);
        videoFit.aspectRatio = (float)width / height;
        if (videoTexture != null && videoTexture.width == width && videoTexture.height == height) return;
        ReleaseVideoTexture();
        videoTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
        {
            name = "Cutscene Video Surface"
        };
        videoTexture.Create();
        videoImage.texture = videoTexture;
        videoPlayer.targetTexture = videoTexture;
    }

    private void ReleaseVideoTexture()
    {
        if (videoTexture == null) return;
        if (videoPlayer != null) videoPlayer.targetTexture = null;
        if (videoImage != null) videoImage.texture = null;
        videoTexture.Release();
        Destroy(videoTexture);
        videoTexture = null;
    }

    private Sprite CreateSkipRingSprite()
    {
        const int size = 128;
        skipRingTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Cutscene Skip Ring",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float radius = new Vector2(x + .5f - size / 2f, y + .5f - size / 2f).magnitude;
            float alpha = Mathf.Clamp01(Mathf.Min(radius - 46f, 60f - radius));
            pixels[y * size + x] = new Color(1, 1, 1, alpha);
        }
        skipRingTexture.SetPixels(pixels);
        skipRingTexture.Apply(false, true);
        return Sprite.Create(skipRingTexture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size);
    }
}
