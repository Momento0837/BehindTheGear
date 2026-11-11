using UnityEngine;

/// <summary>One poison clock per receiver. Reapplication refreshes expiry, never adds a second clock.</summary>
[DisallowMultipleComponent]
public sealed class PoisonStatus2D : MonoBehaviour
{
    private PoisonDaggerTarget target;
    private float expiresAt, nextTickAt, interval;
    private int damage;
    public bool IsActive { get; private set; }
    public float Remaining => IsActive ? Mathf.Max(0, expiresAt - Time.time) : 0;
    public float NextTickAt => nextTickAt;
    public int TickDamage => damage;

    internal static void Apply(PoisonDaggerTarget receiver, int tickDamage, float duration, float tickInterval)
    {
        if (!receiver.IsAlive || tickDamage <= 0) return;
        var poison = receiver.Component.GetComponent<PoisonStatus2D>() ?? receiver.Component.gameObject.AddComponent<PoisonStatus2D>();
        poison.Advance(Time.time);
        if (!receiver.IsAlive) return;
        if (!poison.IsActive)
        {
            poison.interval = Mathf.Max(.01f, tickInterval);
            poison.nextTickAt = Time.time + poison.interval;
        }
        poison.target = receiver;
        poison.damage = tickDamage; // Latest shot's snapshot replaces the old amount; it never adds to it.
        poison.expiresAt = Time.time + Mathf.Max(.01f, duration);
        poison.IsActive = true; poison.enabled = true;
    }
    private void Update() => Advance(Time.time);
    private void Advance(float now)
    {
        if (!IsActive) return;
        if (!target.IsAlive) { Stop(); return; }
        while (nextTickAt <= now + .00001f && nextTickAt <= expiresAt + .00001f)
        {
            target.Damage(damage, false, Vector2.zero);
            nextTickAt += interval;
            if (!target.IsAlive) { Stop(); return; }
        }
        if (now >= expiresAt) Stop();
    }
    private void Stop() { IsActive = false; enabled = false; }
    private void OnDisable() => IsActive = false;
}
