using UnityEngine;

/// <summary>Animated F anchored to the interaction point's floor, never to player jump height.</summary>
public sealed class WorldInteractionHint : MonoBehaviour
{
    private SpriteRenderer visual;
    private ControlHintAnimation clip;
    private Collider2D area;
    private PlayerMovement2D player;
    private Vector3 lastAnchor = Vector3.positiveInfinity;
    private float fixedY, alpha;
    private bool dismissed;
    public bool ShowUntilDismissed { get; set; }
    public void Dismiss() => dismissed = true;
    private void Awake()
    {
        area = GetComponent<Collider2D>();
        clip = ControlHintAnimation.Load("F");
        var root = new GameObject("Animated F Hint", typeof(SpriteRenderer));
        root.transform.SetParent(transform, false);
        visual = root.GetComponent<SpriteRenderer>();
        visual.sortingOrder = 100;
        var source = GetComponent<SpriteRenderer>();
        if (source != null) visual.sortingLayerID = source.sortingLayerID;
        visual.color = Color.clear;
    }
    private void LateUpdate()
    {
        if (player == null) player = FindFirstObjectByType<PlayerMovement2D>();
        if (player == null) { visual.color = Color.clear; return; }
        Bounds bounds = area != null ? area.bounds : new Bounds(transform.position, Vector3.one);
        if (lastAnchor != bounds.center)
        {
            lastAnchor = bounds.center;
            fixedY = ResolveHeight(bounds, player);
        }
        float distance = Mathf.Abs(player.transform.position.x - bounds.ClosestPoint(player.transform.position).x);
        var interaction = GetComponent<Interactable2D>();
        float target = !dismissed && interaction != null && interaction.enabled && (ShowUntilDismissed || distance < 2.5f) ? 1 : 0;
        alpha = Mathf.MoveTowards(alpha, target, Time.unscaledDeltaTime * 3);
        // Plain C# animation data is not retained when scripts reload during play.
        clip ??= ControlHintAnimation.Load("F");
        visual.sprite = clip.Frame(Time.unscaledTime);
        visual.color = new Color(1, 1, 1, alpha);
        visual.transform.position = new Vector3(bounds.center.x, fixedY, transform.position.z);
        Vector3 scale = transform.lossyScale;
        visual.transform.localScale = new Vector3(.36f / Mathf.Max(.001f, Mathf.Abs(scale.x)), .36f / Mathf.Max(.001f, Mathf.Abs(scale.y)), 1);
    }

    // Shared by F interaction prompts and authored world tutorial sprites.
    public static float ResolveHeight(Bounds anchor, PlayerMovement2D player)
    {
        TryResolveHeight(anchor, player, out float height);
        return height;
    }

    public static bool TryResolveHeight(Bounds anchor, PlayerMovement2D player, out float height)
    {
        height = anchor.max.y + .5f;
        var body = player != null ? player.GetComponent<Collider2D>() : null;
        if (body == null) return false;
        float playerHeight = body.bounds.size.y;
        if (playerHeight <= 0f && body is BoxCollider2D box)
            playerHeight = box.size.y * Mathf.Abs(body.transform.lossyScale.y);
        if (playerHeight <= 0f && body is CapsuleCollider2D capsule)
            playerHeight = capsule.size.y * Mathf.Abs(body.transform.lossyScale.y);
        if (playerHeight <= 0f) return false;

        int groundMask = LayerMask.GetMask("Ground");
        foreach (var hit in Physics2D.RaycastAll(anchor.center, Vector2.down, 20f, groundMask))
        {
            if (hit.collider.isTrigger || hit.normal.y < .5f) continue;
            height = hit.point.y + playerHeight + 1.05f;
            return true;
        }
        // Older maps may not tag their floor with the Ground layer.
        foreach (var hit in Physics2D.RaycastAll(anchor.center, Vector2.down, 20f))
        {
            if (hit.collider.isTrigger || hit.collider.GetComponentInParent<PlayerMovement2D>() != null
                || hit.collider.GetComponentInParent<MonsterFollowController>() != null) continue;
            height = hit.point.y + playerHeight + 1.05f;
            return true;
        }
        return false;
    }

    private void OnDisable() { alpha = 0; if (visual != null) visual.color = Color.clear; }
}
