using UnityEngine;

/// <summary>
/// Coin / AP / EXP pickup dropped by monsters. Pops out, falls and rests on the Ground layer only (never pushes the
/// player or monsters), and is collected when the player touches it (no magnet). Coins and AP spin around their vertical
/// axis and go to the player's PlayerFieldWallet, which hands them to the base (HideOutPlayerBridge).
/// EXP is a softly pulsing violet shard that goes to PlayerLevel (the ring next to the player's head).
/// </summary>
public sealed class RewardPickup : MonoBehaviour
{
    public enum Kind { Coin, ActionPoint, Experience }

    private const float PixelsPerUnit = 11f;
    private const float PickupDelay = .35f;     // let it pop out before it can be collected
    private const float PickupPadding = .2f;    // extra reach around the player's body
    private const float Lifetime = 60f;
    private const float BlinkTime = 4f;
    private const float TurnsPerSecond = 1.1f;

    private static readonly Color ExperienceColor = new(.72f, .61f, 1f);
    private const int LargeExperience = 5;      // EXP pickups worth this much or more use the bigger orb

    private static Sprite coinSprite;
    private static Sprite actionPointSprite;
    private static Sprite experienceShardSprite;
    private static Sprite experienceOrbSprite;
    private static PhysicsMaterial2D bouncy;

    private Kind kind;
    private int amount;
    private SpriteRenderer art;
    private float age;
    private bool collected;

    public static RewardPickup Spawn(Kind kind, int amount, Vector3 position, Vector2 velocity)
    {
        var go = new GameObject(kind == Kind.Coin ? "Coin Drop" : kind == Kind.ActionPoint ? "AP Drop" : "EXP Drop");
        go.transform.position = position;

        var body = go.AddComponent<Rigidbody2D>();
        body.gravityScale = 2.5f;
        body.freezeRotation = true;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.linearVelocity = velocity;

        var solid = go.AddComponent<CircleCollider2D>();
        solid.radius = .2f;
        if (bouncy == null) bouncy = new PhysicsMaterial2D("Reward Pickup") { bounciness = .3f, friction = .9f };
        solid.sharedMaterial = bouncy;
        int ground = LayerMask.NameToLayer("Ground");
        if (ground >= 0) solid.excludeLayers = ~(1 << ground); // lands on the floor, passes through everything else

        var visual = new GameObject("Art");
        visual.transform.SetParent(go.transform, false);
        var art = visual.AddComponent<SpriteRenderer>();
        art.sprite = kind == Kind.Coin ? CoinSprite()
            : kind == Kind.ActionPoint ? ActionPointSprite()
            : ExperienceSprite(Mathf.Max(1, amount) >= LargeExperience);
        art.sortingOrder = 20;
        var material = Resources.Load<Material>("Effects/WorldItemForeground"); // same look as other world items
        if (material != null) art.sharedMaterial = material;

        var pickup = go.AddComponent<RewardPickup>();
        pickup.kind = kind;
        pickup.amount = Mathf.Max(1, amount);
        pickup.art = art;
        return pickup;
    }

    private void Update()
    {
        if (collected) return;
        age += Time.deltaTime;

        float shade;
        if (kind == Kind.Experience)
        {
            // EXP does not spin like a coin: it breathes (size and brightness) so it reads as energy, not money.
            float pulse = .5f + .5f * Mathf.Sin(age * 5.5f);
            art.transform.localScale = Vector3.one * Mathf.Lerp(.86f, 1.12f, pulse);
            shade = Mathf.Lerp(.82f, 1f, pulse);
        }
        else
        {
            // Spin around the vertical axis: squash X with cos and darken as it turns edge-on.
            float c = Mathf.Cos(age * TurnsPerSecond * Mathf.PI * 2f);
            art.transform.localScale = new Vector3(Mathf.Max(.12f, Mathf.Abs(c)), 1f, 1f);
            shade = Mathf.Lerp(.7f, 1f, Mathf.Abs(c));
        }
        float alpha = 1f;
        if (age > Lifetime - BlinkTime) alpha = Mathf.Repeat(age * 6f, 1f) < .5f ? 1f : .25f;
        art.color = new Color(shade, shade, shade, alpha);
        if (age >= Lifetime) { Destroy(gameObject); return; }

        if (age < PickupDelay || SceneFadeTransition.IsRunning) return;
        var player = PlayerTargetRegistry.Current;
        if (player == null) return;
        var health = player.GetComponent<PlayerHealth>();
        if (health != null && health.IsDead) return;
        if (!Touches(player)) return;
        Collect(player);
    }

    private bool Touches(Transform player)
    {
        foreach (var body in player.GetComponents<Collider2D>())
        {
            if (!body.enabled || body.isTrigger) continue;
            var bounds = body.bounds;
            bounds.Expand(new Vector3(PickupPadding * 2f, PickupPadding * 2f, 0f));
            if (bounds.Contains(new Vector3(transform.position.x, transform.position.y, bounds.center.z))) return true;
        }
        return Vector2.Distance(player.position, transform.position) < .6f; // fallback when the body collider is elsewhere
    }

