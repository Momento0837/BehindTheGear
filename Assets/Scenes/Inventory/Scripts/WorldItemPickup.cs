using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CircleCollider2D), typeof(SpriteRenderer))]
public sealed class WorldItemPickup : MonoBehaviour
{
    [SerializeField] private InventoryItemDefinition item;
    [SerializeField, Min(1)] private int amount = 1;
    [SerializeField, Min(.1f)] private float magnetRadius = 3f;
    [SerializeField, Min(.05f)] private float collectDistance = .35f;
    [SerializeField, Min(.1f)] private float magnetMoveSpeed = 7f;
    [SerializeField, Min(.1f)] private float questInteractRadius = 1.25f;
    [SerializeField] private GameObject questHighlightPrefab;

    private PlayerInventory target;
    private GameObject runtimeHighlight;
    private float nextFullNoticeTime;
    private bool collecting;

    private bool IsQuestPickup => item != null && item.IsQuestItem;

    private void Reset()
    {
        GetComponent<CircleCollider2D>().isTrigger = true;
    }

    private void Awake()
    {
        ConfigureCollider();
        RefreshVisual();
    }

    private void Update()
    {
        if (!isActiveAndEnabled || collecting || item == null || amount <= 0) return;

        if (IsQuestPickup)
        {
            UpdateQuestHighlight();
            if (target != null && Keyboard.current?.fKey.wasPressedThisFrame == true) TryCollect(target);
            return;
        }

        if (target == null) return;
        if (!target.CanAddAny(item))
        {
            NotifyFull(target);
            return;
        }

        transform.position = Vector3.MoveTowards(transform.position, target.transform.position, magnetMoveSpeed * Time.deltaTime);
        if (Vector2.Distance(transform.position, target.transform.position) <= collectDistance)
            TryCollect(target);
    }

    public void Configure(InventoryItemDefinition definition, int count)
    {
        item = definition;
        amount = Mathf.Max(1, count);
        ConfigureCollider();
        RefreshVisual();
    }

    private void ConfigureCollider()
    {
        CircleCollider2D trigger = GetComponent<CircleCollider2D>();
        trigger.isTrigger = true;
        trigger.radius = IsQuestPickup ? questInteractRadius : magnetRadius;
    }

    private void RefreshVisual()
    {
        if (item == null) return;
        SpriteRenderer visual = GetComponent<SpriteRenderer>();
        visual.sprite = item.Icon;
        visual.sortingOrder = 10;
        EnsureQuestHighlight();
    }

    private void OnTriggerEnter2D(Collider2D other) => TrackTarget(other);
    private void OnTriggerStay2D(Collider2D other) => TrackTarget(other);

    private void OnTriggerExit2D(Collider2D other)
    {
        PlayerInventory inventory = other.GetComponentInParent<PlayerInventory>();
        if (inventory != null && inventory == target) target = null;
    }

    private void TrackTarget(Collider2D other)
    {
        if (collecting || item == null || amount <= 0) return;
        PlayerInventory inventory = other.GetComponentInParent<PlayerInventory>();
        if (inventory == null) return;
        target = inventory;
        if (!IsQuestPickup && !inventory.CanAddAny(item)) NotifyFull(inventory);
    }

    private void TryCollect(PlayerInventory inventory)
    {
        if (inventory == null || collecting) return;
        collecting = true;
        int accepted = inventory.Add(item, amount);
        amount -= accepted;
        if (amount <= 0)
        {
            Destroy(gameObject);
            return;
        }

        collecting = false;
        NotifyFull(inventory);
    }

    private void NotifyFull(PlayerInventory inventory)
    {
        if (Time.unscaledTime < nextFullNoticeTime) return;
        inventory.NotifyInventoryFull();
        nextFullNoticeTime = Time.unscaledTime + .75f;
    }

    private void EnsureQuestHighlight()
    {
        if (!IsQuestPickup || runtimeHighlight != null) return;
        if (questHighlightPrefab != null)
        {
            runtimeHighlight = Instantiate(questHighlightPrefab, transform);
            return;
        }

        GameObject glow = new("Quest Pickup Glow", typeof(SpriteRenderer));
        glow.transform.SetParent(transform, false);
        glow.transform.localScale = Vector3.one * 1.65f;
        SpriteRenderer renderer = glow.GetComponent<SpriteRenderer>();
        renderer.sprite = item.Icon;
        renderer.color = new Color(.65f, .9f, 1f, .32f);
        renderer.sortingOrder = 9;
        runtimeHighlight = glow;
    }

    private void UpdateQuestHighlight()
    {
        if (runtimeHighlight == null) return;
        float pulse = 1.55f + Mathf.Sin(Time.unscaledTime * 4f) * .18f;
        runtimeHighlight.transform.localScale = Vector3.one * pulse;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = IsQuestPickup ? new Color(.35f, .75f, 1f, .65f) : new Color(1f, .76f, .25f, .65f);
        Gizmos.DrawWireSphere(transform.position, IsQuestPickup ? questInteractRadius : magnetRadius);
    }
}
