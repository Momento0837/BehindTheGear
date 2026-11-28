using UnityEngine;

public static class PrototypeScreenEffects
{
    public static float OverridePixels = -1f;
    public const float DamageFlashDuration = .35f;
    private static float damageFlashStarted = float.NegativeInfinity;
    public static void TriggerDamageFlash() => damageFlashStarted = Time.unscaledTime;
    public static float DamageFlashOpacity => PlayerTargetRegistry.Current == null ? 0f
        : .1f * Mathf.Clamp01(1f - (Time.unscaledTime - damageFlashStarted) / DamageFlashDuration);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        OverridePixels = -1;
        damageFlashStarted = float.NegativeInfinity;
    }
    // Separation offset, red overlay, black opacity, monochrome blend.
    public static Vector4 HealthEffect(float ratio) => ratio < .15f ? new Vector4(2, .28f, .15f, .20f)
        : ratio < .30f ? new Vector4(2, .21f, .05f, 0) : ratio < .50f ? new Vector4(1, .16f, 0, 0) : Vector4.zero;
    public static Vector4 CurrentHealthEffect
    {
        get
        {
            var target = PlayerTargetRegistry.Current;
            var health = target != null ? target.GetComponent<PlayerHealth>() : null;
            return health != null ? HealthEffect((float)health.CurrentHealth / Mathf.Max(1, health.MaxHealth)) : Vector4.zero;
        }
    }
    public static float Separation(float maximum, float seconds, float offset)
    {
        // Scripted tutorial bursts retain their authored timing and strength.
        if (OverridePixels >= 0) return OverridePixels;
        // Health tiers: >=50% 1-2px, <50% 2-4px, <30% 3-5px.
        float lower = offset >= 2f ? 3f : offset >= 1f ? 2f : 1f;
        float upper = offset >= 2f ? 5f : offset >= 1f ? 4f : 2f;
        float pulse = .5f - .5f * Mathf.Cos(seconds * Mathf.PI / 3f);
        // Keep the existing movement emphasis without exceeding the tier's upper limit.
        float movement = Mathf.InverseLerp(2f, 5f, maximum);
        return Mathf.Lerp(lower, upper, Mathf.Lerp(pulse, 1f, movement * .35f));
    }
}
