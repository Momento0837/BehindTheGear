using UnityEngine;
using UnityEngine.UI;

// A nonblocking dimmer with a hole around the real button, so its text, hover and clicks remain intact.
[DefaultExecutionOrder(1000)]
public sealed class TutorialButtonHighlight : MonoBehaviour
{
    private Button target;
    private Canvas canvas;
    private Image border;
    private readonly Image[] dim = new Image[4];
    private readonly Vector3[] corners = new Vector3[4];
    private float elapsed, fadeElapsed, fadeFrom, opacity;
    private bool dismissing;
    public Button Target => dismissing ? null : target;

    public void SetTarget(Button button)
    {
        if (button == null || !button.isActiveAndEnabled || !button.IsInteractable()) { Dismiss(); return; }
        if (target == button && !dismissing && canvas != null) return;
        if (canvas == null)
        {
            var style = Resources.Load<InventoryOpeningHintStyle>("UI/InventoryOpeningHintStyle");
            if (style == null || style.border == null) return;
            var root = new GameObject("Button Tutorial Highlight", typeof(RectTransform), typeof(Canvas));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32010;
            for (int i = 0; i < dim.Length; i++) dim[i] = MakeImage("Tutorial Dim " + i);
            border = MakeImage("White Border Pulse");
            border.sprite = style.border;
            border.type = Image.Type.Sliced;
            border.enabled = false;
        }
        target = button; elapsed = 0; dismissing = false;
        Align();
    }

    private Image MakeImage(string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(canvas.transform, false);
        var image = go.GetComponent<Image>();
        image.raycastTarget = false;
        image.color = Color.clear;
        image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(.5f, .5f);
        image.rectTransform.pivot = new Vector2(.5f, .5f);
        return image;
    }

    public void Dismiss()
    {
        if (canvas == null || dismissing) return;
        dismissing = true; fadeElapsed = 0; fadeFrom = opacity;
        border.enabled = false;
    }

    private void LateUpdate()
    {
        if (canvas == null) return;
        if (target == null || !target.isActiveAndEnabled || !target.IsInteractable()) Dismiss();
        if (dismissing)
        {
            fadeElapsed += Time.unscaledDeltaTime;
            SetDim(Mathf.Lerp(fadeFrom, 0, Mathf.SmoothStep(0, 1, fadeElapsed / TutorialHighlightPulse.FadeSeconds)));
            if (fadeElapsed >= TutorialHighlightPulse.FadeSeconds) Clear();
            return;
        }
        elapsed += Time.unscaledDeltaTime;
        Align();
        SetDim(TutorialHighlightPulse.DimOpacity * Mathf.SmoothStep(0, 1, elapsed / TutorialHighlightPulse.FadeSeconds));
        TutorialHighlightPulse.Apply(border, elapsed);
    }

    private void Align()
    {
        var rect = (RectTransform)target.transform;
        var sourceCanvas = target.GetComponentInParent<Canvas>();
        Camera camera = sourceCanvas != null && sourceCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? sourceCanvas.worldCamera : null;
        var root = (RectTransform)canvas.transform;
        rect.GetWorldCorners(corners);
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        foreach (var corner in corners)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, RectTransformUtility.WorldToScreenPoint(camera, corner), null, out var p);
            min = Vector2.Min(min, p); max = Vector2.Max(max, p);
        }
        Place(border, min.x, min.y, max.x - min.x, max.y - min.y);
        Rect bounds = root.rect;
        min = Vector2.Max(bounds.min, Vector2.Min(bounds.max, min));
        max = Vector2.Max(min, Vector2.Min(bounds.max, max));
        Place(dim[0], bounds.xMin, bounds.yMin, bounds.width, min.y - bounds.yMin);
        Place(dim[1], bounds.xMin, max.y, bounds.width, bounds.yMax - max.y);
        Place(dim[2], bounds.xMin, min.y, min.x - bounds.xMin, max.y - min.y);
        Place(dim[3], max.x, min.y, bounds.xMax - max.x, max.y - min.y);
    }

    private static void Place(Image image, float x, float y, float width, float height)
    {
        image.rectTransform.anchoredPosition = new Vector2(x + width * .5f, y + height * .5f);
        image.rectTransform.sizeDelta = new Vector2(width, height);
    }
    private void SetDim(float alpha) { opacity = alpha; foreach (var image in dim) image.color = new Color(0, 0, 0, alpha); }
    public void Clear()
    {
        if (canvas != null) { canvas.gameObject.SetActive(false); Destroy(canvas.gameObject); }
        canvas = null; target = null; border = null; opacity = 0; dismissing = false;
    }
    private void OnDisable() => Clear();
    private void OnDestroy() => Clear();
}
