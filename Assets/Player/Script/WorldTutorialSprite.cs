using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Plays the same baked GIF as the HUD on an artist-placed world SpriteRenderer.
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class WorldTutorialSprite : MonoBehaviour
{
    [SerializeField] private string animationResource = "WASDSpace";
    [SerializeField] private Sprite sourceSprite;
    [SerializeField] private bool matchInteractionHintHeight = true;
    public string AnimationResource => animationResource;

    private SpriteRenderer visual;
    private ControlHintAnimation clip;
    private bool heightAligned;
    private float started;
    private readonly Dictionary<Sprite, Sprite> worldFrames = new();

    public void Configure(string resource)
    {
        animationResource = resource;
        visual = GetComponent<SpriteRenderer>();
        if (sourceSprite == null) sourceSprite = visual.sprite;
    }

    private void Awake()
    {
        visual = GetComponent<SpriteRenderer>();
        if (sourceSprite == null) sourceSprite = visual.sprite;
    }

    private void OnEnable()
    {
        started = Time.unscaledTime;
        heightAligned = false;
    }

    private void LateUpdate()
    {
        if (visual == null) visual = GetComponent<SpriteRenderer>();
        if (matchInteractionHintHeight && !heightAligned)
            heightAligned = AlignToInteractionHeight();
        clip ??= ControlHintAnimation.Load(animationResource);
        visual.sprite = WorldFrame(clip.Frame(Time.unscaledTime - started));
    }

    private Sprite WorldFrame(Sprite frame)
    {
        if (sourceSprite == null) return frame;
        // HUD sprites use PPU 100; retain the placed sprite's world size and pivot (W uses PPU 11).
        float pixelsPerUnit = sourceSprite.pixelsPerUnit * frame.rect.width / sourceSprite.rect.width;
        var pivot = new Vector2(sourceSprite.pivot.x / sourceSprite.rect.width, sourceSprite.pivot.y / sourceSprite.rect.height);
        var framePivot = new Vector2(frame.pivot.x / frame.rect.width, frame.pivot.y / frame.rect.height);
        if (Mathf.Approximately(pixelsPerUnit, frame.pixelsPerUnit) && (pivot - framePivot).sqrMagnitude < .000001f)
            return frame;
        if (!worldFrames.TryGetValue(frame, out var result) || result == null)
        {
            result = Sprite.Create(frame.texture, frame.rect, pivot, pixelsPerUnit, 0, SpriteMeshType.FullRect);
            result.name = animationResource + " World Frame";
            worldFrames[frame] = result;
        }
        return result;
    }

    public bool AlignToInteractionHeight()
    {
        var players = FindObjectsByType<PlayerMovement2D>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var player = players.FirstOrDefault(p => p.gameObject.scene == gameObject.scene && p.gameObject.activeInHierarchy)
            ?? players.FirstOrDefault(p => p.gameObject.activeInHierarchy);
        if (player == null) return false;
        var position = transform.position;
        if (!WorldInteractionHint.TryResolveHeight(new Bounds(position, Vector3.zero), player, out float height))
            return false;
        position.y = height;
        transform.position = position;
        return true;
    }

    private void OnDestroy()
    {
        foreach (var frame in worldFrames.Values)
            if (frame != null) Destroy(frame);
        worldFrames.Clear();
    }
}
