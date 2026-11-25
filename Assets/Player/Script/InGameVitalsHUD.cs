using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Updates vitals and heartbeat visuals without moving or resizing authored HUD elements.</summary>
[DisallowMultipleComponent]
public sealed class InGameVitalsHUD : MonoBehaviour
{
    [Serializable]
    private sealed class Gauge
    {
        public Image foreground = null;
        public Image trail = null;
        private bool initialized;
        private float target, startFill, elapsed;
        private bool animating;

        public void SetValue(float ratio)
        {
            if (foreground == null || trail == null) return;
            ratio = Mathf.Clamp01(ratio);
            if (!initialized)
            {
                initialized = true;
                target = ratio;
                foreground.fillAmount = ratio;
                ClearTrail();
                return;
            }
            if (Mathf.Approximately(target, ratio)) return;
            float previous = target;
            target = ratio;
            foreground.fillAmount = ratio;
            if (ratio < previous)
            {
                // Repeated hits retain the still-visible extent of the previous trail.
                startFill = Mathf.Max(previous, animating ? trail.fillAmount : previous);
                trail.fillAmount = startFill;
                SetOpacity(1f);
                elapsed = 0f;
                animating = true;
            }
            else ClearTrail();
        }

        public void Tick(float deltaTime, float delay, float duration)
        {
            if (!animating || trail == null) return;
            elapsed += deltaTime;
            float progress = Mathf.Clamp01((elapsed - delay) / Mathf.Max(.01f, duration));
            trail.fillAmount = Mathf.Lerp(startFill, target, progress);
            SetOpacity(1f - progress);
            if (progress >= 1f) animating = false;
        }

        public void Reset()
        {
            initialized = false;
            ClearTrail();
        }

        private void ClearTrail()
        {
            animating = false;
            if (trail == null) return;
            trail.fillAmount = target;
            SetOpacity(0f);
        }

        private void SetOpacity(float alpha)
        {
            Color color = trail.color;
            color.a = alpha;
            trail.color = color;
        }
    }

    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private PlayerMana playerMana;
    [SerializeField] private Gauge hp = new();
    [SerializeField] private Gauge mp = new();
    [SerializeField, Min(0f)] private float trailDelay = .1f;
    [SerializeField, Min(.01f)] private float trailDuration = .6f;
    private float nextSearch;
    private Material healthPulseMaterial, originalHealthMaterial;
    private Color originalHealthTint;
    private float heartbeatTime;
    private RawImage healthRipple;
    private Material healthRippleMaterial;
    private bool rippleShaderUnavailable;
    private const float HeartbeatThreshold = .4f;
    private static readonly int TintId = Shader.PropertyToID("_Color");
    private static readonly int RipplePhaseId = Shader.PropertyToID("_Phase");
    private static readonly int RippleStrengthId = Shader.PropertyToID("_Strength");
    private static readonly int RippleRectId = Shader.PropertyToID("_RectSize");
    private static readonly int RippleBarId = Shader.PropertyToID("_Bar");
    private static InGameVitalsHUD rippleOwner;
    private readonly Vector3[] rippleCorners = new Vector3[4];
    private float ripplePhase, rippleStrength;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRippleOwner() => rippleOwner = null;

    // The world shader refracts scenery under the smooth UI wave; HUD text/fill stay crisp.
    public static bool TryGetRipple(out Vector4 bar, out Vector4 parameters)
    {
        bar = parameters = Vector4.zero;
        var owner = rippleOwner;
        if (owner == null || !owner.isActiveAndEnabled || owner.healthRipple == null || !owner.healthRipple.isActiveAndEnabled) return false;
        var rect = owner.hp.foreground.rectTransform;
        rect.GetWorldCorners(owner.rippleCorners);
        var canvas = owner.hp.foreground.canvas;
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var a = RectTransformUtility.WorldToScreenPoint(camera, owner.rippleCorners[0]);
        var b = RectTransformUtility.WorldToScreenPoint(camera, owner.rippleCorners[2]);
        float width = (b.x - a.x) * owner.hp.foreground.fillAmount;
        bar = new Vector4((a.x + width * .5f) / Screen.width, (a.y + b.y) * .5f / Screen.height,
            width * .5f / Screen.width, (b.y - a.y) * .5f / Screen.height);
        float scale = Mathf.Abs(b.y - a.y) / Mathf.Max(.01f, rect.rect.height);
        parameters = new Vector4(owner.ripplePhase, owner.rippleStrength, scale, 1);
        return true;
    }

    private void UpdateHealthPulse()
    {
        float ratio = playerHealth != null ? (float)playerHealth.CurrentHealth / Mathf.Max(1, playerHealth.MaxHealth) : 1f;
        bool critical = playerHealth != null && !playerHealth.IsDead && ratio <= HeartbeatThreshold;
        if (!critical || hp.foreground == null) { ResetHealthPulse(); return; }
        if (healthPulseMaterial == null)
        {
            originalHealthMaterial = hp.foreground.material;
            if (originalHealthMaterial == null || !originalHealthMaterial.HasProperty(TintId)) return;
            // A private material brightens the red texture even when Image.color is already white.
            // Never modify the shared HUD material or the frame/trailing damage image.
            healthPulseMaterial = new Material(originalHealthMaterial) { name = "HP Fill Heartbeat" };
            originalHealthTint = originalHealthMaterial.GetColor(TintId);
            hp.foreground.material = healthPulseMaterial;
            heartbeatTime = 0;
        }
        heartbeatTime += Time.unscaledDeltaTime;
        float phase = Mathf.Repeat(heartbeatTime, 1.2f);
        float pulse = phase < .24f ? Mathf.Pow(Mathf.Sin(Mathf.PI * phase / .24f), 2)
            : phase >= .32f && phase < .54f ? .55f * Mathf.Pow(Mathf.Sin(Mathf.PI * (phase - .32f) / .22f), 2) : 0;
        float strength = Mathf.Clamp01(1f - ratio / HeartbeatThreshold);
        Color tint = originalHealthTint * (1f + Mathf.Lerp(.2f, .95f, strength) * pulse);
        tint.a = originalHealthTint.a;
        healthPulseMaterial.SetColor(TintId, tint);
        // Masked UI may render a stencil copy of this private material.
        var renderedMaterial = hp.foreground.materialForRendering;
        if (renderedMaterial != healthPulseMaterial && renderedMaterial.HasProperty(TintId))
            renderedMaterial.SetColor(TintId, tint);
        UpdateHealthRipple(phase, strength);
    }

