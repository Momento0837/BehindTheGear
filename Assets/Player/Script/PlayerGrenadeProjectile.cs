using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Physical projectile with one timed or impact area hit. Does not own ammo, rewards or enemy AI.</summary>
[DefaultExecutionOrder(10000)]
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
    private int groundLayer;
    private readonly List<Collider2D> ignoredOwnerColliders = new();
    private readonly Dictionary<Collider2D, Transform> impactColliders = new();
    private readonly Dictionary<Transform, List<Collider2D>> impactBodies = new();
    private readonly Dictionary<Transform, SpriteRenderer[]> impactSprites = new();
    private readonly Dictionary<Sprite, Vector2[][]> spriteShapes = new();
    private readonly List<RaycastHit2D> sweepHits = new();
    private readonly List<Vector2> shapePoints = new();
    private readonly List<Vector2> worldPoints = new();
    private Transform thrower;
    private SpriteRenderer[] throwerSprites;
    private bool throwerCleared;
    private readonly List<Collider2D> overlaps = new();
    private readonly HashSet<MonsterFollowController> monsters = new();
    private readonly HashSet<IMeleeDamageReceiver> receivers = new();
    private readonly HashSet<PlayerHealth> players = new();

    public static PlayerGrenadeProjectile Launch(Vector2 position, Vector2 velocity, float gravityScale,
        float fuseSeconds, int damage, float radius, int selfPower, float selfRadius, GameObject owner, Sprite sprite = null)
    {
        var go = new GameObject("Thrown Grenade", typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CircleCollider2D));
        if (owner != null && owner.scene.GetPhysicsScene2D() != Physics2D.defaultPhysicsScene)
            SceneManager.MoveGameObjectToScene(go, owner.scene);
        var grenade = go.AddComponent<PlayerGrenadeProjectile>();
        grenade.transform.position = position;
        grenade.body = go.GetComponent<Rigidbody2D>(); grenade.hitbox = go.GetComponent<CircleCollider2D>();
        grenade.body.position = position;
        grenade.visual = go.GetComponent<SpriteRenderer>();
        grenade.damage = damage; grenade.radius = radius; grenade.selfPower = selfPower; grenade.selfRadius = selfRadius;
        grenade.groundLayer = LayerMask.NameToLayer("Ground");
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
        Physics2D.SyncTransforms();
        grenade.thrower = owner != null ? owner.GetComponentInParent<PlayerHealth>()?.transform ?? owner.transform : null;
        if (grenade.thrower != null)
        {
            var main = grenade.thrower.GetComponent<SpriteRenderer>();
            grenade.throwerSprites = main != null ? new[] { main } : grenade.thrower.GetComponentsInChildren<SpriteRenderer>();
            foreach (var collider in grenade.thrower.GetComponentsInChildren<Collider2D>())
            {
                Physics2D.IgnoreCollision(grenade.hitbox, collider);
                if (!collider.isTrigger) grenade.ignoredOwnerColliders.Add(collider);
            }
        }
        // Only this grenade ignores movement colliders. Other weapons and actors keep theirs.
        foreach (var collider in FindObjectsByType<Collider2D>(FindObjectsSortMode.None)) grenade.RegisterImpactCollider(collider);
        grenade.initialized = true;
        return grenade;
    }
    private void FixedUpdate()
    {
        if (!initialized || exploded) return;
        UpdateThrowerClearance();
        SweepForImpact();
    }
    private void UpdateThrowerClearance()
    {
        if (throwerCleared) return;
        if (thrower == null) { throwerCleared = true; return; }
        // Clear the entire thrower at once. The movement box can be narrower than the sprite.
        foreach (var collider in ignoredOwnerColliders)
        {
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
            var distance = hitbox.Distance(collider);
            if (!distance.isValid || distance.distance <= Physics2D.defaultContactOffset) return;
        }
        float clearance = hitbox.radius + Physics2D.defaultContactOffset;
        foreach (var renderer in throwerSprites)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.sprite == null) continue;
            // Bounds are deliberately conservative for arming only; direct hits still use sprite outlines.
            var bounds = renderer.bounds;
            Vector2 nearest = new(Mathf.Clamp(body.position.x, bounds.min.x, bounds.max.x),
                Mathf.Clamp(body.position.y, bounds.min.y, bounds.max.y));
            if ((body.position - nearest).sqrMagnitude <= clearance * clearance) return;
        }
        throwerCleared = true;
    }
    private void OnCollisionEnter2D(Collision2D collision)
    {
        // A newly enabled/spawned object may not have been registered on the preceding tick.
        if (!initialized || exploded || !RegisterImpactCollider(collision.collider)) return;
        SweepForImpact();
    }

    private bool RegisterImpactCollider(Collider2D collider)
    {
        if (collider == null || collider == hitbox || collider.gameObject.scene.GetPhysicsScene2D() != gameObject.scene.GetPhysicsScene2D()
            || collider.GetComponentInParent<PlayerGrenadeProjectile>() != null) return false;
        if (impactColliders.ContainsKey(collider)) return true;
        Transform target = null;
        Transform layerTarget = null;
        bool terrain = false, targetLayer = false;
        for (var parent = collider.transform; parent != null; parent = parent.parent)
        {
            int layer = parent.gameObject.layer;
            terrain |= layer == groundLayer;
            targetLayer |= layer == LayerMask.NameToLayer("Player") || layer == LayerMask.NameToLayer("Monster")
                || layer == LayerMask.NameToLayer("Interactable");
            if (targetLayer && layerTarget == null) layerTarget = parent;
            foreach (var component in parent.GetComponents<MonoBehaviour>())
                if (component is PlayerHealth || component is MonsterFollowController
                    || component is IMeleeDamageReceiver || component is Interactable2D) { target = parent; break; }
            if (target != null) break;
        }
        // Explicit receivers take priority over a Ground layer on a child collider.
        if (target == null)
        {
            if ((terrain && !targetLayer) || (collider.isTrigger && !targetLayer)) return false;
            target = collider.attachedRigidbody != null ? collider.attachedRigidbody.transform : layerTarget != null ? layerTarget : collider.transform;
        }
        impactColliders.Add(collider, target);
        if (!impactSprites.ContainsKey(target))
        {
            var main = target.GetComponent<SpriteRenderer>();
            impactSprites.Add(target, main != null ? new[] { main } : target.GetComponentsInChildren<SpriteRenderer>());
            impactBodies.Add(target, new List<Collider2D>());
        }
        impactBodies[target].Add(collider);
        if (collider.enabled && collider.gameObject.activeInHierarchy) Physics2D.IgnoreCollision(hitbox, collider);
        return true;
    }

    private void SweepForImpact()
    {
        Vector2 origin = body.position;
        Vector2 step = (body.linearVelocity + Physics2D.gravity * body.gravityScale * Time.fixedDeltaTime) * Time.fixedDeltaTime;
        float distance = step.magnitude, grenadeRadius = hitbox.radius;
        var filter = new ContactFilter2D { useTriggers = true };
        gameObject.scene.GetPhysicsScene2D().CircleCast(origin, grenadeRadius, distance > 0 ? step / distance : Vector2.right, distance, filter, sweepHits);
        float terrainFraction = float.PositiveInfinity;
        foreach (var hit in sweepHits)
        {
            if (hit.collider == hitbox) continue;
            if (!RegisterImpactCollider(hit.collider) && !hit.collider.isTrigger)
                terrainFraction = Mathf.Min(terrainFraction, distance > 0 ? hit.distance / distance : 0);
        }
        float earliest = float.PositiveInfinity;
        foreach (var pair in impactBodies)
        {
            var target = pair.Key;
            if (target == null) continue;
            if (thrower != null && (target == thrower || target.IsChildOf(thrower)) && !throwerCleared) continue;
            bool hasBody = false, hasCollider = false, hasPolygon = false;
            Rigidbody2D targetBody = null;
            foreach (var collider in pair.Value)
            {
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
                Physics2D.IgnoreCollision(hitbox, collider);
                if (collider.GetComponent<MonsterRangeTrigger2D>() != null) continue;
                hasCollider = true;
                if (collider.isTrigger) continue;
                hasBody = true;
                hasPolygon |= collider is PolygonCollider2D;
                targetBody = collider.attachedRigidbody;
            }
            if (!hasCollider) continue;
            float fraction = float.PositiveInfinity;
            Vector2 relativeStep = step;
            if (targetBody != null) relativeStep -= targetBody.linearVelocity * Time.fixedDeltaTime;
            bool hasGeometry = false;
            if (hasPolygon)
            {
                hasGeometry = true;
                foreach (var collider in pair.Value)
                {
                    if (collider is not PolygonCollider2D polygon || !polygon.enabled || polygon.isTrigger || !polygon.gameObject.activeInHierarchy) continue;
                    for (int path = 0; path < polygon.pathCount; path++)
                    {
                        worldPoints.Clear();
                        foreach (var point in polygon.GetPath(path)) worldPoints.Add(polygon.transform.TransformPoint(point + polygon.offset));
                        fraction = Mathf.Min(fraction, GrenadeImpactGeometry.SweepPolygon(origin, relativeStep, grenadeRadius, worldPoints));
                    }
                }
            }
            else foreach (var renderer in impactSprites[target])
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.sprite == null) continue;
                hasGeometry = true;
                foreach (var shape in GetSpriteShapes(renderer.sprite))
                {
                    worldPoints.Clear();
                    foreach (var point in shape)
                        worldPoints.Add(renderer.transform.TransformPoint(new Vector2(renderer.flipX ? -point.x : point.x, renderer.flipY ? -point.y : point.y)));
                    fraction = Mathf.Min(fraction, GrenadeImpactGeometry.SweepPolygon(origin, relativeStep, grenadeRadius, worldPoints));
                }
            }
            // Invisible solid props retain their authored collider; range-only triggers do not.
            if (!hasGeometry && hasBody)
                foreach (var hit in sweepHits) if (!hit.collider.isTrigger && impactColliders.TryGetValue(hit.collider, out var hitTarget) && hitTarget == target)
                    fraction = Mathf.Min(fraction, distance > 0 ? hit.distance / distance : 0);
            earliest = Mathf.Min(earliest, fraction);
        }
        if (earliest > 1 || earliest >= terrainFraction) return;
        body.position = origin + step * earliest;
        transform.position = body.position;
        Physics2D.SyncTransforms();
        Explode();
    }

    private Vector2[][] GetSpriteShapes(Sprite sprite)
    {
        if (spriteShapes.TryGetValue(sprite, out var cached)) return cached;
        int count = sprite.GetPhysicsShapeCount();
        var shapes = new List<Vector2[]>();
        for (int i = 0; i < count; i++) { sprite.GetPhysicsShape(i, shapePoints); shapes.Add(shapePoints.ToArray()); }
        if (shapes.Count == 0)
        {
            // Reuse the imported tight mesh if this sprite has no authored physics outline.
            var vertices = sprite.vertices;
            var triangles = sprite.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
                shapes.Add(new[] { vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]] });
        }
        return spriteShapes[sprite] = shapes.ToArray();
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
        gameObject.scene.GetPhysicsScene2D().OverlapCircle(center, radius, filter, overlaps);
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

