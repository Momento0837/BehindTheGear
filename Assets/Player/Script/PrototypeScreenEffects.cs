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
    public static Vector4 HealthEffect(float ratio) => ratio < .15f ? new Vector4(2, .18f, .15f, .20f)
        : ratio < .30f ? new Vector4(2, .11f, .05f, 0) : ratio < .50f ? new Vector4(1, .06f, 0, 0) : Vector4.zero;
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
        float lower = Mathf.Max(0f, offset - 2f);
        float upper = Mathf.Max(0f, offset + maximum - 2f);
        if (upper <= 0f) upper = 1f;
        return Mathf.Lerp(lower, upper, .5f - .5f * Mathf.Cos(seconds * Mathf.PI / 3f));
    }
}
