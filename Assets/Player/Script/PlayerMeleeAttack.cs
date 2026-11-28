using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public interface IMeleeDamageModifier { int MeleeDamageBonus { get; } }
public interface IMeleeDamageReceiver { void ReceiveMeleeDamage(int damage); }

[RequireComponent(typeof(PlayerInputReader), typeof(BoxCollider2D))]
public sealed class PlayerMeleeAttack : MonoBehaviour
{
    [SerializeField, Min(1)] private int attackDamage = 10;
    [SerializeField, Min(.01f)] private float attackCooldown = 1f;
    [SerializeField, Min(.01f)] private float attackRange = 1.3f;
    [SerializeField] private LayerMask monsterLayers;
    [SerializeField] private RuntimeAnimatorController attackEffectController;
    [SerializeField] private Sprite attackEffectReferenceSprite;
    [SerializeField] private int attackEffectSortingOrder = 2;
    [SerializeField, Min(0f)] private float attackEffectBackwardOffset = .2f;

    [Header("Sound")]
    [Tooltip("콤보 1/2/3단 휘두르기 소리. 비어 있으면 Resources/Audio/SFX/Player_Swing_01~03을 사용합니다.")]
    [SerializeField] private AudioClip[] swingClips = new AudioClip[3];
    [Tooltip("몬스터 피격 소리. 비어 있으면 Resources/Audio/SFX/Monster_Hit을 사용합니다.")]
    [SerializeField] private AudioClip monsterHitClip;
    // Hit is intentionally louder than swings so impacts read clearly over the whoosh.
    [SerializeField, Range(0f, 1f)] private float swingVolume = .6f;
    [SerializeField, Range(0f, 1f)] private float hitVolume = 1f;
    [SerializeField, Range(0f, .5f)] private float pitchJitter = .05f;

    private const string SwingClipResourcePrefix = "Audio/SFX/Player_Swing_0";
    private const string MonsterHitClipResourcePath = "Audio/SFX/Monster_Hit";
    private static bool missingSfxWarned;
    private AudioSource sfxSource;

    private BoxCollider2D playerCollider;
    private readonly Collider2D[] hitBuffer = new Collider2D[16];
    private readonly HashSet<MonsterFollowController> hitMonsters = new();
    private readonly HashSet<IMeleeDamageReceiver> hitReceivers = new();
    private ContactFilter2D monsterFilter;
    private float facingDirection = 1f;
    private float nextAttackTime;
    private bool comboHitPending;
    private bool comboSoundPending;
    private int swingStealthBonus;
    private PlayerMovement2D movement;
    private static readonly int[] AttackEffectStateHashes =
    {
        Animator.StringToHash("Base Layer.Attack01"),
        Animator.StringToHash("Base Layer.Attack02"),
        Animator.StringToHash("Base Layer.Attack03")
    };
    private static readonly float[] AttackEffectDurations = { .5f, .5f, .62f };
    public int ResolvedDamage
    {
        get
        {
            int result = attackDamage;
            foreach (MonoBehaviour component in GetComponents<MonoBehaviour>())
                if (component is IMeleeDamageModifier modifier) result += modifier.MeleeDamageBonus;
            return Mathf.Max(1, result);
        }
    }

    // Targeted training uses exactly the same damage and cooldown as normal melee attacks.
    public bool TryAttackTarget(IMeleeDamageReceiver target)
    {
        if (target == null || Time.time < nextAttackTime) return false;
        nextAttackTime = Time.time + attackCooldown;
        // UI-triggered training hit: no animation/ResolveHit runs, so these sounds play exactly once here.
        PlaySwingSound(1);
        target.ReceiveMeleeDamage(ResolveSwingDamage());
        PlaySfx(monsterHitClip, hitVolume);
        return true;
    }

