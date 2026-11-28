using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The boss's parts and where they sit. Each part is its own SpriteRenderer that is only ever moved in whole pixels
/// and swapped between frames: pixel art is never rotated or scaled here (that would break the grid).
/// Everything is addressed in "body pixels": the 112x92 body frame, origin top-left, y down, 11 px = 1 unit.
/// The root transform is the middle of the body on the ground line.
/// Body, furnace light, gear and pilot share one SortingGroup (behind the player); fists and chains draw in front.
/// </summary>
[DisallowMultipleComponent]
public sealed class BossGoliathRig : MonoBehaviour
{
    public const int Left = 0, Right = 1;
    private const float Ppu = BossSpriteLayout.PixelsPerUnit;

    private BossProfile profile;
    private Sprite[] bodySprites, pilotSprites, gearSprites, furnaceSprites, fistSprites;
    private Sprite chainSprite;
    private SpriteRenderer body, pilot, gear, furnace;
    private readonly SpriteRenderer[] fists = new SpriteRenderer[2];
    private SpriteRenderer[] links;
    private Color tint = Color.white, tone = Color.white;
    private static Material unlit;

    public BossSpriteLayout.Body BodyFrame { get; set; } = BossSpriteLayout.Body.Idle0;
    public BossSpriteLayout.Pilot PilotFrame { get; set; } = BossSpriteLayout.Pilot.Idle0;
    public int GearStep { get; set; }
    /// <summary>Furnace light stage, or -1 when the fire is out.</summary>
    public int FurnaceStage { get; set; }
    /// <summary>Pixels the bucket has ridden down the rails (overheat), on top of the body frame's own slump.</summary>
    public int PilotDrop { get; set; }
    /// <summary>Extra pixel offset of the pilot (hit shake).</summary>
    public Vector2Int PilotNudge { get; set; }
    public bool PilotVisible { get; set; } = true;
    public bool GearVisible { get; set; } = true;
    public bool FistsVisible { get; set; } = true;

    /// <summary>Top-left pixel of each fist in body pixels (may lie far outside the body frame).</summary>
    public readonly Vector2[] FistPixel = new Vector2[2];
    public readonly BossSpriteLayout.Fist[] FistFrame = new BossSpriteLayout.Fist[2];
    /// <summary>Side view only: true when that fist travels to the left.</summary>
    public readonly bool[] FistFacesLeft = new bool[2];

    // Effect frames, cut once here so every effect shares them.
    public Sprite[] Dust { get; private set; }
    public Sprite[] Shockwave { get; private set; }
    public Sprite[] Slag { get; private set; }
    public Sprite[] Marker { get; private set; }
    public Sprite[] Steam { get; private set; }
    public Sprite[] Spark { get; private set; }
    public Sprite[] Explosion { get; private set; }
    public Sprite[] Shadow { get; private set; }
    public Material Material { get; private set; }
    private readonly Collider2D[] platforms = new Collider2D[2];

    public Transform PilotTransform => pilot != null ? pilot.transform : null;
    public Transform FistTransform(int side) => fists[side] != null ? fists[side].transform : null;
    public BossSpriteLayout.BodyAnchors Anchors => BossSpriteLayout.Anchors[(int)BodyFrame];

    // The forest's additive spotlights turned the big flat iron surfaces to haze, so the boss is drawn unlit and takes
    // the scene's dim warm light from BossProfile.sceneTone instead. Effects use the same material at full brightness.
    private static Material UnlitMaterial()
    {
        if (unlit != null) return unlit;
        var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        unlit = new Material(shader) { name = "Boss Unlit" };
        return unlit;
    }

