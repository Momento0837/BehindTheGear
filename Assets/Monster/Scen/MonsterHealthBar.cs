using UnityEngine;

/// <summary>
/// Small pixel-style HP bar above a monster. Attach it with Tools/Behind The Gear/Monster/Add Health Bars to All Monsters,
/// or at runtime through MonsterProfile.showHealthBar. Sizes are in sprite pixels (PPU 11) and editable in the Inspector.
/// Unlit so it stays readable in the dark forest; a light "trail" segment shows the damage just taken,
/// then shrinks to the real value. Hidden as soon as the monster dies.
/// </summary>
[RequireComponent(typeof(MonsterFollowController))]
public sealed class MonsterHealthBar : MonoBehaviour
{
    private const float PixelsPerUnit = 11f; // monster sprites are imported at PPU 11

    [Tooltip("Bar length in sprite pixels (11 px = 1 unit).")]
    [SerializeField, Min(4)] private int widthPixels = 12;
    [Tooltip("Bar thickness in sprite pixels, not counting the 1 px border.")]
    [SerializeField, Min(1)] private int heightPixels = 1;
    [SerializeField] private float gapAboveSprite = .15f;
    [SerializeField, Min(0f)] private float trailDelay = .35f;
    [SerializeField, Min(0f)] private float trailSpeed = 1.5f;  // fraction of the bar per second

    private static readonly Color BorderColor = new(.05f, .04f, .04f, .9f);
    private static readonly Color BackColor = new(.22f, .1f, .09f, 1f);
    private static readonly Color TrailColor = new(1f, .9f, .78f, 1f);
    private static readonly Color FillColor = new(.86f, .2f, .16f, 1f);
    private static Sprite pixel;
    private static Material unlitMaterial;

    private MonsterFollowController monster;
    private SpriteRenderer body;
    private Transform root;
    private SpriteRenderer border, back, trail, fill;
    private float topOffset;
    private bool measured;
    private float width;
    private float height;
    private float trailValue = 1f;
    private float trailHoldUntil;
    private int lastHealth = -1;

    private static Sprite Pixel
    {
        get
        {
            // 1x1 world unit, pivot on the left edge so scaling X shrinks the bar toward the left.
            if (pixel == null) pixel = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0f, .5f), 1f);
            return pixel;
        }
    }

    private void Start()
    {
        monster = GetComponent<MonsterFollowController>();
        body = GetComponent<SpriteRenderer>();
        if (unlitMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader != null) unlitMaterial = new Material(shader);
        }

        // A monster cloned at runtime (StorySceneFlow copies Monster_1) carries the source's bar; drop it and build our own.
        foreach (Transform child in transform)
            if (child.name == "HP Bar") { child.gameObject.SetActive(false); Destroy(child.gameObject); }

        width = widthPixels / PixelsPerUnit;
        height = heightPixels / PixelsPerUnit;
        root = new GameObject("HP Bar").transform;
        root.SetParent(transform, false);
        float px = 1f / PixelsPerUnit;
        border = Part("Border", BorderColor, 0, -width * .5f - px, width + 2f * px, height + 2f * px);
        back = Part("Back", BackColor, 1, -width * .5f, width, height);
        trail = Part("Trail", TrailColor, 2, -width * .5f, width, height);
        fill = Part("Fill", FillColor, 3, -width * .5f, width, height);

        trailValue = Ratio();
    }

    private SpriteRenderer Part(string partName, Color color, int order, float left, float w, float h)
    {
        var part = new GameObject(partName);
        part.transform.SetParent(root, false);
        part.transform.localPosition = new Vector3(left, 0f, 0f);
        part.transform.localScale = new Vector3(w, h, 1f);
        var renderer = part.AddComponent<SpriteRenderer>();
        renderer.sprite = Pixel;
        renderer.color = color;
        if (unlitMaterial != null) renderer.sharedMaterial = unlitMaterial;
        renderer.sortingOrder = order; // re-based on the monster's sorting every frame
        return renderer;
    }

    private float Ratio() => Mathf.Clamp01((float)monster.CurrentHealth / Mathf.Max(1, monster.MaxHealth));

    private void LateUpdate()
    {
        if (root == null || monster == null) return;
        int health = monster.CurrentHealth;
        if (health <= 0)
        {
            root.gameObject.SetActive(false);
            enabled = false;
            return;
        }
        // Hidden while the monster's AI is switched off (story cutscenes / tutorial holds disable it).
        bool visible = monster.enabled;
        if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
        if (!visible) return;

        // Measure once, in the first LateUpdate: the Animator has applied this monster's own frame by then
        // (a cloned monster still shows its source sprite during Start). Fixed afterwards so the bar does not bob.
        if (!measured)
        {
            topOffset = body != null && body.sprite != null ? body.bounds.max.y - transform.position.y + gapAboveSprite : 1f;
            measured = true;
        }

        // Keep the bar a constant world size and upright no matter how the monster is scaled.
        Vector3 scale = transform.lossyScale;
        root.localScale = new Vector3(1f / Mathf.Max(.0001f, Mathf.Abs(scale.x)), 1f / Mathf.Max(.0001f, Mathf.Abs(scale.y)), 1f);
        root.position = new Vector3(transform.position.x, transform.position.y + topOffset, transform.position.z);

        float value = Ratio();
        if (lastHealth >= 0 && health < lastHealth) trailHoldUntil = Time.time + trailDelay;
        if (value >= trailValue) trailValue = value;
        else if (Time.time >= trailHoldUntil) trailValue = Mathf.MoveTowards(trailValue, value, trailSpeed * Time.deltaTime);
        lastHealth = health;

        SetLength(fill, value);
        SetLength(trail, trailValue);

        if (body != null)
        {
            int baseOrder = body.sortingOrder + 2; // +1 is the monster's hit-flash overlay
            SyncSorting(border, baseOrder);
            SyncSorting(back, baseOrder + 1);
            SyncSorting(trail, baseOrder + 2);
            SyncSorting(fill, baseOrder + 3);
        }
    }

    private void SetLength(SpriteRenderer part, float value)
    {
        // Snap to whole sprite pixels; any HP left keeps at least one visible pixel.
        float pixels = Mathf.Round(widthPixels * value);
        if (value > 0f) pixels = Mathf.Max(1f, pixels);
        var s = part.transform.localScale;
        part.transform.localScale = new Vector3(pixels / PixelsPerUnit, s.y, 1f);
        part.enabled = pixels > 0f;
    }

    private void SyncSorting(SpriteRenderer part, int order)
    {
        part.sortingLayerID = body.sortingLayerID;
        part.sortingOrder = order;
    }
}