    private void Awake()
    {
        playerCollider = GetComponent<BoxCollider2D>();
        movement = GetComponent<PlayerMovement2D>();
        SetupSound();
        monsterFilter.SetLayerMask(monsterLayers);
        monsterFilter.useTriggers = false;
        PlayerInputReader input = GetComponent<PlayerInputReader>();
        input.MoveChanged += UpdateFacing;
        // Animated players resolve damage from their actual combo state, not the button press.
        if (movement == null || GetComponent<Animator>() == null) input.BasicAttackPressed += Attack;
    }

    public void Attack()
    {
        if (Time.time < nextAttackTime) return;
        nextAttackTime = Time.time + attackCooldown;

        AimSwingAtMouse();
        PlaySwingSound(1);
        ResolveHit(facingDirection);
    }

    public void BeginComboSwing()
    {
        AimSwingAtMouse();
        comboHitPending = true;
        comboSoundPending = true;
        swingStealthBonus = GetComponent<PlayerSkills>()?.ConsumeStealthAttackBonus() ?? 0;
    }

    public void CancelComboSwing() { comboHitPending = false; comboSoundPending = false; swingStealthBonus = 0; }

    // Body Attack / Run_Attack clips call this at frame 14, independently of damage resolution.
    public void OnAttackSound(int attackIndex)
    {
        if (!isActiveAndEnabled || !comboSoundPending || movement == null || !movement.IsAttacking
            || attackIndex < 1 || attackIndex > 3) return;
        comboSoundPending = false;
        GameSoundService.PlayPlayerAttack(attackIndex);
    }

    public void ResolveComboHit(int attackIndex)
    {
        if (!comboHitPending) return;
        comboHitPending = false;
        PlayComboEffect(attackIndex);
        // Animated swings already sound at frame 14 through OnAttackSound.
        ResolveHit(facingDirection);
    }

    private void ResolveHit(float direction)
    {
        int damage = ResolveSwingDamage();
        Vector2 size = new(attackRange, playerCollider.bounds.size.y);
        Vector2 center = playerCollider.bounds.center + Vector3.right * direction
            * (playerCollider.bounds.extents.x + attackRange * .5f);
        int hitCount = Physics2D.OverlapBox(center, size, 0f, monsterFilter, hitBuffer);
        hitMonsters.Clear();
        hitReceivers.Clear();
        bool damagedMonster = false;
        for (int i = 0; i < hitCount; i++)
        {
            MonsterFollowController monster = hitBuffer[i].GetComponentInParent<MonsterFollowController>();
            if (monster != null && monster.CurrentHealth > 0 && hitMonsters.Add(monster))
            {
                int healthBefore = monster.CurrentHealth;
                monster.TakeMeleeDamage(damage, direction);
                damagedMonster |= monster.CurrentHealth < healthBefore;
                movement?.ApplyHitStop(.05f);
            }
            else if (monster == null)
            {
                foreach (MonoBehaviour component in hitBuffer[i].GetComponentsInParent<MonoBehaviour>())
                    if (component is IMeleeDamageReceiver receiver)
                    {
                        if (hitReceivers.Add(receiver))
                        {
                            // This resolver currently has no critical roll. Preserve that combat policy.
                            if (receiver is ICombatHitReceiver detailed) detailed.ReceiveHit(new CombatHit(damage));
                            else receiver.ReceiveMeleeDamage(damage);
                        }
                        break;
                    }
            }
        }
        // Keep one impact cue per swing: the shared combat sound wins over legacy clips.
        if (damagedMonster) GameSoundService.PlayPunchHit();
        else if (hitReceivers.Count > 0) PlaySfx(monsterHitClip, hitVolume);
    }