    public void Build(BossProfile bossProfile)
    {
        profile = bossProfile;
        tone = profile.sceneTone;
        Material material = UnlitMaterial();
        bodySprites = Slice(profile.body, BossSpriteLayout.BodySize, BossSpriteLayout.BodyFrames);
        pilotSprites = Slice(profile.pilot, BossSpriteLayout.PilotSize, BossSpriteLayout.PilotFrames);
        gearSprites = Slice(profile.gear, BossSpriteLayout.GearSize, BossSpriteLayout.GearFrames);
        furnaceSprites = Slice(profile.furnace, BossSpriteLayout.FurnaceSize, BossSpriteLayout.FurnaceFrames);
        fistSprites = Slice(profile.fist, BossSpriteLayout.FistSize, BossSpriteLayout.FistFrames);
        chainSprite = Slice(profile.chain, BossSpriteLayout.ChainSize, BossSpriteLayout.ChainFrames)[0];

        // Machine: one sorting group so its parts keep their order and sit between the background and the player.
        var machine = new GameObject("Machine");
        machine.transform.SetParent(transform, false);
        var group = machine.AddComponent<SortingGroup>();
        group.sortingOrder = profile.bodySortingOrder;
        body = Part("Body", machine.transform, material, 0);
        furnace = Part("Furnace Light", machine.transform, material, 1);
        gear = Part("Gear", machine.transform, material, 2);
        pilot = Part("Pilot", machine.transform, material, 3);

        links = new SpriteRenderer[Mathf.Max(1, profile.chainLinks) * 2];
        for (int i = 0; i < links.Length; i++)
        {
            links[i] = Part("Chain Link", transform, material, profile.fistSortingOrder - 1);
            links[i].sprite = chainSprite;
        }
        fists[Left] = Part("Fist Left", transform, material, profile.fistSortingOrder);
        fists[Right] = Part("Fist Right", transform, material, profile.fistSortingOrder);
        for (int side = 0; side < 2; side++)
        {
            FistPixel[side] = RestFist(side);
            platforms[side] = BuildPlatform(fists[side].transform);
        }

        Material = material;
        Dust = Slice(profile.fxDust, BossSpriteLayout.FxDustSize, BossSpriteLayout.FxDustFrames);
        Shockwave = Slice(profile.fxShockwave, BossSpriteLayout.FxShockwaveSize, BossSpriteLayout.FxShockwaveFrames);
        Slag = Slice(profile.fxSlag, BossSpriteLayout.FxSlagSize, BossSpriteLayout.FxSlagFrames);
        Marker = Slice(profile.fxMarker, BossSpriteLayout.FxMarkerSize, BossSpriteLayout.FxMarkerFrames);
        Steam = Slice(profile.fxSteam, BossSpriteLayout.FxSteamSize, BossSpriteLayout.FxSteamFrames);
        Spark = Slice(profile.fxSpark, BossSpriteLayout.FxSparkSize, BossSpriteLayout.FxSparkFrames);
        Explosion = Slice(profile.fxExplosion, BossSpriteLayout.FxExplosionSize, BossSpriteLayout.FxExplosionFrames);
        Shadow = Slice(profile.fxShadow, BossSpriteLayout.FxShadowSize, BossSpriteLayout.FxShadowFrames);
        Apply();
    }

    // A fist stuck in the ground is something to stand on: a thin one-way ledge along its top, on the Ground layer.
    // One-way, so switching it on can never trap or shove a player who happens to be inside the fist.
    private static Collider2D BuildPlatform(Transform fist)
    {
        var go = new GameObject("Platform") { layer = 6 };
        go.transform.SetParent(fist, false);
        var box = go.AddComponent<BoxCollider2D>();
        box.size = new Vector2((BossSpriteLayout.FistSize.x - 4) / Ppu, 4f / Ppu);
        box.offset = new Vector2(0f, (BossSpriteLayout.FistSize.y * .5f - 7f) / Ppu); // just under the wrist cuff
        box.usedByEffector = true;
        var effector = go.AddComponent<PlatformEffector2D>();
        effector.useOneWay = true;
        effector.surfaceArc = 170f;
        effector.useSideFriction = false;
        box.enabled = false;
        return box;
    }

    /// <summary>Turns the standing ledge of a fist on (stuck in the ground) or off (moving).</summary>
    public void SetFistPlatform(int side, bool on)
    {
        if (platforms[side] != null) platforms[side].enabled = on;
    }

    /// <summary>World rectangle a fist covers right now.</summary>
    public Rect FistWorldRect(int side)
    {
        Vector2 pixel = new(Mathf.Round(FistPixel[side].x), Mathf.Round(FistPixel[side].y));
        Vector3 bottomLeft = PixelToWorld(pixel.x, pixel.y + BossSpriteLayout.FistSize.y);
        return new Rect(bottomLeft.x, bottomLeft.y, BossSpriteLayout.FistSize.x / Ppu, BossSpriteLayout.FistSize.y / Ppu);
    }

    /// <summary>Where a fist hangs when nothing is happening (top-left pixel, body pixels).</summary>
    public Vector2 RestFist(int side)
    {
        var anchors = Anchors;
        return side == Right
            ? new Vector2(anchors.DrumRight.x + profile.fistRest.x, anchors.DrumRight.y + profile.fistRest.y)
            : new Vector2(anchors.DrumLeft.x - profile.fistRest.x - BossSpriteLayout.FistSize.x, anchors.DrumLeft.y + profile.fistRest.y);
    }

    /// <summary>Darkens or restores the whole boss (silhouette entrance). Multiplies the lit sprite colour.</summary>
    public void SetTint(Color color)
    {
        tint = color;
        Color shown = new(tone.r * color.r, tone.g * color.g, tone.b * color.b, color.a);
        foreach (var renderer in GetComponentsInChildren<SpriteRenderer>(true)) renderer.color = shown;
    }

    public Color Tint => tint;

    /// <summary>World position of a point given in body pixels (x right, y down from the frame's top-left).</summary>
    public Vector3 PixelToWorld(float x, float y)
    {
        return transform.position + new Vector3((x - BossSpriteLayout.BodySize.x * .5f) / Ppu, (BossSpriteLayout.BodySize.y - y) / Ppu, 0f);
    }

