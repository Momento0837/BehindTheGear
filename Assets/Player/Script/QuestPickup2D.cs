using UnityEngine;

/// <summary>Collects the active quest object when the player touches it.</summary>
[RequireComponent(typeof(CircleCollider2D), typeof(SpriteRenderer))]
public sealed class QuestPickup2D : MonoBehaviour
{
    [SerializeField] private InventoryItemDefinition questItem;
    [SerializeField, Min(1)] private int amount = 1;

    private QuestGiver2D questGiver;
    private bool collected;

    private void Awake()
    {
        GetComponent<CircleCollider2D>().isTrigger = true;
        SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.sortingLayerName = "ForeGround";
        spriteRenderer.sortingOrder = 20;
        if (questItem != null) spriteRenderer.sprite = questItem.Icon;
    }

    public void Bind(QuestGiver2D owner) => questGiver = owner;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (collected || questGiver == null || other.GetComponentInParent<PlayerMovement2D>() == null)
            return;

        PlayerInventory inventory = other.GetComponentInParent<PlayerInventory>();
        if (questItem != null)
        {
            int requiredAmount = Mathf.Max(1, amount);
            if (inventory == null || inventory.Add(questItem, requiredAmount) < requiredAmount) return;
        }

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
