using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CircleCollider2D), typeof(SpriteRenderer), typeof(Rigidbody2D))]
public sealed class WorldItemPickup : MonoBehaviour
{
    [SerializeField] private InventoryItemDefinition item;
    [SerializeField, Min(1)] private int amount = 1;
    [SerializeField, Min(.1f)] private float magnetRadius = 3f;
    [SerializeField, Min(.05f)] private float collectDistance = .35f;
    [SerializeField, Min(.1f)] private float magnetMoveSpeed = 7f;
    [SerializeField, Min(.05f)] private float magnetAccelerationSeconds = .5f;
    [SerializeField, Min(.1f)] private float questInteractRadius = 1.25f;
    [SerializeField] private GameObject questHighlightPrefab;

    private const float MaximumMagnetMultiplier = 1.2f;
    private PlayerInventory target;
    private readonly HashSet<Collider2D> targetColliders = new();
    private GameObject runtimeHighlight;
    private SpriteRenderer smallDropVisual;
    private Rigidbody2D body;
    private BoxCollider2D physicalCollider;
    private float currentMagnetSpeed;
    private bool magnetActive;
    private float nextFullNoticeTime;
    private bool collecting;
    public InventoryItemDefinition Definition => item;
    public int Amount => amount;

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

    private void OnEnable() => RefreshVisual();

    private void OnDisable()
    {
        target = null;
        targetColliders.Clear();
    }

    private void Update()
    {
        if (SceneFadeTransition.IsRunning || !isActiveAndEnabled || collecting || item == null || amount <= 0) return;

        if (IsQuestPickup)
        {
            UpdateQuestHighlight();
            if (target != null && Keyboard.current?.fKey.wasPressedThisFrame == true) TryCollect(target);
            return;
        }

        if (target != null && !target.CanAddAny(item)) NotifyFull(target);
    }

    private void FixedUpdate()
    {
        bool canAttract = !SceneFadeTransition.IsRunning && !collecting && item != null && amount > 0
            && !IsQuestPickup && target != null && target.CanAddAny(item);
        SetMagnetState(canAttract);
        if (!canAttract || body == null) return;

        float maxSpeed = magnetMoveSpeed * MaximumMagnetMultiplier;
        float stepSpeed = Mathf.Clamp(currentMagnetSpeed, magnetMoveSpeed, maxSpeed);
        Vector2 destination = Vector2.MoveTowards(body.position, target.transform.position, stepSpeed * Time.fixedDeltaTime);
        body.MovePosition(destination);
        currentMagnetSpeed = Mathf.MoveTowards(stepSpeed, maxSpeed,
            (maxSpeed - magnetMoveSpeed) * Time.fixedDeltaTime / Mathf.Max(.05f, magnetAccelerationSeconds));
        if (Vector2.Distance(destination, target.transform.position) <= collectDistance)
            TryCollect(target);
    }

    public void Configure(InventoryItemDefinition definition, int count)
    {
        item = definition;
        amount = Mathf.Max(1, count);
        collecting = false;
        target = null;
        targetColliders.Clear();
        ConfigureCollider();
        RefreshVisual();
        SetMagnetState(false);
    }

    private void ConfigureCollider()
    {
        CircleCollider2D trigger = GetComponent<CircleCollider2D>();
        trigger.isTrigger = true;
        trigger.radius = IsQuestPickup ? questInteractRadius : magnetRadius;
        // Existing scene pickups may predate the Rigidbody2D requirement.
        body = GetComponent<Rigidbody2D>();
        if (body == null) body = gameObject.AddComponent<Rigidbody2D>();
        body.constraints = RigidbodyConstraints2D.FreezeRotation;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        currentMagnetSpeed = magnetMoveSpeed;
        magnetActive = false;
        body.bodyType = IsQuestPickup ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic;
        body.gravityScale = IsQuestPickup ? 0f : 1f;
    }

    private void SetMagnetState(bool active)
    {
        if (body == null) return;
        if (magnetActive != active)
        {
            magnetActive = active;
            currentMagnetSpeed = magnetMoveSpeed;
            body.linearVelocity = Vector2.zero;
            body.WakeUp();
        }
        bool fixedPickup = IsQuestPickup;
        var bodyType = active || fixedPickup ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic;
        if (body.bodyType != bodyType) body.bodyType = bodyType;
        body.gravityScale = active || fixedPickup ? 0f : 1f;
        // The wide trigger only detects attraction. During attraction, keep the old unobstructed path
        // to the player; when attraction stops, the item immediately falls/collides with the floor.
        bool solidEnabled = !active && !fixedPickup;
        if (physicalCollider != null && physicalCollider.enabled != solidEnabled)
        {
            physicalCollider.enabled = solidEnabled;
            if (solidEnabled) IgnoreOtherPickupCollisions();
        }
        if (!active) currentMagnetSpeed = magnetMoveSpeed;
    }

