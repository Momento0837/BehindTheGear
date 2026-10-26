using System.Collections.Generic;
using UnityEngine;

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

    private BoxCollider2D playerCollider;
    private readonly Collider2D[] hitBuffer = new Collider2D[16];
    private readonly HashSet<MonsterFollowController> hitMonsters = new();
    private ContactFilter2D monsterFilter;
    private float facingDirection = 1f;
    private float nextAttackTime;
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
        target.ReceiveMeleeDamage(ResolvedDamage);
        return true;
    }

    private void Awake()
    {
        playerCollider = GetComponent<BoxCollider2D>();
        monsterFilter.SetLayerMask(monsterLayers);
        monsterFilter.useTriggers = false;
        PlayerInputReader input = GetComponent<PlayerInputReader>();
        input.MoveChanged += UpdateFacing;
        input.BasicAttackPressed += Attack;
    }

    public void Attack()
    {
        if (Time.time < nextAttackTime) return;
        nextAttackTime = Time.time + attackCooldown;

        Vector2 size = new(attackRange, playerCollider.bounds.size.y);
        Vector2 center = GetAttackCenter();
        int hitCount = Physics2D.OverlapBox(center, size, 0f, monsterFilter, hitBuffer);
        hitMonsters.Clear();
        for (int i = 0; i < hitCount; i++)
        {
            MonsterFollowController monster = hitBuffer[i].GetComponentInParent<MonsterFollowController>();
            if (monster != null && hitMonsters.Add(monster)) monster.TakeDamage(ResolvedDamage);
            else if (monster == null)
            {
                foreach (MonoBehaviour component in hitBuffer[i].GetComponents<MonoBehaviour>())
                    if (component is IMeleeDamageReceiver receiver) { receiver.ReceiveMeleeDamage(ResolvedDamage); break; }
            }
        }
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
        renderer.sortingOrder = attackEffectSortingOrder;
        renderer.flipX = facingDirection < 0f;

        Animator effectAnimator = effect.GetComponent<Animator>();
        effectAnimator.runtimeAnimatorController = attackEffectController;
        effectAnimator.Play(AttackEffectStateHashes[index], 0, 0f);
        Destroy(effect, AttackEffectDurations[index]);
    }

    private void UpdateFacing(float direction)
    {
        if (direction != 0f) facingDirection = Mathf.Sign(direction);
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