    private void SetupSound()
    {
        if (swingClips == null || swingClips.Length < 3) System.Array.Resize(ref swingClips, 3);
        bool missing = false;
        for (int i = 0; i < 3; i++)
        {
            if (swingClips[i] != null) continue;
            swingClips[i] = Resources.Load<AudioClip>(SwingClipResourcePrefix + (i + 1));
            missing |= swingClips[i] == null;
        }
        if (monsterHitClip == null)
        {
            monsterHitClip = Resources.Load<AudioClip>(MonsterHitClipResourcePath);
            missing |= monsterHitClip == null;
        }
        if (missing && !missingSfxWarned)
        {
            missingSfxWarned = true;
            Debug.LogWarning("[PlayerMeleeAttack] 일부 공격 효과음이 없어 해당 소리는 생략됩니다. "
                + "Inspector의 Sound 항목 또는 Resources/Audio/SFX/Player_Swing_01~03, Monster_Hit을 확인하세요.", this);
        }

        sfxSource = GetComponent<AudioSource>();
        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;
        }
    }

    private void PlaySwingSound(int attackIndex)
    {
        int index = attackIndex - 1;
        if (swingClips != null && index >= 0 && index < swingClips.Length)
            PlaySfx(swingClips[index], swingVolume);
    }

    private void PlaySfx(AudioClip clip, float volume)
    {
        if (clip == null || sfxSource == null) return;
        sfxSource.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        sfxSource.PlayOneShot(clip, volume);
    }

    private int ResolveSwingDamage()
    {
        int bonus = swingStealthBonus + (GetComponent<PlayerSkills>()?.ConsumeStealthAttackBonus() ?? 0);
        swingStealthBonus = 0;
        return ResolvedDamage + bonus;
    }

    public void PlayComboEffect(int attackIndex)
    {
        int index = attackIndex - 1;
        if (index < 0 || index >= AttackEffectStateHashes.Length
            || attackEffectController == null || attackEffectReferenceSprite == null)
            return;

        GameObject effect = new("Player Attack Effect", typeof(SpriteRenderer), typeof(Animator));
        effect.transform.SetParent(transform, true);
        effect.transform.position = GetAttackCenter()
            - Vector2.right * facingDirection * attackEffectBackwardOffset;
        float scale = attackRange / Mathf.Max(.01f, attackEffectReferenceSprite.bounds.size.x);
        effect.transform.localScale = new Vector3(scale, scale, 1f);

        SpriteRenderer renderer = effect.GetComponent<SpriteRenderer>();
        renderer.sprite = attackEffectReferenceSprite;
        renderer.sortingLayerName = "ForeGround";
        renderer.sortingOrder = Mathf.Max(1000, attackEffectSortingOrder);
        renderer.color = new Color(1, 1, 1, .9f);
        renderer.flipX = facingDirection < 0f;

        Animator effectAnimator = effect.GetComponent<Animator>();
        effectAnimator.runtimeAnimatorController = attackEffectController;
        effectAnimator.Play(AttackEffectStateHashes[index], 0, 0f);
        Destroy(effect, AttackEffectDurations[index]);
    }

    private void UpdateFacing(float direction)
    {
        if (comboHitPending || movement != null && movement.IsAttacking) return;
        if (direction != 0f) facingDirection = Mathf.Sign(direction);
    }

    private void AimSwingAtMouse()
    {
        var camera = Camera.main;
        if (Mouse.current != null && camera != null)
        {
            float offset = Mouse.current.position.ReadValue().x - camera.WorldToScreenPoint(transform.position).x;
            if (Mathf.Abs(offset) > .01f) facingDirection = Mathf.Sign(offset);
        }
        GetComponent<PlayerSpriteFacing>()?.FaceAttack(facingDirection);
    }

    private Vector2 GetAttackCenter()
    {
        Bounds bounds = playerCollider.bounds;
        return bounds.center + Vector3.right * facingDirection * (bounds.extents.x + attackRange * .5f);
    }

    private void OnDrawGizmos()
    {
        BoxCollider2D box = GetComponent<BoxCollider2D>();
        if (box == null) return;

        Bounds bounds = box.bounds;
        Vector2 size = new(attackRange, bounds.size.y);
        Vector2 center = bounds.center + Vector3.right * facingDirection * (bounds.extents.x + attackRange * .5f);

        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(center, size);
    }
}
