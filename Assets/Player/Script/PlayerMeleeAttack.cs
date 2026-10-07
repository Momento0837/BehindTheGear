using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(PlayerInputReader), typeof(BoxCollider2D))]
public sealed class PlayerMeleeAttack : MonoBehaviour
{
    [SerializeField, Min(1)] private int attackDamage = 10;
    [SerializeField, Min(.01f)] private float attackCooldown = 1f;
    [SerializeField, Min(.01f)] private float attackRange = 1.3f;
    [SerializeField] private LayerMask monsterLayers;
    [SerializeField] private Sprite slashSprite;
    [SerializeField, Min(.01f)] private float slashDuration = .25f;

    private BoxCollider2D playerCollider;
    private readonly Collider2D[] hitBuffer = new Collider2D[16];
    private readonly HashSet<MonsterFollowController> hitMonsters = new();
    private ContactFilter2D monsterFilter;
    private float facingDirection = 1f;
    private float nextAttackTime;

    private void Awake()
    {
        playerCollider = GetComponent<BoxCollider2D>();
        monsterFilter.SetLayerMask(monsterLayers);
        monsterFilter.useTriggers = false;
        PlayerInputReader input = GetComponent<PlayerInputReader>();
        input.MoveChanged += UpdateFacing;
        input.PrimaryClickPressed += Attack;
        input.AttackCPressed += Attack;
    }

    public void Attack()
    {
        if (Time.time < nextAttackTime) return;
        nextAttackTime = Time.time + attackCooldown;

        Bounds bounds = playerCollider.bounds;
        Vector2 size = new(attackRange, bounds.size.y);
        Vector2 center = bounds.center + Vector3.right * facingDirection * (bounds.extents.x + attackRange * .5f);
        int hitCount = Physics2D.OverlapBox(center, size, 0f, monsterFilter, hitBuffer);
        hitMonsters.Clear();
        for (int i = 0; i < hitCount; i++)
        {
            MonsterFollowController monster = hitBuffer[i].GetComponentInParent<MonsterFollowController>();
            if (monster != null && hitMonsters.Add(monster)) monster.TakeDamage(attackDamage);
        }

        CreateSlashEffect(center);
    }

    private void UpdateFacing(float direction)
    {
        if (direction != 0f) facingDirection = Mathf.Sign(direction);
    }

    private void CreateSlashEffect(Vector2 position)
    {
        if (slashSprite == null) return;
        GameObject effect = new GameObject("Player Slash Effect", typeof(SpriteRenderer));
        effect.transform.position = position;
        float scale = attackRange / slashSprite.bounds.size.x;
        effect.transform.localScale = new Vector3(scale, scale, 1f);

        SpriteRenderer renderer = effect.GetComponent<SpriteRenderer>();
        renderer.sprite = slashSprite;
        renderer.sortingOrder = 2;
        renderer.flipX = facingDirection < 0f;
        Destroy(effect, slashDuration);
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
