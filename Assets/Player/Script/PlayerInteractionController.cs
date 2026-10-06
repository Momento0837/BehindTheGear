using UnityEngine;

[RequireComponent(typeof(PlayerInputReader), typeof(BoxCollider2D))]
public sealed class PlayerInteractionController : MonoBehaviour
{
    [SerializeField, Range(1f, 3f)] private float rangeMultiplier = 1.5f;
    [SerializeField] private LayerMask interactionLayers = ~0;
    [SerializeField] private InteractionPromptUI interactionPrompt;
    [SerializeField] private bool logSuccessfulInteractions = true;

    private BoxCollider2D playerHitbox;
    private Interactable2D currentTarget;
    private ContactFilter2D interactionFilter;
    private readonly Collider2D[] interactionHits = new Collider2D[16];

    private void Awake()
    {
        playerHitbox = GetComponent<BoxCollider2D>();
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
            Interactable2D interactable = hit.GetComponentInParent<Interactable2D>();
            if (interactable == null || interactable.gameObject == gameObject) continue;
            float distance = ((Vector2)interactable.transform.position - (Vector2)transform.position).sqrMagnitude;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = interactable;
            }
        }

        return closest;
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