/// <summary>Swept circle against imported polygon edges; no texture reads or scene colliders.</summary>
internal static class GrenadeImpactGeometry
{
    public static float SweepPolygon(Vector2 start, Vector2 step, float radius, List<Vector2> polygon)
    {
        if (polygon.Count < 3) return float.PositiveInfinity;
        float earliest = float.PositiveInfinity;
        bool inside = false;
        for (int i = 0, previous = polygon.Count - 1; i < polygon.Count; previous = i++)
        {
            Vector2 a = polygon[previous], b = polygon[i], edge = b - a;
            if ((a.y > start.y) != (b.y > start.y)
                && start.x < (b.x - a.x) * (start.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            float length = edge.magnitude;
            if (length <= Mathf.Epsilon) continue;
            Vector2 tangent = edge / length, normal = new(-tangent.y, tangent.x);
            float along = Vector2.Dot(start - a, tangent);
            Vector2 closest = a + tangent * Mathf.Clamp(along, 0, length);
            if ((start - closest).sqrMagnitude <= radius * radius) return 0;
            earliest = Mathf.Min(earliest, SweepPoint(start - a, step, radius));
            float normalSpeed = Vector2.Dot(step, normal);
            if (Mathf.Abs(normalSpeed) < .000001f) continue;
            float signedDistance = Vector2.Dot(start - a, normal);
            for (int side = -1; side <= 1; side += 2)
            {
                float time = (side * radius - signedDistance) / normalSpeed;
                if (time < 0 || time > 1) continue;
                float projection = along + Vector2.Dot(step, tangent) * time;
                if (projection >= 0 && projection <= length) earliest = Mathf.Min(earliest, time);
            }
        }
        return inside ? 0 : earliest;
    }

    private static float SweepPoint(Vector2 offset, Vector2 step, float radius)
    {
        float a = step.sqrMagnitude;
        if (a < .0000001f) return float.PositiveInfinity;
        float b = Vector2.Dot(offset, step), c = offset.sqrMagnitude - radius * radius;
        float discriminant = b * b - a * c;
        if (discriminant < 0) return float.PositiveInfinity;
        float time = (-b - Mathf.Sqrt(discriminant)) / a;
        return time >= 0 && time <= 1 ? time : float.PositiveInfinity;
    }
}
