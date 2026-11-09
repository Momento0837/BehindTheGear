using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Q/C stealth. The former shockwave is retained for later use, without an input binding.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerInputReader), typeof(PlayerHealth), typeof(PlayerMana))]
public sealed class PlayerSkills : MonoBehaviour
{
    [Header("Q - Stealth")]
    [SerializeField, Min(.1f)] private float stealthDuration = 2.5f;
    [SerializeField, Min(0)] private int stealthAttackBonus = 10;
    [SerializeField, Min(.1f)] private float stealthCooldown = 12f;
    [SerializeField, Min(1)] private int stealthManaCost = 15;
    [SerializeField, Range(.05f, 1f)] private float stealthOpacity = .28f;
    [Header("Archived - Shockwave (not bound to E/V)")]
    [SerializeField, Min(.1f)] private float shockwaveCooldown = 8f;
    [SerializeField, Min(1)] private int shockwaveManaCost = 25;
    [SerializeField, Min(.1f)] private float shockwaveRadius = 3f;
    [SerializeField, Min(.1f)] private float shockwaveDamageMultiplier = 1.5f;
    [SerializeField, Min(0f)] private float shockwavePushSpeed = 8f;
    [SerializeField, Min(0f)] private float shockwavePushDuration = .25f;
    [SerializeField, Min(0f)] private float shockwaveStaggerDuration = .6f;

    private PlayerInputReader input;
    private PlayerHealth health;
    private PlayerMana mana;
    private PlayerControlLock controlLock;
    private Rigidbody2D body;
    private PlayerMovement2D movement;
    private PlayerMeleeAttack melee;
    private float stealthEndsAt, stealthReadyAt, shockwaveReadyAt;
    private bool stealthActive;
    private SpriteRenderer[] cloakedRenderers;
    private float[] originalAlphas;
    private readonly List<Collider2D> overlaps = new();
    private readonly HashSet<MonsterFollowController> hitMonsters = new();
    private readonly HashSet<IMeleeDamageReceiver> hitReceivers = new();

    public bool IsStealthed => isActiveAndEnabled && stealthActive && StealthRemaining > 0f;
    public float StealthRemaining => Mathf.Max(0f, stealthEndsAt - Time.time);
    public float StealthCooldownRemaining => Mathf.Max(0f, stealthReadyAt - Time.time);
    public float ShockwaveCooldownRemaining => Mathf.Max(0f, shockwaveReadyAt - Time.time);
    public float StealthCooldown => stealthCooldown;
    public float ShockwaveCooldown => shockwaveCooldown;
    public int StealthManaCost => stealthManaCost;
    public int StealthAttackBonus => stealthAttackBonus;
    public int ShockwaveManaCost => shockwaveManaCost;
    public int ShockwaveDamage => Mathf.Max(1, Mathf.RoundToInt((melee != null ? melee.ResolvedDamage : 10) * shockwaveDamageMultiplier));
    public bool CanUseSkills => isActiveAndEnabled && input != null && input.isActiveAndEnabled
        && health != null && !health.IsDead && controlLock != null && !controlLock.IsLocked
        && body != null && body.simulated && (movement == null || movement.isActiveAndEnabled)
        && !InventoryUI.GameplayInputSuppressedThisFrame && !SceneFadeTransition.IsRunning && Time.timeScale > 0f;

    public static bool IsHiddenFromMonsters(PlayerHealth target) =>
        target != null && target.TryGetComponent<PlayerSkills>(out var skills) && skills.IsStealthed;

    private void Awake()
    {
        input = GetComponent<PlayerInputReader>();
        health = GetComponent<PlayerHealth>();
        mana = GetComponent<PlayerMana>();
        controlLock = GetComponent<PlayerControlLock>();
        body = GetComponent<Rigidbody2D>();
        movement = GetComponent<PlayerMovement2D>();
        melee = GetComponent<PlayerMeleeAttack>();
    }

