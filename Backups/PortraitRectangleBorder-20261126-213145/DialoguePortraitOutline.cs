using UnityEngine;
using UnityEngine.UI;

// Keep the portrait's silhouette outline at three screen pixels as the UI scales.
[DisallowMultipleComponent, RequireComponent(typeof(Image)), DefaultExecutionOrder(1200)]
public sealed class DialoguePortraitOutline : MonoBehaviour
{
    private const float ThicknessPixels = 3f;
    private Image portrait;
    private Outline outline;

    public static void Apply(Image image)
    {
        if (image == null) return;
        var style = image.GetComponent<DialoguePortraitOutline>() ?? image.gameObject.AddComponent<DialoguePortraitOutline>();
        style.Configure();
    }

    private void OnEnable() => Configure();

    private void Configure()
    {
        if (portrait == null) portrait = GetComponent<Image>();
        if (outline == null) outline = GetComponent<Outline>() ?? gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, .8f);
        outline.useGraphicAlpha = true;
        outline.enabled = true;
        UpdateThickness();
    }

    private void LateUpdate() => UpdateThickness();

    private void UpdateThickness()
    {
        if (portrait == null || !portrait.isActiveAndEnabled || outline == null || portrait.canvas == null) return;
        var canvas = portrait.canvas;
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var rect = portrait.rectTransform;
        Vector2 origin = RectTransformUtility.WorldToScreenPoint(camera, rect.position);
        float scaleX = Vector2.Distance(origin, RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(Vector3.right)));
        float scaleY = Vector2.Distance(origin, RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(Vector3.up)));
        outline.effectDistance = new Vector2(ThicknessPixels / Mathf.Max(.001f, scaleX), -ThicknessPixels / Mathf.Max(.001f, scaleY));
    }
}
