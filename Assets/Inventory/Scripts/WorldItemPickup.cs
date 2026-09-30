using UnityEngine;

[RequireComponent(typeof(CircleCollider2D), typeof(SpriteRenderer))]
public sealed class WorldItemPickup : MonoBehaviour
{
    [SerializeField] private InventoryItemDefinition item;
    [SerializeField, Min(1)] private int amount = 1;
    private bool collecting;

    private void Reset() => GetComponent<CircleCollider2D>().isTrigger = true;

    private void Awake()
    {
        GetComponent<CircleCollider2D>().isTrigger = true;
        if (item == null) return;
        SpriteRenderer visual = GetComponent<SpriteRenderer>();
        visual.sprite = item.Icon;
        visual.sortingOrder = 10;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!isActiveAndEnabled || collecting || item == null || amount <= 0) return;
        PlayerInventory inventory = other.GetComponentInParent<PlayerInventory>();
        if (inventory == null) return;
        collecting = true;
        amount -= inventory.Add(item, amount);
        if (amount <= 0) Destroy(gameObject);
        else collecting = false;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, .76f, .25f, .65f);
        Gizmos.DrawWireSphere(transform.position, .3f);
    }
}
