using UnityEngine;

[RequireComponent(typeof(PlayerInputReader), typeof(BoxCollider2D), typeof(PlayerSpriteFacing))]
public sealed class PlayerInteractionController : MonoBehaviour
{
    [SerializeField, Range(1f, 3f)] private float rangeMultiplier = 2f;
    [SerializeField] private LayerMask interactionLayers = ~0;
    [SerializeField] private LayerMask lineOfSightLayers = ~0;
    [SerializeField] private InteractionPromptUI interactionPrompt;
    [SerializeField] private bool logSuccessfulInteractions = true;

    private BoxCollider2D playerHitbox;
    private PlayerSpriteFacing playerFacing;
    private Interactable2D currentTarget;
    private ContactFilter2D interactionFilter;
    private readonly Collider2D[] interactionHits = new Collider2D[16];

    private void Awake()
    {
        playerHitbox = GetComponent<BoxCollider2D>();
        playerFacing = GetComponent<PlayerSpriteFacing>();
        GetComponent<PlayerInputReader>().InteractPressed += TryInteract;
        interactionFilter.SetLayerMask(interactionLayers);
        interactionFilter.useTriggers = true;
    }

    private void Update()
    {
        currentTarget = FindClosestInteractable();
        if (interactionPrompt != null)
        {
            interactionPrompt.SetVisible(currentTarget != null);
            if (currentTarget != null) interactionPrompt.SetTarget(currentTarget);
        }
    }

    public void ConfigurePrompt(InteractionPromptUI prompt) => interactionPrompt = prompt;

    public void TryInteract()
    {
        Interactable2D target = currentTarget != null ? currentTarget : FindClosestInteractable();
        if (target == null) return;
        target.Interact(gameObject);
        if (logSuccessfulInteractions)
            Debug.Log($"[Player Interaction] F used on '{target.name}'.", target);
    }

    private Interactable2D FindClosestInteractable()
    {
        Bounds bounds = playerHitbox.bounds;
        Interactable2D closest = null;
        float closestDistance = float.MaxValue;
        int hitCount = Physics2D.OverlapBox(
            bounds.center,
            bounds.size * rangeMultiplier,
            0f,
            interactionFilter,
            interactionHits);

        for (int i = 0; i < hitCount; i++)
        {
            Collider2D hit = interactionHits[i];
            if (hit == null || !hit.isTrigger) continue;
            Interactable2D interactable = hit.GetComponentInParent<Interactable2D>();
            if (interactable == null || interactable.gameObject == gameObject) continue;
            if (!IsInFacingDirection(bounds.center, hit.bounds.center)) continue;
            if (!HasLineOfSight(bounds.center, hit, interactable)) continue;
            float distance = ((Vector2)interactable.transform.position - (Vector2)transform.position).sqrMagnitude;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = interactable;
            }
        }

        return closest;
    }

    private bool IsInFacingDirection(Vector2 playerPosition, Vector2 targetPosition)
    {
        float horizontalOffset = targetPosition.x - playerPosition.x;
        return horizontalOffset * playerFacing.FacingDirection >= -0.05f;
    }

    private bool HasLineOfSight(Vector2 origin, Collider2D targetCollider, Interactable2D target)
    {
        Vector2 targetPoint = targetCollider.ClosestPoint(origin);
        Vector2 direction = targetPoint - origin;
        float distance = direction.magnitude;
        if (distance <= 0.01f) return true;

        int blockerMask = lineOfSightLayers.value & ~(1 << gameObject.layer);
        RaycastHit2D hit = Physics2D.Raycast(origin, direction / distance, distance, blockerMask);
        if (hit.collider == null) return true;
        return hit.collider.GetComponentInParent<Interactable2D>() == target;
    }

    private void OnDrawGizmosSelected()
    {
        BoxCollider2D hitbox = playerHitbox != null ? playerHitbox : GetComponent<BoxCollider2D>();
        if (hitbox == null) return;
        Bounds bounds = hitbox.bounds;
        Gizmos.color = new Color(.2f, 1f, .45f, .28f);
        Gizmos.DrawCube(bounds.center, bounds.size * rangeMultiplier);
        Gizmos.color = new Color(.2f, 1f, .45f, 1f);
        Gizmos.DrawWireCube(bounds.center, bounds.size * rangeMultiplier);
    }
}
