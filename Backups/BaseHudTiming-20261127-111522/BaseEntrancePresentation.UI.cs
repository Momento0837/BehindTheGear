using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace BehindTheGear.HideOut
{
    public sealed partial class BaseEntrancePresentation
    {
        private const float HudEntranceDuration = .8f;
        private sealed class IconCanvasState
        {
            public Canvas canvas;
            public bool added, enabled, overrideSorting;
            public int order, layer;
        }
        private sealed class FogImage
        {
            public SpriteRenderer source;
            public Image image;
            public Material material;
        }
        private sealed class HudElement
        {
            public RectTransform rect;
            public Vector2 position, offset;
            public CanvasGroup group;
            public bool addedGroup;
            public float alpha;
        }
        private readonly List<IconCanvasState> iconCanvases = new();
        private readonly List<FogImage> fogImages = new();
        private readonly List<HudElement> hudElements = new();
        private RectTransform fogCanvasRect;
        private readonly Vector3[] uiCorners = new Vector3[4];

        private void PrepareEntranceUI(Transform inputRoot)
        {
            var root = inputRoot.GetComponent<Canvas>();
            if (root == null || root.renderMode != RenderMode.ScreenSpaceOverlay) return;
            foreach (var icon in icons)
            {
                var canvas = icon.target.GetComponent<Canvas>();
                var state = new IconCanvasState { added = canvas == null };
                if (canvas == null) canvas = icon.target.gameObject.AddComponent<Canvas>();
                state.canvas = canvas; state.enabled = canvas.enabled; state.overrideSorting = canvas.overrideSorting;
                state.order = canvas.sortingOrder; state.layer = canvas.sortingLayerID;
                iconCanvases.Add(state);
                canvas.enabled = true; canvas.overrideSorting = true;
                canvas.sortingLayerID = root.sortingLayerID; canvas.sortingOrder = root.sortingOrder - 2;
            }
            var fogObject = new GameObject("Entrance Fog Above Icons", typeof(RectTransform), typeof(Canvas));
            fogCanvasRect = fogObject.GetComponent<RectTransform>();
            fogCanvasRect.SetParent(inputRoot, false);
            fogCanvasRect.anchorMin = Vector2.zero; fogCanvasRect.anchorMax = Vector2.one;
            fogCanvasRect.offsetMin = fogCanvasRect.offsetMax = Vector2.zero;
            var fogCanvas = fogObject.GetComponent<Canvas>();
            fogCanvas.overrideSorting = true; fogCanvas.sortingLayerID = root.sortingLayerID;
            fogCanvas.sortingOrder = root.sortingOrder - 1;
            // No GraphicRaycaster: this visual layer must never intercept a pointer.
            foreach (var source in centerFog.Concat(sweeps))
            {
                var go = new GameObject(source.name + " UI", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(fogCanvasRect, false);
                var image = go.GetComponent<Image>();
                image.sprite = source.sprite; image.raycastTarget = false;
                var material = new Material(sweepMaterial) { hideFlags = HideFlags.HideAndDontSave };
                image.material = material;
                image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(.5f, .5f);
                fogImages.Add(new FogImage { source = source, image = image, material = material });
                source.forceRenderingOff = true; // Render once, in front of the icon canvases.
            }
            var view = inputRoot.GetComponent<HideOutCanvasView>();
            if (view != null)
            {
                CaptureHud(view.entranceTop, (RectTransform)inputRoot, true);
                CaptureHud(view.entranceBottom, (RectTransform)inputRoot, false);
            }
        }

        private void CaptureHud(IEnumerable<RectTransform> source, RectTransform canvas, bool top)
        {
            var targets = source.Where(r => r != null && r.gameObject.activeInHierarchy).Distinct().ToArray();
            float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
            foreach (var target in targets)
                foreach (var graphic in target.GetComponentsInChildren<Graphic>())
                {
                    graphic.rectTransform.GetWorldCorners(uiCorners);
                    foreach (var corner in uiCorners)
                    {
                        float y = canvas.InverseTransformPoint(corner).y;
                        minimum = Mathf.Min(minimum, y); maximum = Mathf.Max(maximum, y);
                    }
                }
            if (float.IsInfinity(minimum)) return;
            float distance = top ? canvas.rect.yMax - minimum + 24 : canvas.rect.yMin - maximum - 24;
            foreach (var target in targets)
            {
                var group = target.GetComponent<CanvasGroup>();
                bool added = group == null;
                if (added) group = target.gameObject.AddComponent<CanvasGroup>();
                hudElements.Add(new HudElement { rect = target, position = target.anchoredPosition,
                    offset = target.parent.InverseTransformVector(canvas.TransformVector(Vector3.up * distance)),
                    group = group, addedGroup = added, alpha = group.alpha });
            }
        }

        private void UpdateEntranceUI(float elapsedFromIcons)
        {
            float t = Mathf.Clamp01(elapsedFromIcons / HudEntranceDuration);
            float remaining = Mathf.Pow(1 - t, 3);
            foreach (var element in hudElements)
            {
                if (element.rect == null) continue;
                element.rect.anchoredPosition = element.position + element.offset * remaining;
                element.group.alpha = element.alpha * Mathf.SmoothStep(0, 1, t);
            }
            if (fogCanvasRect == null) return;
            foreach (var fog in fogImages)
            {
                if (fog.source == null || fog.image == null) continue;
                var bounds = fog.source.bounds;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(fogCanvasRect,
                    viewCamera.WorldToScreenPoint(bounds.min), null, out Vector2 minimum);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(fogCanvasRect,
                    viewCamera.WorldToScreenPoint(bounds.max), null, out Vector2 maximum);
                fog.image.rectTransform.anchoredPosition = (minimum + maximum) * .5f;
                fog.image.rectTransform.sizeDelta = maximum - minimum;
                fog.material.SetFloat("_Opacity", fog.source.color.a);
                fog.image.enabled = fog.source.color.a > 0;
            }
        }

        private void FinishEntranceUI()
        {
            foreach (var element in hudElements)
            {
                if (element.rect != null) element.rect.anchoredPosition = element.position;
                if (element.group == null) continue;
                element.group.alpha = element.alpha;
                if (element.addedGroup) Destroy(element.group);
            }
            hudElements.Clear();
            if (fogCanvasRect != null) { fogCanvasRect.gameObject.SetActive(false); Destroy(fogCanvasRect.gameObject); }
            foreach (var fog in fogImages) if (fog.material != null) Destroy(fog.material);
            fogImages.Clear();
            foreach (var state in iconCanvases)
            {
                if (state.canvas == null) continue;
                state.canvas.overrideSorting = state.overrideSorting;
                state.canvas.sortingLayerID = state.layer; state.canvas.sortingOrder = state.order;
                state.canvas.enabled = state.enabled;
                if (state.added) Destroy(state.canvas);
            }
            iconCanvases.Clear();
        }
    }
}
