using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(PlayerInputReader), typeof(PlayerSkills))]
public sealed class PlayerPoisonDaggerSkill : MonoBehaviour
{
    [SerializeField] private PoisonDaggerSettings settings;
    private PlayerSkills skills;
    private PlayerMeleeAttack melee;
    private PlayerSpriteFacing facing;
    private PlayerInputReader input;
    private Collider2D ownerCollider;
    private float readyAt;
    public PoisonDaggerSettings Settings
    {
        get { if (settings == null) settings = Resources.Load<PoisonDaggerSettings>(PoisonDaggerSettings.ResourcePath); return settings; }
        set => settings = value;
    }
    public float Cooldown => Settings != null ? Settings.cooldown : 30;
    public float CooldownRemaining => Mathf.Max(0, readyAt - Time.unscaledTime);
    public int ManaCost => Mathf.CeilToInt((GetComponent<PlayerMana>()?.MaxMana ?? 100) * .1f);
    public bool CanUse => isActiveAndEnabled && Settings != null && skills != null && skills.CanUseSkills;
    private void Awake()
    {
        if (settings == null) settings = Resources.Load<PoisonDaggerSettings>(PoisonDaggerSettings.ResourcePath);
        skills = GetComponent<PlayerSkills>(); melee = GetComponent<PlayerMeleeAttack>();
        facing = GetComponent<PlayerSpriteFacing>(); input = GetComponent<PlayerInputReader>(); ownerCollider = GetComponent<Collider2D>();
    }
    public bool TryThrow()
    {
        if (!CanUse || CooldownRemaining > 0 || settings.daggerSprite == null) return false;
        if (GetComponent<PlayerMana>() is not PlayerMana mana || !mana.TrySpend(ManaCost)) return false;
        Vector2 direction = facing != null ? facing.AimDirection : input.AimDirection;
        if (direction.sqrMagnitude < .001f) direction = Vector2.right;
        direction.Normalize();
        Vector2 center = ownerCollider != null ? ownerCollider.bounds.center : transform.position;
        Vector2 origin = center + direction * settings.spawnOffset;
        int attack = melee != null ? melee.ResolvedDamage : 10;
        int direct = Snapshot(attack, settings.hitDamageMultiplier), poison = Snapshot(attack, settings.poisonDamageMultiplier);
        readyAt = Time.unscaledTime + Mathf.Max(0, settings.cooldown);
        skills.BreakStealth();
        PlayerPoisonDaggerProjectile.Launch(origin + direction * settings.centerLeadDistance, direction, gameObject, settings, direct, poison);
        return true;
    }
    private static int Snapshot(int attack, float multiplier) => multiplier <= 0 ? 0 : Mathf.Max(1, Mathf.RoundToInt(attack * multiplier));
}
