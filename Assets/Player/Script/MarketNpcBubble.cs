using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.InputSystem;

/// <summary>One passing greeting, anchored to the Market NPC rather than the player.</summary>
[DefaultExecutionOrder(1100)]
public sealed class MarketNpcBubble : MonoBehaviour
{
    private const float CharacterDelay = .045f;
    private const float GlyphDuration = .24f;
    private const float EntranceDuration = .42f;
    private const float Padding = 26f;
    private ConversationDialogue conversation;
    private Transform player;
    private Canvas canvas;
    private RectTransform bubble;
    private Image image;
    private CanvasGroup group;
    private TMP_Text text;
    private string opening;
    private string[] greetingLines;
    private int lineIndex;
    private bool fastCompleting;
    private float fastStarted;
    private float fastStartWidth;
    private float fullWidth;
    private float fullHeight;
    private float elapsed;
    private float headOffset;
    private bool dismissed;
    private int revealedCount;
    private float[] characterRevealTimes;
    private PlayerControlLock controls;
    private PlayerCameraFollow2D focusCamera;
    private bool holdsControls;
    public event System.Action Finished;
    public bool IsPresenting => holdsControls;
    public bool HasPresented { get; private set; }

    public void Configure(ConversationDialogue dialogue, Transform playerTransform, TextAsset offer)
    {
        conversation = dialogue;
        player = playerTransform;
        greetingLines = ConversationDialogue.GetGreetingLines(offer, 3);
        opening = greetingLines.Length > 0 ? greetingLines[0] : string.Empty;
        image = dialogue.TalkingImage;
        if (gameObject.scene.name != "Market" || image == null || string.IsNullOrEmpty(opening))
        {
            enabled = false;
            return;
        }

        // Give Talking its own canvas: the normal dialogue canvas stays hidden until F is used.
        var root = new GameObject("Market NPC Talking Canvas", typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);
        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        bubble = image.rectTransform;
        bubble.SetParent(root.transform, false);
        bubble.anchorMin = bubble.anchorMax = new Vector2(.5f, .5f);
        image.raycastTarget = false;
        image.type = Image.Type.Sliced;
        group = image.GetComponent<CanvasGroup>();
        if (group == null) group = image.gameObject.AddComponent<CanvasGroup>();
        group.interactable = group.blocksRaycasts = false;

        var label = new GameObject("Talking Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        text = label.GetComponent<TextMeshProUGUI>();
        text.rectTransform.SetParent(bubble, false);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(Padding, Padding + 8f * SliceScale);
        text.rectTransform.offsetMax = new Vector2(-Padding, -Padding);
        var font = Resources.Load<TMP_FontAsset>("Dialogue/Fonts/SuhgungDialogue");
        text.font = font != null ? font : dialogue.BodyText.font;
        text.fontSharedMaterial = font != null ? font.material : dialogue.BodyText.fontSharedMaterial;
        text.fontSize = 21.6f;
        text.fontStyle = FontStyles.Normal;
        text.color = new Color(.12f, .09f, .07f, 1f);
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        text.text = string.Empty;

        // Measure once so idle/laugh animation frames cannot make the bubble bob.
        headOffset = 2f;
        foreach (var renderer in GetComponentsInChildren<SpriteRenderer>())
            if (renderer.enabled) headOffset = Mathf.Max(headOffset, renderer.bounds.max.y - transform.position.y);
        headOffset += .25f;
        ResizeToText();
        image.gameObject.SetActive(false);
        // The gameplay camera also clamps its view matrix just before rendering.
        RenderPipelineManager.beginCameraRendering += PositionAtRender;
    }

    private void Update()
    {
        if (dismissed || player == null || image == null) return;
        if (!HasPresented && player.position.x >= 70f &&
            Mathf.Abs(player.position.y - transform.position.y) < 2.5f && !conversation.IsPlaying)
        {
            HasPresented = true;
            controls = player.GetComponent<PlayerControlLock>();
            controls?.Acquire();
            holdsControls = true;
            focusCamera = Camera.main != null ? Camera.main.GetComponent<PlayerCameraFollow2D>() : null;
            focusCamera?.BeginDialogueFocus(conversation, player);
            BeginLine(0);
            group.alpha = 0f;
            bubble.localScale = Vector3.one * .8f;
            bubble.localRotation = Quaternion.Euler(0f, 0f, -5f);
            image.gameObject.SetActive(true);
            return;
        }
        if (IsPresenting && (Mouse.current?.leftButton.wasPressedThisFrame == true ||
            Keyboard.current?.spaceKey.wasPressedThisFrame == true)) Advance();
    }

    private void BeginLine(int index)
    {
        lineIndex = index;
        opening = FormatBubbleText(greetingLines[index]);
        elapsed = 0f;
        revealedCount = 0;
        fastCompleting = false;
        text.text = string.Empty;
        characterRevealTimes = new float[opening.Length];
        int timed = 0;
        for (int i = 0; i < opening.Length;)
        {
            int end = i;
            while (end < opening.Length && opening[end] == ' ') end++;
            if (end - i >= 5)
            {
                while (i < end) characterRevealTimes[i++] = timed * CharacterDelay;
                continue;
            }
            characterRevealTimes[i++] = timed++ * CharacterDelay;
        }
        Vector2 full = Measure(opening);
        fullWidth = Mathf.Max(full.x, 50f) + Padding * 2f + 2f;
        fullHeight = Mathf.Max(28f, full.y) + Padding * 2f + 8f * SliceScale;
        ResizeToText();
    }

    private void Advance()
    {
        if (!IsPresenting || fastCompleting) return;
        if (revealedCount < opening.Length)
        {
            fastCompleting = true;
            fastStarted = elapsed;
            fastStartWidth = bubble.sizeDelta.x;
            int remaining = opening.Length - revealedCount;
            for (int i = revealedCount; i < opening.Length; i++)
                characterRevealTimes[i] = elapsed + .18f * (i - revealedCount + 1) / remaining;
            return;
        }
        if (lineIndex + 1 < greetingLines.Length) BeginLine(lineIndex + 1);
        else
        {
            Dismiss();
            Finished?.Invoke();
        }
    }

    private void LateUpdate()
    {
        if (!HasPresented || dismissed || bubble == null) return;
        if (conversation.IsPlaying) { Dismiss(); return; }
        elapsed += Time.unscaledDeltaTime;
        float entrance = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / EntranceDuration));
        group.alpha = entrance;
        bubble.localScale = Vector3.one * Mathf.Lerp(.8f, 1f, entrance);
        float rotation = elapsed < .2f
            ? Mathf.Lerp(-5f, 5f, Mathf.SmoothStep(0f, 1f, elapsed / .2f))
            : Mathf.Lerp(5f, 0f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - .2f) / .28f)));
        bubble.localRotation = Quaternion.Euler(0f, 0f, rotation);

        int count = revealedCount;
        while (count < opening.Length && characterRevealTimes[count] <= elapsed) count++;
        if (fastCompleting)
        {
            float time = elapsed - fastStarted;
            float width = time < .22f
                ? Mathf.Lerp(fastStartWidth, fullWidth * 1.05f, Mathf.SmoothStep(0f, 1f, time / .22f))
                : Mathf.Lerp(fullWidth * 1.05f, fullWidth, Mathf.SmoothStep(0f, 1f, (time - .22f) / .16f));
            SetBubbleSize(width, fullHeight);
            // Reveal only glyphs that fit inside the expanding body.
            while (count > revealedCount && Measure(opening.Substring(0, count)).x > width - Padding * 2f) count--;
            for (int i = revealedCount; i < count; i++) characterRevealTimes[i] = elapsed;
            if (time >= .38f) fastCompleting = false;
        }
        if (revealedCount != count)
        {
            revealedCount = count;
            text.text = opening.Substring(0, count);
            if (!fastCompleting) ResizeToText();
        }
        AnimateGlyphs();
        var camera = Camera.main;
        if (camera == null) return;
        PositionBubble(camera);
    }

    private void PositionAtRender(ScriptableRenderContext context, Camera camera)
    {
        if (HasPresented && !dismissed && camera == Camera.main) PositionBubble(camera);
    }

    private void PositionBubble(Camera camera)
    {
        float entrance = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / EntranceDuration));
        Vector3 screen = camera.WorldToScreenPoint(transform.position + Vector3.up * headOffset);
        image.enabled = screen.z > 0f;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas.transform as RectTransform,
            screen, null, out Vector2 local);
        bubble.anchoredPosition = local + Vector2.down * (18f * (1f - entrance));
        // Keep the full rotated/scaled body inside the canvas as the camera zooms.
        var canvasRect = (RectTransform)canvas.transform;
        var corners = new Vector3[4];
        bubble.GetWorldCorners(corners);
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (var corner in corners)
        {
            Vector2 point = canvasRect.InverseTransformPoint(corner);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        Rect bounds = canvasRect.rect;
        Vector2 correction = Vector2.zero;
        if (max.x > bounds.xMax - 16f) correction.x = bounds.xMax - 16f - max.x;
        if (min.x + correction.x < bounds.xMin + 16f) correction.x = bounds.xMin + 16f - min.x;
        if (max.y > bounds.yMax - 16f) correction.y = bounds.yMax - 16f - max.y;
        if (min.y + correction.y < bounds.yMin + 16f) correction.y = bounds.yMin + 16f - min.y;
        bubble.anchoredPosition += correction;
    }

    public static string FormatBubbleText(string value) =>
        System.Text.RegularExpressions.Regex.Replace(value ?? string.Empty, @"(?<=[.。!?])[ \t]+(?=\S)", "\n");

    private Vector2 Measure(string value)
    {
        float canvasWidth = ((RectTransform)canvas.transform).rect.width;
        float bodyWidth = Mathf.Max(50f, Mathf.Min(520f, canvasWidth * .7f) - Padding * 2f);
        return text.GetPreferredValues(value, bodyWidth, Mathf.Infinity);
    }

    private void ResizeToText()
    {
        Vector2 preferred = Measure(text.text);
        float width = Mathf.Max(preferred.x, 50f) + Padding * 2f + 2f;
        float height = Mathf.Max(28f, preferred.y) + Padding * 2f + 8f * SliceScale;
        SetBubbleSize(width, height);
    }

    private void SetBubbleSize(float width, float height)
    {
        bubble.sizeDelta = new Vector2(width, height);
        // The tip lies inside the fixed left/bottom slices, so its pixel inset stays constant as we grow.
        bubble.pivot = new Vector2(6f * SliceScale / width, 3f * SliceScale / height);
    }

    private float SliceScale => image.sprite != null
        ? canvas.referencePixelsPerUnit / (image.sprite.pixelsPerUnit * image.pixelsPerUnitMultiplier) : 1f;

    private void AnimateGlyphs()
    {
        // Rebuild clean geometry before adding offsets, avoiding accumulated vertex movement.
        text.havePropertiesChanged = true;
        text.ForceMeshUpdate();
        var info = text.textInfo;
        for (int i = 0; i < info.characterCount; i++)
        {
            var character = info.characterInfo[i];
            if (!character.isVisible) continue;
            float progress = Mathf.Clamp01((elapsed - characterRevealTimes[character.index]) / GlyphDuration);
            float rise = -14f * (1f - Mathf.SmoothStep(0f, 1f, progress));
            var mesh = info.meshInfo[character.materialReferenceIndex];
            for (int corner = 0; corner < 4; corner++)
            {
                int vertex = character.vertexIndex + corner;
                mesh.vertices[vertex] += Vector3.up * rise;
                var color = mesh.colors32[vertex];
                color.a = (byte)Mathf.RoundToInt(color.a * progress);
                mesh.colors32[vertex] = color;
            }
        }
        text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices | TMP_VertexDataUpdateFlags.Colors32);
    }

    public void Dismiss()
    {
        if (holdsControls)
        {
            holdsControls = false;
            controls?.Release();
            focusCamera?.EndDialogueFocus(conversation);
        }
        dismissed = true;
        if (image != null) image.gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= PositionAtRender;
        Dismiss();
    }
}
