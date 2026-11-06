using System.Collections.Generic;
using UnityEngine;

/// <summary>Physical projectile with one timed area hit. Does not own ammo, rewards or enemy AI.</summary>
public sealed class PlayerGrenadeProjectile : MonoBehaviour
{
    private Rigidbody2D body;
    private CircleCollider2D hitbox;
    private SpriteRenderer visual;
    private float detonateAt, radius, selfRadius;
    private int damage, selfPower;
    private bool initialized, exploded;
    private Texture2D generatedTexture;
    private Sprite generatedSprite;
    private PhysicsMaterial2D bounce;
    private readonly List<Collider2D> overlaps = new();
    private readonly HashSet<MonsterFollowController> monsters = new();
    private readonly HashSet<IMeleeDamageReceiver> receivers = new();
    private readonly HashSet<PlayerHealth> players = new();

    public static PlayerGrenadeProjectile Launch(Vector2 position, Vector2 velocity, float gravityScale,
        float fuseSeconds, int damage, float radius, int selfPower, float selfRadius, GameObject owner, Sprite sprite = null)
    {
        var go = new GameObject("Thrown Grenade", typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CircleCollider2D));
        var grenade = go.AddComponent<PlayerGrenadeProjectile>();
        grenade.transform.position = position;
        grenade.body = go.GetComponent<Rigidbody2D>(); grenade.hitbox = go.GetComponent<CircleCollider2D>();
        grenade.visual = go.GetComponent<SpriteRenderer>();
        grenade.damage = damage; grenade.radius = radius; grenade.selfPower = selfPower; grenade.selfRadius = selfRadius;
        grenade.detonateAt = Time.time + fuseSeconds; grenade.hitbox.radius = .14f;
        grenade.bounce = new PhysicsMaterial2D("Temporary grenade bounce") { bounciness = .3f, friction = .65f };
        grenade.hitbox.sharedMaterial = grenade.bounce;
        grenade.body.gravityScale = gravityScale; grenade.body.mass = .2f;
        grenade.body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        grenade.body.interpolation = RigidbodyInterpolation2D.Interpolate;
        grenade.body.linearVelocity = velocity; grenade.body.angularVelocity = -Mathf.Sign(velocity.x) * 240;
        var source = owner != null ? owner.GetComponent<SpriteRenderer>() : null;
        if (source != null)
        {
            grenade.visual.sharedMaterial = source.sharedMaterial;
            grenade.visual.sortingLayerID = source.sortingLayerID;
            grenade.visual.sortingOrder = source.sortingOrder + 4;
        }
        else grenade.visual.sortingOrder = 4;
        grenade.visual.sprite = sprite != null ? sprite : grenade.CreatePlaceholderSprite();
        if (owner != null)
            foreach (var collider in owner.GetComponentsInChildren<Collider2D>()) Physics2D.IgnoreCollision(grenade.hitbox, collider);
        grenade.initialized = true;
        return grenade;
    }
    private void Update()
    {
        if (!initialized || exploded) return;
        visual.color = Time.time >= detonateAt - .4f && Mathf.FloorToInt(Time.time * 18) % 2 == 0 ? new Color(1, .55f, .3f) : Color.white;
        if (Time.time >= detonateAt) Explode();
    }
    public void Explode()
    {
        if (!initialized || exploded) return;
        exploded = true; hitbox.enabled = false; body.simulated = false;
        Vector2 center = transform.position;
        var filter = new ContactFilter2D { useTriggers = false };
        Physics2D.OverlapCircle(center, radius, filter, overlaps);
        foreach (var hit in overlaps)
        {
            var health = hit.GetComponentInParent<PlayerHealth>();
            if (health != null)
            {
                if (Vector2.Distance(hit.ClosestPoint(center), center) <= selfRadius && players.Add(health)) health.TakeDamage(selfPower);
                continue;
            }
            var monster = hit.GetComponentInParent<MonsterFollowController>();
            if (monster != null)
            {
                if (monster.isActiveAndEnabled && monster.CurrentHealth > 0 && monsters.Add(monster))
                    monster.TakeMeleeDamage(damage, monster.transform.position.x < center.x ? -1 : 1);
                continue;
            }
            foreach (var component in hit.GetComponentsInParent<MonoBehaviour>())
                if (component is IMeleeDamageReceiver receiver)
                {
                    if (receivers.Add(receiver))
                    {
                        if (receiver is ICombatHitReceiver detailed) detailed.ReceiveHit(new CombatHit(damage));
                        else receiver.ReceiveMeleeDamage(damage);
                    }
                    break;
                }
        }
        PlayerSkillPulse.Create(center, radius, new Color(1, .55f, .16f, .95f), visual);
        PlayerSkillPulse.Create(center, radius * .55f, new Color(1, .88f, .4f, .8f), visual);
        Destroy(gameObject);
    }
    private Sprite CreatePlaceholderSprite()
    {
        const int size = 32;
        generatedTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 13));
            Color color = d <= 11 ? (d > 9 ? new Color(.16f, .18f, .11f) : new Color(.48f, .52f, .24f)) : Color.clear;
            if (y >= 22 && y <= 27 && x >= 12 && x <= 19) color = new Color(.38f, .33f, .2f);
            if (y >= 27 && y <= 30 && x >= 18 && x <= 21) color = new Color(1, .64f, .15f);
            generatedTexture.SetPixel(x, y, color);
        }
        generatedTexture.Apply();
        generatedSprite = Sprite.Create(generatedTexture, new Rect(0, 0, size, size), Vector2.one * .5f, 80);
        return generatedSprite;
    }
    private void OnDestroy()
    {
        if (generatedSprite != null) Destroy(generatedSprite);
        if (generatedTexture != null) Destroy(generatedTexture);
        if (bounce != null) Destroy(bounce);
    }
}
