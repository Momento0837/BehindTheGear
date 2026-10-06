using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public sealed class MonsterFollowController : MonoBehaviour
{
    [Header("Stats")]
    [SerializeField, Min(1)] private int maxHealth = 10;
    [SerializeField, Min(0f)] private float moveSpeed = 3f;
    [SerializeField, Min(1)] private int attackPower = 10;
    [SerializeField, Min(.01f)] private float attackDelay = .5f;

    [Header("Ranges")]
    [SerializeField] private BoxCollider2D recognitionRange;
    [SerializeField] private BoxCollider2D attackRange;
    [SerializeField] private Transform player;
    [SerializeField] private LayerMask sightBlockingLayers;

    [Header("Drops")]
    [SerializeField] private GameObject[] dropPrefabs;

    private Rigidbody2D body;
    private PlayerHealth playerHealth;
    private bool hasRecognizedPlayer;
    private float nextAttackTime;

    public int CurrentHealth { get; private set; }

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        body.freezeRotation = true;
        CurrentHealth = maxHealth;
        playerHealth = player != null ? player.GetComponent<PlayerHealth>() : null;
    }

    private void FixedUpdate()
    {
        if (!hasRecognizedPlayer && IsPlayerInsideRecognitionRange())
        {
            hasRecognizedPlayer = true;
        }

        if (!hasRecognizedPlayer || player == null || CurrentHealth <= 0)
        {
            Stop();
            return;
        }

        if (IsSightBlocked())
        {
            Stop();
            return;
        }

        if (IsPlayerInsideAttackRange())
        {
            Stop();
            Attack();
            return;
        }

        MoveTowardsCurrentPlayerPosition();
    }

    private bool IsPlayerInsideRecognitionRange()
    {
        return recognitionRange != null && player != null && recognitionRange.bounds.Contains(player.position);
    }

    private bool IsPlayerInsideAttackRange()
    {
        return attackRange != null && player != null && attackRange.bounds.Contains(player.position);
    }

    private bool IsSightBlocked()
    {
        Vector2 origin = body.position;
        Vector2 target = player.position;
        Vector2 direction = target - origin;
        if (direction.sqrMagnitude <= Mathf.Epsilon) return false;
        return Physics2D.Raycast(origin, direction.normalized, direction.magnitude, sightBlockingLayers).collider != null;
    }

    private void Attack()
    {
        if (Time.time < nextAttackTime) return;
        if (playerHealth == null && player != null) playerHealth = player.GetComponent<PlayerHealth>();
        if (playerHealth == null || playerHealth.IsDead) return;

        int damage = Mathf.Max(1, attackPower - playerHealth.Defense);
        playerHealth.TakeDamage(attackPower);
        Debug.Log($"[Monster] {name} attacked {player.name}: {damage} damage", this);
        nextAttackTime = Time.time + attackDelay;
    }

    public void TakeDamage(int damage)
    {
        if (damage <= 0 || CurrentHealth <= 0) return;

        CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
        Debug.Log($"[Monster] {name} HP: {CurrentHealth} / {maxHealth}", this);
        if (CurrentHealth == 0) Die();
    }

    private void Die()
    {
        Stop();
        CreateDrops();
        gameObject.SetActive(false);
    }

    private void CreateDrops()
    {
        bool createdDrop = false;
        if (dropPrefabs != null)
        {
            foreach (GameObject dropPrefab in dropPrefabs)
            {
                if (dropPrefab == null) continue;
                Instantiate(dropPrefab, transform.position + Vector3.up * .5f, Quaternion.identity);
                createdDrop = true;
            }
        }

        if (!createdDrop) CreateTemporaryDrop();
    }

    private void CreateTemporaryDrop()
    {
        GameObject drop = GameObject.CreatePrimitive(PrimitiveType.Quad);
        drop.name = "Monster Drop";
        drop.transform.position = transform.position + Vector3.up * .5f;
        drop.transform.localScale = Vector3.one * .35f;
        drop.GetComponent<Renderer>().material.color = Color.yellow;
    }

    private void MoveTowardsCurrentPlayerPosition()
    {
        float direction = Mathf.Sign(player.position.x - body.position.x);
        body.linearVelocity = new Vector2(direction * moveSpeed, body.linearVelocity.y);
    }

    private void Stop()
    {
        if (body != null) body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
    }

    private void OnDrawGizmos()
    {
        if (attackRange == null) return;

        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(attackRange.bounds.center, attackRange.bounds.size);
    }
}
