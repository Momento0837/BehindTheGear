using UnityEngine;

/// <summary>Each sensor queries its own collider for recognition or attack range.</summary>
public sealed class MonsterRangeTrigger2D : MonoBehaviour
{
    private MonsterFollowController owner;
    private BoxCollider2D sensorCollider;
    private readonly Collider2D[] overlaps = new Collider2D[32];
    private ContactFilter2D playerFilter;
    public bool HasPlayer
    {
        get
        {
            if (owner == null || sensorCollider == null || !sensorCollider.enabled
                || !gameObject.activeInHierarchy) return false;
            // Query current geometry so cloning, sleeping bodies and re-enabling cannot lose detection.
            int count = sensorCollider.Overlap(playerFilter, overlaps);
            for (int i = 0; i < count; i++)
            {
                var health = overlaps[i].GetComponentInParent<PlayerHealth>();
                if (health == null || health.IsDead || PlayerSkills.IsHiddenFromMonsters(health)) continue;
                owner.RecognizePlayer(health);
                return true;
            }
            return false;
        }
    }

    public void Configure(MonsterFollowController value, bool attack)
    {
        owner = value;
        sensorCollider = GetComponent<BoxCollider2D>();
        playerFilter = new ContactFilter2D();
        playerFilter.useTriggers = false;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        var health = other.GetComponentInParent<PlayerHealth>();
        if (health == null || owner == null) return;
        owner.RecognizePlayer(health);
    }
}
