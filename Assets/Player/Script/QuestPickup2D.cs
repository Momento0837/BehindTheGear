using UnityEngine;

/// <summary>Collects the active quest object when the player touches it.</summary>
[RequireComponent(typeof(CircleCollider2D), typeof(SpriteRenderer))]
public sealed class QuestPickup2D : MonoBehaviour
{
    private QuestGiver2D questGiver;
    private bool collected;

    private void Awake()
    {
        GetComponent<CircleCollider2D>().isTrigger = true;
        GetComponent<SpriteRenderer>().sortingOrder = 10;
    }

    public void Bind(QuestGiver2D owner) => questGiver = owner;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (collected || questGiver == null || other.GetComponentInParent<PlayerMovement2D>() == null)
            return;

        collected = true;
        questGiver.NotifyQuestItemCollected(this);
        Destroy(gameObject);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, .72f, .15f, .8f);
        Gizmos.DrawWireSphere(transform.position, .4f);
    }
}
