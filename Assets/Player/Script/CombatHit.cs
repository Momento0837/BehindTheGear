using UnityEngine;

/// <summary>Final combat result, before HP clamping. Critical must come from the attack resolver.</summary>
public readonly struct CombatHit
{
    public readonly int Damage;
    public readonly bool IsCritical;
    public CombatHit(int damage, bool isCritical = false)
    {
        Damage = Mathf.Max(0, damage);
        IsCritical = isCritical;
    }
}

public interface ICombatHitReceiver : IMeleeDamageReceiver
{
    void ReceiveHit(CombatHit hit);
}
