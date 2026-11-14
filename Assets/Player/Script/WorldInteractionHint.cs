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
            fixedY = bounds.max.y + .5f;
            foreach (var hit in Physics2D.RaycastAll(bounds.center, Vector2.down, 20f))
            {
                if (hit.collider.isTrigger || hit.collider.GetComponentInParent<PlayerMovement2D>() != null
                    || hit.collider.GetComponentInParent<MonsterFollowController>() != null) continue;
                fixedY = hit.point.y + player.GetComponent<Collider2D>().bounds.size.y + 1.05f;
                break;
            }
        }
        float distance = Mathf.Abs(player.transform.position.x - bounds.ClosestPoint(player.transform.position).x);
        var interaction = GetComponent<Interactable2D>();
        float target = !dismissed && interaction != null && interaction.enabled && (ShowUntilDismissed || distance < 2.5f) ? 1 : 0;
        alpha = Mathf.MoveTowards(alpha, target, Time.unscaledDeltaTime * 3);
        visual.sprite = clip.Frame(Time.unscaledTime);
        visual.color = new Color(1, 1, 1, alpha);
        visual.transform.position = new Vector3(bounds.center.x, fixedY, transform.position.z);
        Vector3 scale = transform.lossyScale;
        visual.transform.localScale = new Vector3(.36f / Mathf.Max(.001f, Mathf.Abs(scale.x)), .36f / Mathf.Max(.001f, Mathf.Abs(scale.y)), 1);
    }
    private void OnDisable() { alpha = 0; if (visual != null) visual.color = Color.clear; }
}
