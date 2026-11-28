using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The level badge next to the player's head: a 9x9 pixel disc (smaller than the face) with the level number, whose
/// one-pixel outer ring fills clockwise
/// with EXP. When the ring is full it flashes, the number counts up and "LEVEL UP!" / "AP +1" rise above the player.
/// Drawn in code (no art assets) at the same 11 px-per-unit density as the player sprite, unlit like the monster HP bar.
/// It is a child of the player, so the base (which hides every player renderer) and stealth (which fades them) apply to
/// it automatically. Presentation only: the numbers live in PlayerLevel.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerLevelBadge : MonoBehaviour
{
    private const int Size = 9;
    private const float DiscRadius = 3.65f, RingRadius = 4.5f; // in badge pixels from the centre
    private const float PixelsPerUnit = 11f;      // player and monster sprites are imported at PPU 11
    private const int SortingOrder = 300;          // above the player and monsters, below damage numbers (600+)
    private const float FallbackHeadHeight = 1.075f;
    private const float MoteFlightSeconds = .26f;  // EXP shard flying from the pickup into the ring
    private const float RingFillPerSecond = .9f;   // slowest ring animation speed, in rings per second
    private const float PopSeconds = .32f;
    private const float BurstSeconds = .5f;

    private static readonly Color32 Clear = new(0, 0, 0, 0);
    private static readonly Color32 Track = new(58, 44, 74, 255);
    private static readonly Color32 Fill = new(183, 155, 255, 255);
    private static readonly Color32 FillHead = new(240, 230, 255, 255);
    private static readonly Color32 Disc = new(30, 22, 30, 255);
    private static readonly Color32 Digit = new(255, 242, 216, 255);
    private static readonly Color32 Flash = new(255, 250, 240, 255);
    private static readonly Color LevelUpColor = new(1f, .93f, .62f);
    private static readonly Color ActionPointColor = new(.55f, .86f, 1f);
    private static readonly Color ExperienceColor = new(.72f, .61f, 1f);

    // 3x5 pixel digits, rows top to bottom.
    private static readonly string[] Digits =
    {
        "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
        "111100111001111", "111100111101111", "111001010010010", "111101111101111", "111101111001111",
    };

    private static Material unlitMaterial;
    private static Sprite moteSprite;
    private static Sprite burstSprite;

    // Ring pixels as texture indices, ordered clockwise from 12 o'clock; built once per badge.
    private readonly List<int> ring = new();
    private readonly List<Mote> motes = new();
    private Color32[] basePixels;
    private Color32[] pixels;
    private Texture2D texture;
    private Sprite sprite;
    private PlayerLevel level;
    private SpriteRenderer body;
    private SpriteRenderer badge;
    private SpriteRenderer burst;
    private Transform root;
    private int shownLevel = 1;
    private float shownProgress;
    private int drawnLevel = -1, drawnLit = -1;
    private bool drawnFlash;
    private float holdUntil;       // ring waits for the shard that is still flying in
    private float popStarted = -10f;
    private float burstStarted = -10f;
    private float flashUntil;
    private int pendingActionPoints;
    private float actionPointLabelAt = -1f;

    private sealed class Mote
    {
        public Transform transform;
        public SpriteRenderer renderer;
        public Vector3 from;
        public float started;
    }

    private void Awake()
    {
        level = GetComponent<PlayerLevel>();
        body = GetComponent<SpriteRenderer>();
        BuildBadge();
        if (level == null) { enabled = false; return; }
        shownLevel = level.Level;
        shownProgress = level.Progress;
        level.Changed += OnLevelDataChanged;
        level.Restored += ShowCurrent;
        level.ExperienceGained += OnExperienceGained;
        level.LeveledUp += OnLeveledUp;
        Redraw(true);
    }

    private void OnDestroy()
    {
        if (level != null)
        {
            level.Changed -= OnLevelDataChanged;
            level.Restored -= ShowCurrent;
            level.ExperienceGained -= OnExperienceGained;
            level.LeveledUp -= OnLeveledUp;
        }
        foreach (var mote in motes) if (mote.transform != null) Destroy(mote.transform.gameObject);
        motes.Clear();
        if (sprite != null) Destroy(sprite);
        if (texture != null) Destroy(texture);
    }

    private void BuildBadge()
    {
        if (unlitMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader != null) unlitMaterial = new Material(shader) { name = "Level Badge Unlit" };
        }

        // Classify every pixel of the badge once: ring (sorted clockwise) or inner disc.
        basePixels = new Color32[Size * Size];
        pixels = new Color32[Size * Size];
        float centre = (Size - 1) * .5f;
        var ordered = new List<KeyValuePair<float, int>>();
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float dx = x - centre, dy = y - centre; // texture rows are bottom-up, so +dy is up
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                int index = y * Size + x;
                if (distance > RingRadius) basePixels[index] = Clear;
                else if (distance > DiscRadius)
                {
                    basePixels[index] = Track;
                    float angle = Mathf.Atan2(dx, dy);   // 0 at the top, growing clockwise
                    if (angle < 0f) angle += Mathf.PI * 2f;
                    ordered.Add(new KeyValuePair<float, int>(angle, index));
                }
                else basePixels[index] = Disc;
            }
        ordered.Sort((a, b) => a.Key.CompareTo(b.Key));
        ring.Clear();
        foreach (var entry in ordered) ring.Add(entry.Value);

        texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Level Badge" };
        sprite = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(.5f, .5f), PixelsPerUnit);
        sprite.name = "Level Badge";

        root = new GameObject("Level Badge").transform;
        root.SetParent(transform, false);
        badge = root.gameObject.AddComponent<SpriteRenderer>();
        badge.sprite = sprite;
        Configure(badge, SortingOrder);

        var burstObject = new GameObject("Level Up Ring");
        burstObject.transform.SetParent(root, false);
        burst = burstObject.AddComponent<SpriteRenderer>();
        burst.sprite = BurstSprite();
        Configure(burst, SortingOrder - 1);
        burst.enabled = false;
        PlaceBadge();
    }

    private void Configure(SpriteRenderer renderer, int order)
    {
        if (unlitMaterial != null) renderer.sharedMaterial = unlitMaterial;
        if (body != null) renderer.sortingLayerID = body.sortingLayerID;
        renderer.sortingOrder = order;
    }

    // Up and to the left of the head; the offset is tuned in Resources/Monsters/RewardTable (Level badge).
    private void PlaceBadge()
    {
        Vector2 top = new(0f, FallbackHeadHeight);
        var box = GetComponent<BoxCollider2D>();
        if (box != null) top = box.offset + new Vector2(0f, box.size.y * .5f);
        Vector2 position = top + PlayerLevel.Table.levelBadgeOffset;
        // Snap to the sprite pixel grid so the badge pixels line up with the player's. The player frames are an even
        // number of pixels around a centre pivot (pixel edges on whole pixels); this badge is an odd number of pixels,
        // so its centre sits on a half pixel.
        position.x = (Mathf.Round(position.x * PixelsPerUnit - .5f) + .5f) / PixelsPerUnit;
        position.y = (Mathf.Round(position.y * PixelsPerUnit - .5f) + .5f) / PixelsPerUnit;
        root.localPosition = position;
    }

    private void OnLevelDataChanged()
    {
        // Anything that lowers the numbers is shown at once; gains animate in Update.
        if (level.Level < shownLevel || level.Level == shownLevel && level.Progress < shownProgress) ShowCurrent();
    }

    // A loaded save jumps straight to its level: no ring animation, no "LEVEL UP!", no sound.
    private void ShowCurrent()
    {
        shownLevel = level.Level;
        shownProgress = level.Progress;
        pendingActionPoints = 0;
        actionPointLabelAt = -1f;
        holdUntil = 0f;
        Redraw(true);
    }

    private void OnExperienceGained(int amount, Vector3 source)
    {
        if (!isActiveAndEnabled) return;
        var mote = new GameObject("EXP Mote");
        var renderer = mote.AddComponent<SpriteRenderer>();
        renderer.sprite = MoteSprite();
        renderer.color = ExperienceColor;
        Configure(renderer, SortingOrder + 1);
        mote.transform.position = source;
        motes.Add(new Mote { transform = mote.transform, renderer = renderer, from = source, started = Time.time });
        holdUntil = Mathf.Max(holdUntil, Time.time + MoteFlightSeconds);
    }

    private void OnLeveledUp(int newLevel, int actionPoints) => pendingActionPoints += Mathf.Max(0, actionPoints);

    private void Update()
    {
        UpdateMotes();
        AnimateRing();
        AnimatePop();
        if (actionPointLabelAt > 0f && Time.time >= actionPointLabelAt)
        {
            actionPointLabelAt = -1f;
            if (pendingActionPoints > 0)
                MonsterDamageNumbers.ShowLabel(HeadPoint(.55f), "AP +" + pendingActionPoints, ActionPointColor, .95f);
            pendingActionPoints = 0;
        }
    }

    private void LateUpdate()
    {
        // Follow the player sprite: hidden while the body is not drawn (inside the base, portal travel, ...).
        bool visible = body == null || body.enabled;
        if (badge.enabled != visible) badge.enabled = visible;
        if (!visible && burst.enabled) burst.enabled = false;
        if (body == null) return;
        if (badge.forceRenderingOff != body.forceRenderingOff) badge.forceRenderingOff = body.forceRenderingOff;
        if (burst.forceRenderingOff != body.forceRenderingOff) burst.forceRenderingOff = body.forceRenderingOff;
    }

    private void UpdateMotes()
    {
        for (int i = motes.Count - 1; i >= 0; i--)
        {
            var mote = motes[i];
            if (mote.transform == null) { motes.RemoveAt(i); continue; }
            float t = Mathf.Clamp01((Time.time - mote.started) / MoteFlightSeconds);
            float eased = t * t; // starts slowly, then snaps into the ring
            Vector3 target = root.position;
            Vector3 position = Vector3.LerpUnclamped(mote.from, target, eased);
            position.y += Mathf.Sin(t * Mathf.PI) * .35f; // small arc
            mote.transform.position = position;
            mote.transform.localScale = Vector3.one * Mathf.Lerp(1f, .6f, t);
            if (t < 1f) continue;
            Destroy(mote.transform.gameObject);
            motes.RemoveAt(i);
            flashUntil = Mathf.Max(flashUntil, Time.time + .07f); // the ring blinks as the shard lands
        }
    }

    private void AnimateRing()
    {
        if (level == null) return;
        bool levelPending = shownLevel < level.Level;
        float target = levelPending ? 1f : level.Progress;
        if (Time.time >= holdUntil && (levelPending || !Mathf.Approximately(shownProgress, target)))
        {
            // Several levels at once (a boss) should not take seconds: speed up with the amount still to show.
            float backlog = level.Level - shownLevel + Mathf.Abs(target - shownProgress);
            float speed = RingFillPerSecond * Mathf.Max(1f, backlog * 1.6f);
            shownProgress = Mathf.MoveTowards(shownProgress, target, speed * Time.deltaTime);
            if (levelPending && shownProgress >= 1f)
            {
                shownLevel++;
                shownProgress = 0f;
                PlayLevelUp();
            }
        }
        Redraw(false);
    }

    private void PlayLevelUp()
    {
        popStarted = burstStarted = Time.time;
        flashUntil = Time.time + .16f;
        MonsterDamageNumbers.ShowLabel(HeadPoint(.2f), "LEVEL UP!", LevelUpColor, 1.15f);
        // One "AP +n" for everything gained in this burst, shortly after the level-up text.
        // Wait until "LEVEL UP!" (0.8 s label) has faded so the two rising labels don't overlap.
        if (shownLevel >= level.Level) actionPointLabelAt = Time.time + .8f;
    }

    private void AnimatePop()
    {
        float pop = (Time.time - popStarted) / PopSeconds;
        root.localScale = Vector3.one * (pop >= 0f && pop < 1f ? 1f + .45f * Mathf.Sin(pop * Mathf.PI) : 1f);

        float t = (Time.time - burstStarted) / BurstSeconds;
        bool bursting = t >= 0f && t < 1f && badge.enabled;
        if (burst.enabled != bursting) burst.enabled = bursting;
        if (!bursting) return;
        burst.transform.localScale = Vector3.one * Mathf.Lerp(.9f, 2.6f, 1f - (1f - t) * (1f - t));
        burst.color = new Color(1f, .96f, .86f, (1f - t) * badge.color.a);
    }

    private Vector3 HeadPoint(float above) => root.position + new Vector3(.9f, .3f + above, 0f);

    private void Redraw(bool force)
    {
        int lit = shownProgress >= 1f ? ring.Count : Mathf.Clamp(Mathf.RoundToInt(shownProgress * ring.Count), 0, ring.Count);
        // Never show a full ring while the level is not actually complete (it would look like a missed level-up).
        if (lit == ring.Count && shownProgress < 1f) lit = ring.Count - 1;
        bool flash = Time.time < flashUntil;
        if (!force && lit == drawnLit && shownLevel == drawnLevel && flash == drawnFlash) return;
        drawnLit = lit; drawnLevel = shownLevel; drawnFlash = flash;

        System.Array.Copy(basePixels, pixels, pixels.Length);
        for (int i = 0; i < ring.Count; i++)
            if (i < lit) pixels[ring[i]] = flash ? Flash : Fill;
        if (lit > 0 && lit < ring.Count) pixels[ring[lit - 1]] = FillHead;
        DrawNumber(Mathf.Clamp(shownLevel, 1, 99));
        texture.SetPixels32(pixels);
        texture.Apply(false);
    }

    private void DrawNumber(int value)
    {
        string text = value.ToString();
        int width = text.Length * 3 + (text.Length - 1);
        int left = (Size - width) / 2;
        int top = (Size - 5) / 2 + 4; // texture row of the digits' first (top) line (rows are bottom-up)
        for (int k = 0; k < text.Length; k++)
        {
            string glyph = Digits[text[k] - '0'];
            for (int row = 0; row < 5; row++)
                for (int column = 0; column < 3; column++)
                    if (glyph[row * 3 + column] == '1')
                        pixels[(top - row) * Size + left + k * 4 + column] = Digit;
        }
    }

    private static Sprite MoteSprite()
    {
        if (moteSprite != null) return moteSprite;
        var mote = new Texture2D(3, 3, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "EXP Mote" };
        var clear = new Color32(0, 0, 0, 0); var white = new Color32(255, 255, 255, 255);
        mote.SetPixels32(new[] { clear, white, clear, white, white, white, clear, white, clear });
        mote.Apply(false);
        moteSprite = Sprite.Create(mote, new Rect(0, 0, 3, 3), new Vector2(.5f, .5f), PixelsPerUnit);
        moteSprite.name = "EXP Mote";
        return moteSprite;
    }

    // One-pixel circle outline that expands and fades when the level goes up.
    private static Sprite BurstSprite()
    {
        if (burstSprite != null) return burstSprite;
        const int size = 11;
        var ringTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Level Up Ring" };
        var buffer = new Color32[size * size];
        float centre = (size - 1) * .5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float distance = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre));
                buffer[y * size + x] = distance > 4.3f && distance <= 5.3f ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            }
        ringTexture.SetPixels32(buffer);
        ringTexture.Apply(false);
        burstSprite = Sprite.Create(ringTexture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), PixelsPerUnit);
        burstSprite.name = "Level Up Ring";
        return burstSprite;
    }
}
