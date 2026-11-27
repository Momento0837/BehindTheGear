using UnityEngine;
using UnityEngine.UI;

// A rectangular frame around the whole portrait viewport, including transparent image margins.
[DisallowMultipleComponent, RequireComponent(typeof(Image)), DefaultExecutionOrder(1200)]
public sealed class DialoguePortraitOutline : MonoBehaviour
{
    private const float ThicknessPixels = 3f;
    private const string FrameName = "Portrait Rectangle Border";
    private Image portrait, content;
    private RectTransform frame;
    private readonly Image[] edges = new Image[4];

    public static void Apply(Image image, Image displayedContent = null)
    {
        if (image == null) return;
        var style = image.GetComponent<DialoguePortraitOutline>() ?? image.gameObject.AddComponent<DialoguePortraitOutline>();
        style.content = displayedContent != null ? displayedContent : image;
        style.Configure();
    }

    private void OnEnable() => Configure();

    private void Configure()
    {
        if (portrait == null) portrait = GetComponent<Image>();
        if (content == null) content = portrait;
        // Remove the previous silhouette effect, including on an already open dialogue.
        if (portrait.TryGetComponent<Outline>(out var oldOutline)) oldOutline.enabled = false;
        if (content != portrait)
        {
            if (content.TryGetComponent<Outline>(out oldOutline)) oldOutline.enabled = false;
            if (content.TryGetComponent<DialoguePortraitOutline>(out var oldStyle)) oldStyle.enabled = false;
        }
        if (frame == null)
        {
            frame = transform.Find(FrameName) as RectTransform;
            if (frame == null)
            {
                frame = new GameObject(FrameName, typeof(RectTransform)).GetComponent<RectTransform>();
                frame.gameObject.layer = gameObject.layer;
                frame.SetParent(transform, false);
            }
            frame.anchorMin = Vector2.zero; frame.anchorMax = Vector2.one;
            frame.offsetMin = frame.offsetMax = Vector2.zero;
            string[] names = { "Top", "Bottom", "Left", "Right" };
            for (int i = 0; i < edges.Length; i++)
            {
                var child = frame.Find(names[i]);
                edges[i] = child != null ? child.GetComponent<Image>() : null;
                if (edges[i] == null)
                {
                    edges[i] = new GameObject(names[i], typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
                    edges[i].gameObject.layer = gameObject.layer;
                    edges[i].transform.SetParent(frame, false);
                }
                edges[i].raycastTarget = false;
                edges[i].sprite = null;
                var edge = edges[i].rectTransform;
                edge.anchorMin = edge.anchorMax = edge.pivot = Vector2.zero;
            }
        }
        UpdateFrame();
    }

    private void LateUpdate() => UpdateFrame();
    private void OnDisable() { if (frame != null) frame.gameObject.SetActive(false); }

    private void UpdateFrame()
    {
        if (frame == null || portrait == null) return;
        bool visible = content != null && content.isActiveAndEnabled && content.overrideSprite != null;
        if (frame.gameObject.activeSelf != visible) frame.gameObject.SetActive(visible);
        if (!visible) return;
        var canvas = portrait.GetComponentInParent<Canvas>();
        if (canvas == null) return;
        if (frame.GetSiblingIndex() != transform.childCount - 1) frame.SetAsLastSibling();
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var rect = portrait.rectTransform;
        Vector2 origin = RectTransformUtility.WorldToScreenPoint(camera, rect.position);
        float scaleX = Vector2.Distance(origin, RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(Vector3.right)));
        float scaleY = Vector2.Distance(origin, RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(Vector3.up)));
        float width = rect.rect.width, height = rect.rect.height;
        float thicknessX = Mathf.Min(width * .5f, ThicknessPixels / Mathf.Max(.001f, scaleX));
        float thicknessY = Mathf.Min(height * .5f, ThicknessPixels / Mathf.Max(.001f, scaleY));
        Color color = new Color(0f, 0f, 0f, .8f * content.color.a);
        foreach (var edge in edges) edge.color = color;
        // Draw inside the viewport so its existing RectMask2D cannot clip half of the border.
        // Side bars exclude the corners: opacity stays 80% instead of accumulating there.
        Place(edges[0], 0, height - thicknessY, width, thicknessY);
        Place(edges[1], 0, 0, width, thicknessY);
        Place(edges[2], 0, thicknessY, thicknessX, Mathf.Max(0, height - 2 * thicknessY));
        Place(edges[3], width - thicknessX, thicknessY, thicknessX, Mathf.Max(0, height - 2 * thicknessY));
    }

    private static void Place(Image image, float x, float y, float width, float height)
    {
        image.rectTransform.anchoredPosition = new Vector2(x, y);
        image.rectTransform.sizeDelta = new Vector2(width, height);
    }
}
