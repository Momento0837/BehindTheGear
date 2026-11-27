using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(1100)]
public sealed class AutomaticNpcBubble : MonoBehaviour
{
    Canvas canvas;
    RectTransform bubble;
    TMP_Text text;
    Image image;
    CanvasGroup group;
    float entranceElapsed, exitDrop;
    public bool ScreenRight { get; set; }
    public bool ScreenLeft { get; set; }
    public float Opacity { get; set; } = 1f;
    public Vector3? RightMapEdge { get; set; }
    bool ScreenAnchored => ScreenRight || ScreenLeft;
    float MaximumOpacity => Mathf.Clamp01(Opacity);
    public bool PlayerReaction { get; set; }
    float sliceScale = 1f;
    Vector3 headAnchor = Vector3.up * 2.55f;
    bool headAnchorCaptured;
    const float HeadGap = 12f; // Reference-resolution UI pixels, independent of the camera lens.
    const float FadeDuration = .5f;
    float EntranceDuration => ScreenAnchored || PlayerReaction ? .48f : FadeDuration;
    public bool IsPlaying { get; private set; }
    public int RevealedLineCount { get; private set; }
    public void Begin(Image style, string[] lines)
    {
        IsPlaying = true;
        RevealedLineCount = 0;
        // A scene-root overlay cannot inherit the NPC's world transform or animation scale.
        var root = new GameObject(PlayerReaction ? "Player Reaction Bubble" : ScreenAnchored ? "Pursuit Bubble" : "Selena Automatic Bubble", typeof(Canvas), typeof(CanvasScaler));
        if (root.scene != gameObject.scene) SceneManager.MoveGameObjectToScene(root, gameObject.scene);
        canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 20;
        var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1280,720);
        bubble = new GameObject("Talking", typeof(RectTransform)).GetComponent<RectTransform>();
        bubble.SetParent(root.transform, false);
        group = bubble.gameObject.AddComponent<CanvasGroup>();
        group.interactable = group.blocksRaycasts = false;
        image = new GameObject("Mirrored Background", typeof(RectTransform), typeof(Image)).GetComponent<Image>(); image.transform.SetParent(bubble,false);
        image.rectTransform.anchorMin = Vector2.zero; image.rectTransform.anchorMax = Vector2.one;
        image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero;
        image.rectTransform.localScale = new Vector3(PlayerReaction || ScreenLeft ? 1 : -1, 1, 1);
        image.sprite = style != null ? style.sprite : null; image.type = Image.Type.Sliced; image.raycastTarget = false;
        if (style != null)
        {
            image.pixelsPerUnitMultiplier = style.pixelsPerUnitMultiplier;
            image.color = style.color; image.material = style.material;
        }
        sliceScale = image.sprite != null ? canvas.referencePixelsPerUnit / (image.sprite.pixelsPerUnit * image.pixelsPerUnitMultiplier) : 1f;
        bubble.anchorMin = bubble.anchorMax = new Vector2(.5f, .5f);
        text = new GameObject("Text",typeof(RectTransform),typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>(); text.transform.SetParent(bubble,false);
        text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one; text.rectTransform.offsetMin = new Vector2(26,26 + 8 * sliceScale); text.rectTransform.offsetMax = new Vector2(-26,-26);
        UIFontTheme.Apply(text, UIFontTheme.Normal); text.fontSize = 22; text.color = new Color(.12f,.09f,.07f); text.raycastTarget = false;
        if (ScreenAnchored || PlayerReaction)
        {
            var font = Resources.Load<TMP_FontAsset>("Dialogue/Fonts/SuhgungDialogue");
            if (font != null) { text.font = font; text.fontSharedMaterial = font.material; }
            text.fontSize = 21.6f;
            text.alignment = TextAlignmentOptions.Center;
        }
        // Enlarge the reaction glyph around its center without resizing the bubble layout.
        if (PlayerReaction) text.rectTransform.localScale = Vector3.one * 1.7f;
        text.textWrappingMode = ScreenAnchored || PlayerReaction ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
        CaptureHeadAnchor();
        RenderPipelineManager.beginCameraRendering += PositionAtRender;
        StartCoroutine(Play(lines));
    }
    IEnumerator Play(string[] lines)
    {
        for(int i=0;i<lines.Length;i++)
        {
            // Pursuit shouts expand horizontally; only explicit JSON newlines break the line.
            text.text=ScreenAnchored ? lines[i] : MarketNpcBubble.FormatBubbleText(lines[i]); text.maxVisibleCharacters=0;
            float maxWidth = Mathf.Max(50, Mathf.Min(520, ((RectTransform)canvas.transform).rect.width * .7f) - 52);
            Vector2 size=text.GetPreferredValues(text.text,ScreenAnchored ? Mathf.Infinity : maxWidth,Mathf.Infinity);
            float width = Mathf.Max(50, size.x) + 54;
            float height = Mathf.Max(28,size.y) + 52 + 8 * sliceScale;
            bubble.sizeDelta=new Vector2(width,height);
            float tailInset = 6 * sliceScale / width;
            bubble.pivot=new Vector2(PlayerReaction || ScreenLeft ? tailInset : 1 - tailInset, 3 * sliceScale / height);
            ApplyEntrance(0);
            bubble.gameObject.SetActive(true); text.ForceMeshUpdate();
            for(float t=0;t<Mathf.Max(EntranceDuration,lines[i].Length*.045f);t+=Time.deltaTime)
            { ApplyEntrance(t); text.maxVisibleCharacters=Mathf.FloorToInt(t/.045f);yield return null; }
            ApplyEntrance(EntranceDuration);
            text.maxVisibleCharacters=int.MaxValue;
            RevealedLineCount = i + 1;
            yield return new WaitForSeconds(.5f);
            for (float fade = 0; fade < FadeDuration; fade += Time.deltaTime)
            { float progress = Mathf.Clamp01(fade / FadeDuration); group.alpha = MaximumOpacity * (1 - progress); exitDrop = (ScreenAnchored ? 18 : 15) * progress; yield return null; }
            group.alpha = 0; bubble.gameObject.SetActive(false); exitDrop = 0;
            if(i+1<lines.Length)yield return new WaitForSeconds(1);
        }
        IsPlaying = false;
    }
    void ApplyEntrance(float elapsed)
    {
        entranceElapsed = elapsed;
        if (!ScreenAnchored && !PlayerReaction)
        {
            group.alpha = MaximumOpacity * Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / FadeDuration));
            bubble.localScale = Vector3.one;
            bubble.localRotation = Quaternion.identity;
            return;
        }
        float appear = Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / (PlayerReaction ? .42f : .22f)));
        float settle = Mathf.SmoothStep(0, 1, Mathf.Clamp01((elapsed - .22f) / .16f));
        float widthScale = elapsed < .22f ? Mathf.Lerp(.8f, 1.05f, appear) : Mathf.Lerp(1.05f, 1, settle);
        group.alpha = MaximumOpacity * appear;
        bubble.localScale = PlayerReaction ? Vector3.one * (.5f * Mathf.Lerp(.8f, 1, appear))
            : new Vector3(widthScale, Mathf.Lerp(.8f, 1, appear), 1);
        float angle = elapsed < .2f ? Mathf.Lerp(-5, 5, Mathf.SmoothStep(0, 1, elapsed / .2f))
            : Mathf.Lerp(5, 0, Mathf.SmoothStep(0, 1, Mathf.Clamp01((elapsed - .2f) / .28f)));
        bubble.localRotation = Quaternion.Euler(0, 0, ScreenLeft ? -angle : angle);
    }
    void LateUpdate()
    {
        if (Camera.main != null) PositionBubble(Camera.main);
    }
    void PositionAtRender(ScriptableRenderContext context, Camera camera)
    {
        if (camera == Camera.main) PositionBubble(camera);
    }
    void PositionBubble(Camera camera)
    {
        if(bubble==null)return;
        CaptureHeadAnchor();
        var screen = ScreenAnchored ? new Vector3(Screen.width * (ScreenLeft ? .04f : .96f), Screen.height * .5f, 1)
            : camera.WorldToScreenPoint(transform.TransformPoint(headAnchor));
        if (ScreenRight && RightMapEdge.HasValue)
        {
            // Keep the source attached to the map edge as the camera pans or zooms.
            Vector3 edgeScreen = camera.WorldToScreenPoint(RightMapEdge.Value);
            screen.x = edgeScreen.x;
            screen.z = edgeScreen.z;
        }
        image.enabled = screen.z > 0;
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)canvas.transform,screen,null,out var local);
        // The tail pivot is inset from the image edge; keep the right border inside the map.
        if (ScreenRight && RightMapEdge.HasValue) local.x -= bubble.rect.width * (1f - bubble.pivot.x);
        if (!ScreenAnchored) local.y += HeadGap;
        local.y -= exitDrop;
        float riseDuration = PlayerReaction ? .42f : ScreenAnchored ? .22f : FadeDuration;
        float riseDistance = ScreenAnchored || PlayerReaction ? 18f : 15f;
        local.y -= riseDistance * (1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01(entranceElapsed / riseDuration)));
        // Follow the speaker even outside the viewport; edge clamping breaks the head anchor.
        bubble.anchoredPosition = local;
    }
    void CaptureHeadAnchor()
    {
        if (headAnchorCaptured || ScreenAnchored) return;
        var sprite = GetComponent<SpriteRenderer>();
        if (sprite == null || sprite.sprite == null) return;
        // Capture once: walking frames must not make the bubble jitter with changing sprite bounds.
        headAnchor = transform.InverseTransformPoint(new Vector3(sprite.bounds.center.x, sprite.bounds.max.y, transform.position.z));
        headAnchorCaptured = true;
    }
    void OnEnable() { if (canvas != null) canvas.gameObject.SetActive(true); }
    void OnDisable() { if (canvas != null) canvas.gameObject.SetActive(false); }
    void OnDestroy(){RenderPipelineManager.beginCameraRendering -= PositionAtRender;if(canvas!=null)Destroy(canvas.gameObject);}
}
