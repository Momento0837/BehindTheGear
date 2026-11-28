using UnityEngine;

/// <summary>
/// One-shot (or held) frame animation for the boss's effects: dust, steam, sparks, marks. Flat pixel sprites that are
/// only moved, on the pixel grid; never rotated or scaled. Uses scaled time, so effects freeze with the game.
/// </summary>
public sealed class BossFx : MonoBehaviour
{
    private Sprite[] frames;
    private SpriteRenderer spriteRenderer;
    private Vector3 position;
    private Vector2 velocity;
    private float frameSeconds, age;
    private bool loop;

    /// <summary>Plays `frames` once at `position` (centre) and removes itself. `loop` keeps it until Stop().</summary>
    public static BossFx Play(BossGoliathRig rig, Sprite[] frames, Vector3 position, float frameSeconds, int sortingOrder,
        Vector2 velocity = default, bool flipX = false, bool loop = false)
    {
        if (frames == null || frames.Length == 0 || frames[0] == null) return null;
        var go = new GameObject("Boss FX");
        var fx = go.AddComponent<BossFx>();
        fx.frames = frames;
        fx.frameSeconds = Mathf.Max(.01f, frameSeconds);
        fx.velocity = velocity;
        fx.loop = loop;
        fx.position = position;
        fx.spriteRenderer = go.AddComponent<SpriteRenderer>();
        if (rig != null && rig.Material != null) fx.spriteRenderer.sharedMaterial = rig.Material;
        fx.spriteRenderer.sortingOrder = sortingOrder;
        fx.spriteRenderer.flipX = flipX;
        fx.spriteRenderer.sprite = frames[0];
        go.transform.position = BossGoliath.Snap(position);
        return fx;
    }

    /// <summary>Shows one chosen frame (held effects such as the slam shadow pick their own frame).</summary>
    public void SetFrame(int index)
    {
        if (spriteRenderer != null) spriteRenderer.sprite = frames[Mathf.Clamp(index, 0, frames.Length - 1)];
    }

    public void MoveTo(Vector3 worldPosition)
    {
        position = worldPosition;
        transform.position = BossGoliath.Snap(position);
    }

    public void Stop()
    {
        if (this != null) Destroy(gameObject);
    }

    private void Update()
    {
        age += Time.deltaTime;
        position += (Vector3)(velocity * Time.deltaTime);
        transform.position = BossGoliath.Snap(position);
        if (loop && frameSeconds >= 100f) return; // held: the owner picks the frame
        int index = Mathf.FloorToInt(age / frameSeconds);
        if (index >= frames.Length)
        {
            if (!loop) { Destroy(gameObject); return; }
            index %= frames.Length;
        }
        spriteRenderer.sprite = frames[index];
    }
}