    /// <summary>Body pixel X of a world X (for aiming a fist at the player).</summary>
    public float WorldToPixelX(float worldX) => (worldX - transform.position.x) * Ppu + BossSpriteLayout.BodySize.x * .5f;

    /// <summary>Body pixel Y of the ground line (the bottom row of the body frame).</summary>
    public float GroundPixelY => BossSpriteLayout.BodySize.y;

    private void LateUpdate() => Apply();

    public void Apply()
    {
        if (body == null) return;
        var anchors = Anchors;
        body.sprite = bodySprites[(int)BodyFrame];
        Place(body.transform, Vector2.zero, BossSpriteLayout.BodySize);

        furnace.enabled = FurnaceStage >= 0;
        if (furnace.enabled) furnace.sprite = furnaceSprites[Mathf.Min(FurnaceStage, furnaceSprites.Length - 1)];
        Place(furnace.transform, anchors.Furnace, BossSpriteLayout.FurnaceSize);

        gear.enabled = GearVisible;
        gear.sprite = gearSprites[((GearStep % gearSprites.Length) + gearSprites.Length) % gearSprites.Length];
        Place(gear.transform, anchors.Gear, BossSpriteLayout.GearSize);

        pilot.enabled = PilotVisible;
        pilot.sprite = pilotSprites[(int)PilotFrame];
        Place(pilot.transform, new Vector2(anchors.Pilot.x + PilotNudge.x, anchors.Pilot.y + PilotDrop + PilotNudge.y), BossSpriteLayout.PilotSize);

        int perSide = links.Length / 2;
        for (int side = 0; side < 2; side++)
        {
            var fist = fists[side];
            fist.enabled = FistsVisible;
            var frame = FistFrame[side];
            fist.sprite = fistSprites[(int)frame];
            // The sheet shows the right-hand fist (and, for the side view, a fist travelling right).
            fist.flipX = frame == BossSpriteLayout.Fist.Side ? FistFacesLeft[side] : side == Left;
            Vector2 pixel = new(Mathf.Round(FistPixel[side].x), Mathf.Round(FistPixel[side].y));
            Place(fist.transform, pixel, BossSpriteLayout.FistSize);

            // Chain: links spread evenly from the shoulder drum to where the chain enters the fist.
            Vector2 from = side == Left ? anchors.DrumLeft : anchors.DrumRight;
            Vector2 to = frame == BossSpriteLayout.Fist.Side
                ? new Vector2(pixel.x + (fist.flipX ? BossSpriteLayout.FistSize.x - 2 : 2), pixel.y + 12)
                : new Vector2(pixel.x + BossSpriteLayout.FistSize.x * .5f, pixel.y + 1);
            for (int i = 0; i < perSide; i++)
            {
                var link = links[side * perSide + i];
                link.enabled = FistsVisible;
                float t = (i + .5f) / perSide;
                Vector2 point = Vector2.Lerp(from, to, t);
                Place(link.transform, new Vector2(Mathf.Round(point.x) - 1, Mathf.Round(point.y) - 2), BossSpriteLayout.ChainSize);
            }
        }
    }

    // Sprites use a centre pivot; `topLeft` is the part's top-left corner in body pixels.
    private static void Place(Transform part, Vector2 topLeft, Vector2Int size)
    {
        part.localPosition = new Vector3(
            (topLeft.x + size.x * .5f - BossSpriteLayout.BodySize.x * .5f) / Ppu,
            (BossSpriteLayout.BodySize.y - topLeft.y - size.y * .5f) / Ppu, 0f);
    }

    private SpriteRenderer Part(string partName, Transform parent, Material material, int order)
    {
        var go = new GameObject(partName);
        go.transform.SetParent(parent, false);
        var renderer = go.AddComponent<SpriteRenderer>();
        if (material != null) renderer.sharedMaterial = material;
        renderer.sortingOrder = order;
        renderer.color = new Color(tone.r * tint.r, tone.g * tint.g, tone.b * tint.b, tint.a);
        return renderer;
    }

    /// <summary>Cuts a horizontal strip into frames (the sheets are imported as one sprite each).</summary>
    public static Sprite[] Slice(Texture2D sheet, Vector2Int frame, int count)
    {
        var sprites = new Sprite[count];
        if (sheet == null) return sprites;
        for (int i = 0; i < count; i++)
        {
            sprites[i] = Sprite.Create(sheet, new Rect(i * frame.x, 0, frame.x, frame.y), new Vector2(.5f, .5f), Ppu, 0, SpriteMeshType.FullRect);
            sprites[i].name = sheet.name + " " + i;
        }
        return sprites;
    }

    private void OnDestroy()
    {
        // Sprites made at runtime are not assets: release them with the boss.
        foreach (var set in new[] { bodySprites, pilotSprites, gearSprites, furnaceSprites, fistSprites, Dust, Shockwave, Slag, Marker, Steam, Spark, Explosion, Shadow })
            if (set != null) foreach (var sprite in set) if (sprite != null) Destroy(sprite);
        if (chainSprite != null) Destroy(chainSprite);
    }
}
