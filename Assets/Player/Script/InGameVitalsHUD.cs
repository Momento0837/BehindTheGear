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

    private void Awake()
    {
        // Keep the authored HUD functional even if a prefab instance loses its optional hotbar component.
        if (GetComponent<InGameHotbarHUD>() == null) gameObject.AddComponent<InGameHotbarHUD>();
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
    }

    public void Bind(PlayerHealth health)
    {
        playerHealth = health;
        playerMana = health != null ? health.GetComponent<PlayerMana>() ?? health.gameObject.AddComponent<PlayerMana>() : null;
        GetComponent<InGameHotbarHUD>()?.Bind(health != null ? health.GetComponent<PlayerInventory>() : null);
        hp.Reset();
        mp.Reset();
    }

    private void OnDisable()
    {
        hp.Reset();
        mp.Reset();
    }
}
