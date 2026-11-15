using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Animates authored inventory layers, restoring their layout on close.</summary>
public sealed class InventoryEntranceAnimation : MonoBehaviour
{
    const float DropDuration = 1.5f;
    const float GroupDelay = 4f / 60f;
    const float GearSettleDuration = .12f;
    sealed class Layer
    {
        public RectTransform rect;
        public Vector3 position;
        public Quaternion rotation;
        public float delay;
        public bool gear;
        public Vector3 closePosition;
        public Quaternion closeRotation;
    }
    readonly List<Layer> layers = new();
    readonly List<(Graphic graphic, bool raycast)> decorations = new();
    float elapsed, travel, duration;
    public bool IsPlaying { get; private set; }
    public bool IsClosing { get; private set; }
    public float CloseProgress => IsClosing ? Mathf.Clamp01(elapsed / Mathf.Max(.01f, duration)) : 0f;
    System.Action closed;

    public void PlayClose(System.Action onClosed)
    {
        if (IsClosing) return;
        if (layers.Count == 0) { onClosed?.Invoke(); return; }
        foreach (var layer in layers)
            if (layer.rect != null)
            { layer.closePosition = layer.rect.anchoredPosition3D; layer.closeRotation = layer.rect.localRotation; }
        closed = onClosed;
        elapsed = 0; duration = DropDuration + 3 * GroupDelay;
        IsClosing = IsPlaying = true;
    }

    public void Play()
    {
        Restore();
        var root = (RectTransform)transform;
        Canvas.ForceUpdateCanvases();
        travel = root.rect.height + 40f;
        Add("BackChain01", 0);
        Add("BackChain02", 1); Add("BackChain03", 1);
        Add("Gear", 2); Add("BG", 2);
        Add("Front_Chain", 3);
        elapsed = 0;
        IsPlaying = layers.Count > 0;
        Apply();
    }

    void Add(string name, int group)
    {
        var rect = transform.Find(name) as RectTransform;
        if (rect == null) return;
        bool gear = name == "Gear";
        float delay = group * GroupDelay;
        layers.Add(new Layer { rect = rect, position = rect.anchoredPosition3D,
            rotation = rect.localRotation, delay = delay, gear = gear });
        duration = Mathf.Max(duration, delay + DropDuration + (gear ? GearSettleDuration : 0));
        // Decorative art must not catch clicks while the inventory moves underneath it.
        if (name != "BG")
            foreach (var graphic in rect.GetComponentsInChildren<Graphic>(true))
            { decorations.Add((graphic, graphic.raycastTarget)); graphic.raycastTarget = false; }
    }

    void Update()
    {
        if (!IsPlaying) return;
        elapsed += Time.unscaledDeltaTime;
        Apply();
        if (elapsed >= duration)
        {
            IsPlaying = false;
            var callback = closed; closed = null;
            callback?.Invoke();
        }
    }

    void Apply()
    {
        foreach (var layer in layers)
        {
            if (layer.rect == null) continue;
            float delay = IsClosing ? 3 * GroupDelay - layer.delay : layer.delay;
            float progress = Mathf.Clamp01((elapsed - delay) / DropDuration);
            // Asymmetric S-curve: smooth acceleration, then a long, gentle deceleration.
            // Both endpoints have zero velocity; peak speed occurs at 20% of the descent.
            float eased = 1 - Mathf.Pow(1 - progress, 5) * (1 + 5 * progress);
            if (IsClosing)
            {
                layer.rect.anchoredPosition3D = Vector3.Lerp(layer.closePosition, layer.position + Vector3.up * travel, eased);
                if (layer.gear)
                    layer.rect.localRotation = Quaternion.Slerp(layer.closeRotation, layer.rotation * Quaternion.Euler(0, 0, 15), eased);
                continue;
            }
            layer.rect.anchoredPosition3D = layer.position + Vector3.up * (travel * (1 - eased));
            if (!layer.gear) continue;
            float angle = 15 * (1 - eased);
            if (progress >= 1)
            {
                float settle = Mathf.Clamp01((elapsed - layer.delay - DropDuration) / GearSettleDuration);
                angle = -Mathf.Sin(settle * Mathf.PI);
                if (settle >= 1) angle = 0;
            }
            layer.rect.localRotation = layer.rotation * Quaternion.Euler(0, 0, angle);
        }
    }

    void Restore()
    {
        foreach (var layer in layers)
            if (layer.rect != null)
            { layer.rect.anchoredPosition3D = layer.position; layer.rect.localRotation = layer.rotation; }
        foreach (var decoration in decorations)
            if (decoration.graphic != null) decoration.graphic.raycastTarget = decoration.raycast;
        layers.Clear(); decorations.Clear(); duration = 0; IsPlaying = IsClosing = false; closed = null;
    }
    void OnDisable() => Restore();
}