    private void UpdateHealthRipple(float phase, float strength)
    {
        if (healthRipple == null && !rippleShaderUnavailable)
        {
            var shader = Resources.Load<Shader>("Effects/HealthHeartbeatRipple");
            if (shader == null || !shader.isSupported) { rippleShaderUnavailable = true; return; }
            healthRippleMaterial = new Material(shader) { name = "HP Heartbeat Ripple" };
            healthRipple = new GameObject("HP Heartbeat Ripple", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage)).GetComponent<RawImage>();
            healthRipple.transform.SetParent(hp.foreground.transform, false);
            healthRipple.gameObject.layer = hp.foreground.gameObject.layer;
            // The HUD is screen-space overlay, after the world's low-health post effect.
            // Keep the wave inside that canvas, below menus and scene fades, with no input interception.
            healthRipple.raycastTarget = false;
            healthRipple.maskable = false;
            healthRipple.material = healthRippleMaterial;
            var rect = healthRipple.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(-36, -36);
            rect.offsetMax = new Vector2(36, 36);
        }
        if (healthRipple == null) return;
        healthRipple.enabled = hp.foreground.isActiveAndEnabled;
        var size = hp.foreground.rectTransform.rect.size;
        float fill = hp.foreground.fillAmount;
        healthRippleMaterial.SetVector(RippleRectId, new Vector4(size.x + 72, size.y + 72, 0, 0));
        // Follow the remaining red fill rather than illuminating the empty part of the gauge.
        healthRippleMaterial.SetVector(RippleBarId, new Vector4((fill - 1f) * size.x * .5f, 0, size.x * fill * .5f, size.y * .5f));
        healthRippleMaterial.SetFloat(RipplePhaseId, phase);
        healthRippleMaterial.SetFloat(RippleStrengthId, strength);
        rippleOwner = this;
        ripplePhase = phase;
        rippleStrength = strength;
    }

    private void ResetHealthPulse()
    {
        if (rippleOwner == this) rippleOwner = null;
        if (healthRipple != null) { healthRipple.gameObject.SetActive(false); Destroy(healthRipple.gameObject); }
        if (healthRippleMaterial != null) Destroy(healthRippleMaterial);
        healthRipple = null;
        healthRippleMaterial = null;
        if (healthPulseMaterial == null) return;
        if (hp.foreground != null && hp.foreground.material == healthPulseMaterial)
            hp.foreground.material = originalHealthMaterial;
        Destroy(healthPulseMaterial);
        healthPulseMaterial = null;
        heartbeatTime = 0;
    }

    private void Awake()
    {
        // Keep the authored HUD functional even if a prefab instance loses its optional hotbar component.
        if (GetComponent<InGameHotbarHUD>() == null) gameObject.AddComponent<InGameHotbarHUD>();
        if (GetComponent<InGameSkillHUD>() == null) gameObject.AddComponent<InGameSkillHUD>();
    }

    private void LateUpdate()
    {
        if (playerHealth == null && Time.unscaledTime >= nextSearch)
        {
            nextSearch = Time.unscaledTime + .25f;
            Transform target = PlayerTargetRegistry.Current;
            var health = target != null ? target.GetComponent<PlayerHealth>() : null;
            if (health == null) health = FindFirstObjectByType<PlayerHealth>();
            if (health != null) Bind(health);
        }
        if (playerHealth != null)
            hp.SetValue((float)playerHealth.CurrentHealth / Mathf.Max(1, playerHealth.MaxHealth));
        if (playerMana == null && playerHealth != null)
            playerMana = playerHealth.GetComponent<PlayerMana>() ?? playerHealth.gameObject.AddComponent<PlayerMana>();
        if (playerMana != null)
            mp.SetValue((float)playerMana.CurrentMana / Mathf.Max(1, playerMana.MaxMana));
        hp.Tick(Time.unscaledDeltaTime, trailDelay, trailDuration);
        mp.Tick(Time.unscaledDeltaTime, trailDelay, trailDuration);
        UpdateHealthPulse();
    }

    public void Bind(PlayerHealth health)
    {
        ResetHealthPulse();
        playerHealth = health;
        playerMana = health != null ? health.GetComponent<PlayerMana>() ?? health.gameObject.AddComponent<PlayerMana>() : null;
        GetComponent<InGameHotbarHUD>()?.Bind(health != null ? health.GetComponent<PlayerInventory>() : null);
        hp.Reset();
        mp.Reset();
    }

    private void OnDisable()
    {
        ResetHealthPulse();
        hp.Reset();
        mp.Reset();
    }
}