    private void RefreshVisual()
    {
        if (item == null) return;
        SpriteRenderer visual = GetComponent<SpriteRenderer>();
        bool smallDrop = item.ItemId == "red_cloth";
        if (smallDrop && smallDropVisual == null)
        {
            var existing = transform.Find("Red Cloth Visual");
            if (existing != null) smallDropVisual = existing.GetComponent<SpriteRenderer>();
            if (smallDropVisual == null)
            {
                var child = new GameObject("Red Cloth Visual");
                child.transform.SetParent(transform, false);
                child.transform.localScale = Vector3.one * .2f;
                smallDropVisual = child.AddComponent<SpriteRenderer>();
                smallDropVisual.sortingLayerID = visual.sortingLayerID;
            }
        }
        // Scale only the artwork so the pickup/magnet collider keeps its original range.
        visual.enabled = !smallDrop;
        if (smallDropVisual != null) smallDropVisual.enabled = smallDrop;
        if (smallDrop) visual = smallDropVisual;
        visual.sprite = item.Icon;
        visual.sortingOrder = 10;
        ApplyForegroundMaterial(visual);
        FitPhysicalCollider(visual);
        EnsureQuestHighlight();
    }

    private void FitPhysicalCollider(SpriteRenderer visual)
    {
        if (visual.sprite == null) return;
        physicalCollider = GetComponent<BoxCollider2D>();
        if (physicalCollider == null) physicalCollider = gameObject.AddComponent<BoxCollider2D>();
        physicalCollider.isTrigger = false;
        physicalCollider.excludeLayers = LayerMask.GetMask("Player", "Monster", "Interactable");
        var sprite = visual.sprite;
        var points = new List<Vector2>();
        Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
        void Include(Vector2 point)
        {
            Vector2 local = transform.InverseTransformPoint(visual.transform.TransformPoint(point));
            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
        }
        // Imported alpha outlines avoid treating transparent image padding as solid ground contact.
        // Generated placeholder icons may have no outline, so use their Sprite bounds instead.
        for (int shape = 0; shape < sprite.GetPhysicsShapeCount(); shape++)
        {
            sprite.GetPhysicsShape(shape, points);
            foreach (var point in points) Include(point);
        }
        if (float.IsPositiveInfinity(min.x))
        {
            Bounds bounds = sprite.bounds;
            Include(new Vector2(bounds.min.x, bounds.min.y));
            Include(new Vector2(bounds.min.x, bounds.max.y));
            Include(new Vector2(bounds.max.x, bounds.min.y));
            Include(new Vector2(bounds.max.x, bounds.max.y));
        }
        physicalCollider.offset = (min + max) * .5f;
        physicalCollider.size = Vector2.Max(max - min, Vector2.one * .02f);
        physicalCollider.enabled = !IsQuestPickup && !magnetActive;
        IgnoreOtherPickupCollisions();
    }

    private void IgnoreOtherPickupCollisions()
    {
        if (physicalCollider == null || !physicalCollider.isActiveAndEnabled) return;
        // Loot should settle on the map, not form piles on top of other loot.
        foreach (var other in FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None))
        {
            if (other == this || other.gameObject.scene != gameObject.scene || other.physicalCollider == null || !other.physicalCollider.isActiveAndEnabled) continue;
            Physics2D.IgnoreCollision(physicalCollider, other.physicalCollider, true);
        }
    }

    private static void ApplyForegroundMaterial(SpriteRenderer visual)
    {
        var material = Resources.Load<Material>("Effects/WorldItemForeground");
        if (material != null) visual.sharedMaterial = material;
    }

    private void OnTriggerEnter2D(Collider2D other) => TrackTarget(other);
    private void OnTriggerStay2D(Collider2D other) => TrackTarget(other);

    private void OnTriggerExit2D(Collider2D other)
    {
        targetColliders.Remove(other);
        PlayerInventory inventory = other.GetComponentInParent<PlayerInventory>();
        if (inventory == null || inventory != target) return;
        // Leaving with one of several player colliders must not restart attraction every frame.
        foreach (var remaining in targetColliders)
            if (remaining != null && remaining.enabled && remaining.gameObject.activeInHierarchy && remaining.GetComponentInParent<PlayerInventory>() == target) return;
        target = null;
    }

    private void TrackTarget(Collider2D other)
    {
        if (collecting || item == null || amount <= 0) return;
        PlayerInventory inventory = other.GetComponentInParent<PlayerInventory>();
        if (inventory == null) return;
        targetColliders.Add(other);
        target = inventory;
        if (!IsQuestPickup && !inventory.CanAddAny(item)) NotifyFull(inventory);
    }

    private void TryCollect(PlayerInventory inventory)
    {
        if (SceneFadeTransition.IsRunning || inventory == null || collecting) return;
        collecting = true;
        int accepted = inventory.Add(item, amount);
        if (accepted > 0) LocationHUD.ShowAcquired(item, accepted);
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
        ApplyForegroundMaterial(renderer);
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
