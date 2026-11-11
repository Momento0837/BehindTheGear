using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Depth-weighted UI perspective around a smoothly moving pointer focal point.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
public sealed class MenuPointerParallax : MonoBehaviour
{
    [Serializable]
    private sealed class Layer
    {
        public RectTransform target;
        [Range(0f, 1f)] public float depth = .5f;
        [NonSerialized] public Vector2 origin;
        [NonSerialized] public Vector3 originalScale;
        [NonSerialized] public Quaternion originalRotation;
    }

    [SerializeField] private Layer[] layers = Array.Empty<Layer>();
    [Tooltip("Maximum offset in Canvas reference-resolution units.")]
    [SerializeField] private Vector2 maxOffset = new(32f, 52f);
    [SerializeField, Min(.01f)] private float smoothTime = .2f;
    [SerializeField, Range(0f, .1f)] private float perspectiveScale = .025f;
    [Tooltip("Maximum pitch and yaw in degrees at the closest layer.")]
    [SerializeField] private Vector2 tiltDegrees = new(3f, 2f);
    private Canvas canvas;
    private Vector2 offset;
    private Vector2 velocity;
    private float engagement, engagementVelocity;

    private void OnEnable()
    {
        canvas = GetComponent<Canvas>();
        offset = velocity = Vector2.zero;
        engagement = engagementVelocity = 0f;
        foreach (var layer in layers)
            if (layer.target != null)
            {
                layer.origin = layer.target.anchoredPosition;
                layer.originalScale = layer.target.localScale;
                layer.originalRotation = layer.target.localRotation;
            }
    }

    private void LateUpdate()
    {
        Vector2 desired = Vector2.zero;
        bool pointerInside = false;
        if (Application.isFocused && Mouse.current != null)
        {
            Rect viewport = canvas.renderMode != RenderMode.ScreenSpaceOverlay && canvas.worldCamera != null
                ? canvas.worldCamera.pixelRect : new Rect(0f, 0f, Screen.width, Screen.height);
            Vector2 pointer = Mouse.current.position.ReadValue();
            if (viewport.width > 0f && viewport.height > 0f && viewport.Contains(pointer))
            {
                Vector2 normalized = new Vector2((pointer.x - viewport.xMin) / viewport.width,
                    (pointer.y - viewport.yMin) / viewport.height) * 2f - Vector2.one;
                desired = normalized;
                pointerInside = true;
            }
        }
        offset = Vector2.SmoothDamp(offset, desired, ref velocity, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
        engagement = Mathf.SmoothDamp(engagement, pointerInside ? 1f : 0f, ref engagementVelocity,
            smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
        RectTransform canvasRect = (RectTransform)canvas.transform;
        Vector2 focalPoint = canvasRect.rect.center + Vector2.Scale(offset, canvasRect.rect.size * .5f);
        Vector3 worldFocus = canvasRect.TransformPoint(focalPoint);
        foreach (var layer in layers)
        {
            var rect = layer.target;
            if (rect == null) continue;
            // Resolve the authored anchor position again, so resizing the canvas keeps its layout.
            rect.anchoredPosition = layer.origin;
            Vector3 home = rect.localPosition;
            Vector3 focus = rect.parent.InverseTransformPoint(worldFocus);
            focus.z = home.z;
            float scale = 1f + perspectiveScale * layer.depth * engagement;
            Quaternion tilt = Quaternion.Euler(-offset.y * tiltDegrees.x * layer.depth,
                offset.x * tiltDegrees.y * layer.depth, 0f);
            Vector3 projected = focus + tilt * ((home - focus) * scale);
            projected -= (Vector3)(Vector2.Scale(offset, maxOffset) * layer.depth);
            projected.z = home.z;
            rect.localPosition = projected;
            rect.localScale = layer.originalScale * scale;
            rect.localRotation = tilt * layer.originalRotation;
        }
    }

    private void OnDisable()
    {
        foreach (var layer in layers)
            if (layer.target != null)
            {
                layer.target.anchoredPosition = layer.origin;
                layer.target.localScale = layer.originalScale;
                layer.target.localRotation = layer.originalRotation;
            }
        offset = velocity = Vector2.zero;
        engagement = engagementVelocity = 0f;
    }
}
