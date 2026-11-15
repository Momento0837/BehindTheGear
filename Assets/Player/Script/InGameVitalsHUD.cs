using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Updates only image fill/opacity. Never writes to a RectTransform.</summary>
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
    private static readonly int TintId = Shader.PropertyToID("_Color");

    private void UpdateHealthPulse()
    {
        bool critical = playerHealth != null && !playerHealth.IsDead
            && (float)playerHealth.CurrentHealth / Mathf.Max(1, playerHealth.MaxHealth) <= .15f;
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
        Color tint = originalHealthTint * (1f + .45f * pulse);
        tint.a = originalHealthTint.a;
        healthPulseMaterial.SetColor(TintId, tint);
        // Masked UI may render a stencil copy of this private material.
        var renderedMaterial = hp.foreground.materialForRendering;
        if (renderedMaterial != healthPulseMaterial && renderedMaterial.HasProperty(TintId))
            renderedMaterial.SetColor(TintId, tint);
    }

    private void ResetHealthPulse()
    {
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