    private void OnEnable()
    {
        input.SecondaryAttackPressed += BreakStealth;
        health.Damaged += BreakStealth;
        health.Died += BreakStealth;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        BreakStealth();
        if (input != null)
        {
            input.SecondaryAttackPressed -= BreakStealth;
        }
        if (health != null)
        {
            health.Damaged -= BreakStealth;
            health.Died -= BreakStealth;
        }
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => BreakStealth();

    private void Update()
    {
        if (stealthActive && (StealthRemaining <= 0f || !CanUseSkills)) BreakStealth();
    }

    private void LateUpdate()
    {
        if (!IsStealthed || cloakedRenderers == null) return;
        for (int i = 0; i < cloakedRenderers.Length; i++)
        {
            if (cloakedRenderers[i] == null) continue;
            Color color = cloakedRenderers[i].color;
            color.a = originalAlphas[i] * stealthOpacity;
            cloakedRenderers[i].color = color;
        }
    }

    public bool TryUseStealth()
    {
        if (!CanUseSkills || IsStealthed || StealthCooldownRemaining > 0f
            || movement != null && movement.IsAttacking || !mana.TrySpend(stealthManaCost)) return false;
        stealthActive = true;
        stealthEndsAt = Time.time + stealthDuration;
        stealthReadyAt = Time.time + stealthCooldown;
        cloakedRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        originalAlphas = new float[cloakedRenderers.Length];
        for (int i = 0; i < cloakedRenderers.Length; i++) originalAlphas[i] = cloakedRenderers[i].color.a;
        LateUpdate();
        SpawnPulse(1.2f, new Color(.64f, .49f, .95f, .8f));
        return true;
    }

    public void BreakStealth()
    {
        if (!stealthActive) return;
        stealthActive = false;
        stealthEndsAt = 0f;
        if (cloakedRenderers != null)
            for (int i = 0; i < cloakedRenderers.Length; i++)
            {
                if (cloakedRenderers[i] == null) continue;
                Color color = cloakedRenderers[i].color;
                color.a = originalAlphas[i];
                cloakedRenderers[i].color = color;
            }
        cloakedRenderers = null;
        originalAlphas = null;
    }

    // Called when a real swing is accepted, before visibility is restored.
    public int ConsumeStealthAttackBonus()
    {
        int bonus = IsStealthed ? stealthAttackBonus : 0;
        BreakStealth();
        return bonus;
    }

    public bool TryUseShockwave()
    {
        if (!CanUseSkills || ShockwaveCooldownRemaining > 0f || !mana.TrySpend(shockwaveManaCost)) return false;
        BreakStealth();
        shockwaveReadyAt = Time.time + shockwaveCooldown;
        var collider = GetComponent<Collider2D>();
        Vector2 center = collider != null ? collider.bounds.center : transform.position;
        var filter = new ContactFilter2D { useTriggers = false };
        Physics2D.OverlapCircle(center, shockwaveRadius, filter, overlaps);
        hitMonsters.Clear();
        hitReceivers.Clear();
        int damage = ShockwaveDamage;
        foreach (var hit in overlaps)
        {
            var monster = hit.GetComponentInParent<MonsterFollowController>();
            if (monster == null)
            {
                foreach (var component in hit.GetComponentsInParent<MonoBehaviour>())
                    if (component is IMeleeDamageReceiver receiver)
                    {
                        if (hitReceivers.Add(receiver))
                        {
                            if (receiver is ICombatHitReceiver detailed) detailed.ReceiveHit(new CombatHit(damage));
                            else receiver.ReceiveMeleeDamage(damage);
                        }
                        break;
                    }
                continue;
            }
            if (monster == null || !monster.isActiveAndEnabled || monster.CurrentHealth <= 0 || !hitMonsters.Add(monster)) continue;
            float direction = monster.transform.position.x < center.x ? -1f : 1f;
            monster.TakeShockwaveDamage(damage, direction, shockwavePushSpeed, shockwavePushDuration, shockwaveStaggerDuration);
        }
        SpawnPulse(shockwaveRadius, new Color(.35f, .87f, 1f, .9f));
        return true;
    }

    private void SpawnPulse(float radius, Color color)
    {
        var source = GetComponent<SpriteRenderer>();
        PlayerSkillPulse.Create(transform.position, radius, color, source);
    }
}
