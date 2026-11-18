using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;

[DefaultExecutionOrder(1100)]
public sealed class AutomaticNpcBubble : MonoBehaviour
{
    Canvas canvas;
    RectTransform bubble;
    TMP_Text text;
    Image image;
    CanvasGroup group;
    float entranceElapsed;
    float sliceScale = 1f;
    float headOffset = 2.8f;
    public bool IsPlaying { get; private set; }
    public void Begin(Image style, string[] lines)
    {
        IsPlaying = true;
        var root = new GameObject("Selena Automatic Bubble", typeof(Canvas), typeof(CanvasScaler)); root.transform.SetParent(transform, false);
        canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 20;
        var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1280,720);
        bubble = new GameObject("Talking", typeof(RectTransform)).GetComponent<RectTransform>();
        bubble.SetParent(root.transform, false);
        group = bubble.gameObject.AddComponent<CanvasGroup>();
        group.interactable = group.blocksRaycasts = false;
        image = new GameObject("Mirrored Background", typeof(RectTransform), typeof(Image)).GetComponent<Image>(); image.transform.SetParent(bubble,false);
        image.rectTransform.anchorMin = Vector2.zero; image.rectTransform.anchorMax = Vector2.one;
        image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero;
        image.rectTransform.localScale = new Vector3(-1, 1, 1);
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
        text.textWrappingMode = TextWrappingModes.Normal;
        var sprite = GetComponent<SpriteRenderer>();
        if (sprite != null) headOffset = sprite.bounds.max.y - transform.position.y + .25f;
        RenderPipelineManager.beginCameraRendering += PositionAtRender;
        StartCoroutine(Play(lines));
    }
    IEnumerator Play(string[] lines)
    {
        for(int i=0;i<lines.Length;i++)
        {
            text.text=MarketNpcBubble.FormatBubbleText(lines[i]); text.maxVisibleCharacters=0;
            float maxWidth = Mathf.Max(50, Mathf.Min(520, ((RectTransform)canvas.transform).rect.width * .7f) - 52);
            Vector2 size=text.GetPreferredValues(text.text,maxWidth,Mathf.Infinity);
            float width = Mathf.Max(50, size.x) + 54;
            float height = Mathf.Max(28,size.y) + 52 + 8 * sliceScale;
            bubble.sizeDelta=new Vector2(width,height);
            bubble.pivot=new Vector2(1 - 6 * sliceScale / width, 3 * sliceScale / height);
            ApplyEntrance(0);
            bubble.gameObject.SetActive(true); text.ForceMeshUpdate();
            for(float t=0;t<Mathf.Max(.48f,lines[i].Length*.045f);t+=Time.deltaTime)
            { ApplyEntrance(t); text.maxVisibleCharacters=Mathf.FloorToInt(t/.045f);yield return null; }
            ApplyEntrance(.48f);
            text.maxVisibleCharacters=int.MaxValue; yield return new WaitForSeconds(.5f); bubble.gameObject.SetActive(false);
            if(i+1<lines.Length)yield return new WaitForSeconds(1);
        }
        IsPlaying = false;
    }
    void ApplyEntrance(float elapsed)
    {
        entranceElapsed = elapsed;
        float appear = Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / .22f));
        float settle = Mathf.SmoothStep(0, 1, Mathf.Clamp01((elapsed - .22f) / .16f));
        float widthScale = elapsed < .22f ? Mathf.Lerp(0, 1.05f, appear) : Mathf.Lerp(1.05f, 1, settle);
        group.alpha = appear;
        bubble.localScale = new Vector3(widthScale, Mathf.Lerp(.8f, 1, appear), 1);
        float angle = elapsed < .2f ? Mathf.Lerp(-5, 5, Mathf.SmoothStep(0, 1, elapsed / .2f))
            : Mathf.Lerp(5, 0, Mathf.SmoothStep(0, 1, Mathf.Clamp01((elapsed - .2f) / .28f)));
        bubble.localRotation = Quaternion.Euler(0, 0, angle);
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
        var screen=camera.WorldToScreenPoint(transform.position+Vector3.up*headOffset);
        image.enabled = screen.z > 0;
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)canvas.transform,screen,null,out var local);
        local.y -= 18f * (1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01(entranceElapsed / .22f)));
        bubble.anchoredPosition=local;
        var rect = ((RectTransform)canvas.transform).rect;
        local.x = Mathf.Clamp(local.x, rect.xMin + 16 + bubble.pivot.x * bubble.sizeDelta.x,
            rect.xMax - 16 - (1 - bubble.pivot.x) * bubble.sizeDelta.x);
        local.y = Mathf.Clamp(local.y, rect.yMin + 16 + bubble.pivot.y * bubble.sizeDelta.y,
            rect.yMax - 16 - (1 - bubble.pivot.y) * bubble.sizeDelta.y);
        bubble.anchoredPosition = local;
    }
    void OnDestroy(){RenderPipelineManager.beginCameraRendering -= PositionAtRender;if(canvas!=null)Destroy(canvas.gameObject);}
}
