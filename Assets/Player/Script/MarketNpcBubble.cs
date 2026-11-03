using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

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
    private float previousOffset;
    private float elapsed;
    private float headOffset;
    private bool dismissed;
    private int revealedCount;
    private float[] characterRevealTimes;
    public bool HasPresented { get; private set; }

    public void Configure(ConversationDialogue dialogue, Transform playerTransform, TextAsset offer)
    {
        conversation = dialogue;
        player = playerTransform;
        opening = ConversationDialogue.GetOpeningText(offer);
        characterRevealTimes = new float[opening.Length];
        int timedCharacters = 0;
        for (int i = 0; i < opening.Length;)
        {
            int end = i;
            while (end < opening.Length && opening[end] == ' ') end++;
            if (end - i >= 5)
            {
                while (i < end) characterRevealTimes[i++] = timedCharacters * CharacterDelay;
                continue;
            }
            characterRevealTimes[i++] = timedCharacters++ * CharacterDelay;
        }
        previousOffset = player.position.x - transform.position.x;
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
        text.textWrappingMode = TextWrappingModes.NoWrap;
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
        float offset = player.position.x - transform.position.x;
        // Crossing the NPC, rather than simply entering F range, preserves a direct conversation.
        bool passed = previousOffset * offset <= 0f && Mathf.Abs(offset - previousOffset) > .001f;
        previousOffset = offset;
        if (!HasPresented && passed && Mathf.Abs(offset) < 4f &&
            Mathf.Abs(player.position.y - transform.position.y) < 2.5f && !conversation.IsPlaying)
        {
            HasPresented = true;
            elapsed = 0f;
            revealedCount = 0;
            group.alpha = 0f;
            bubble.localScale = Vector3.one * .8f;
            bubble.localRotation = Quaternion.Euler(0f, 0f, -5f);
            image.gameObject.SetActive(true);
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
        if (revealedCount != count)
        {
            revealedCount = count;
            text.text = opening.Substring(0, count);
            ResizeToText();
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
    }

    private void ResizeToText()
    {
        Vector2 preferred = text.GetPreferredValues(text.text, Mathf.Infinity, Mathf.Infinity);
        float width = Mathf.Max(preferred.x, 50f) + Padding * 2f;
        float height = Mathf.Max(28f, preferred.y) + Padding * 2f + 8f * SliceScale;
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
        dismissed = true;
        if (image != null) image.gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= PositionAtRender;
        Dismiss();
    }
}