    private void Collect(Transform player)
    {
        collected = true;
        var inventory = player.GetComponentInParent<PlayerInventory>();
        if (inventory == null) inventory = player.GetComponentInChildren<PlayerInventory>();
        var owner = inventory != null ? inventory.gameObject : player.gameObject; // HideOutPlayerBridge lives next to PlayerInventory
        if (kind == Kind.Experience)
        {
            // Counted at once (never lost to a scene change); the badge only animates the shard flying into the ring.
            var level = PlayerLevel.EnsureOn(owner);
            bool usable = !level.IsMaxLevel; // a shard dropped just before reaching the maximum level is simply absorbed
            level.AddExperience(amount, transform.position);
            if (usable) MonsterDamageNumbers.ShowLabel(transform.position + new Vector3(0f, .35f, 0f), "EXP +" + amount, ExperienceColor, .8f);
            Destroy(gameObject);
            return;
        }
        var wallet = owner.GetComponent<PlayerFieldWallet>();
        if (wallet == null) wallet = owner.AddComponent<PlayerFieldWallet>();
        wallet.Add(kind, amount);
        bool coin = kind == Kind.Coin;
        MonsterDamageNumbers.ShowLabel(transform.position + new Vector3(0f, .35f, 0f),
            coin ? "+" + amount : "AP +" + amount,
            coin ? new Color(1f, .86f, .38f) : new Color(.55f, .86f, 1f), .8f);
        Destroy(gameObject);
    }

    private static Sprite CoinSprite()
    {
        if (coinSprite != null) return coinSprite;
        Color32 o = new(110, 70, 18, 255), r = new(176, 118, 30, 255), g = new(238, 186, 58, 255),
            s = new(255, 244, 178, 255), m = new(202, 140, 38, 255), t0 = new(0, 0, 0, 0);
        coinSprite = Build("Coin", new[,]
        {
            { t0, t0, o, o, o, t0, t0 },
            { t0, o, r, g, r, o, t0 },
            { o, r, s, g, g, r, o },
            { o, g, g, m, g, g, o },
            { o, r, g, m, g, r, o },
            { t0, o, r, g, r, o, t0 },
            { t0, t0, o, o, o, t0, t0 },
        });
        return coinSprite;
    }

    private static Sprite ActionPointSprite()
    {
        if (actionPointSprite != null) return actionPointSprite;
        Color32 o = new(22, 58, 82, 255), d = new(46, 120, 170, 255), b = new(96, 196, 236, 255),
            s = new(220, 248, 255, 255), t0 = new(0, 0, 0, 0);
        actionPointSprite = Build("Action Point", new[,]
        {
            { t0, t0, t0, o, t0, t0, t0 },
            { t0, t0, o, s, o, t0, t0 },
            { t0, o, s, b, b, o, t0 },
            { o, b, b, b, d, d, o },
            { t0, o, b, d, d, o, t0 },
            { t0, t0, o, d, o, t0, t0 },
            { t0, t0, t0, o, t0, t0, t0 },
        });
        return actionPointSprite;
    }

    private static Sprite ExperienceSprite(bool large)
    {
        Color32 o = new(60, 38, 112, 255), d = new(124, 92, 214, 255), b = new(183, 155, 255, 255),
            s = new(240, 230, 255, 255), t0 = new(0, 0, 0, 0);
        if (large)
        {
            if (experienceOrbSprite == null) experienceOrbSprite = Build("Experience Orb", new[,]
            {
                { t0, t0, o, o, o, t0, t0 },
                { t0, o, d, b, d, o, t0 },
                { o, d, b, s, b, d, o },
                { o, b, s, s, b, b, o },
                { o, d, b, b, b, d, o },
                { t0, o, d, b, d, o, t0 },
                { t0, t0, o, o, o, t0, t0 },
            });
            return experienceOrbSprite;
        }
        if (experienceShardSprite == null) experienceShardSprite = Build("Experience Shard", new[,]
        {
            { t0, t0, o, t0, t0 },
            { t0, o, s, o, t0 },
            { o, s, b, d, o },
            { t0, o, d, o, t0 },
            { t0, t0, o, t0, t0 },
        });
        return experienceShardSprite;
    }

    // Rows are written top to bottom; textures are bottom-up.
    private static Sprite Build(string spriteName, Color32[,] pixels)
    {
        int h = pixels.GetLength(0), w = pixels.GetLength(1);
        var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = spriteName };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                texture.SetPixel(x, h - 1 - y, pixels[y, x]);
        texture.Apply();
        var sprite = Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(.5f, .5f), PixelsPerUnit);
        sprite.name = spriteName;
        return sprite;
    }
}
