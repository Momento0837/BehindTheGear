using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent, RequireComponent(typeof(CapsuleCollider2D))]
public sealed class PlayerPoisonDaggerProjectile : MonoBehaviour
{
    private Vector2 direction;
    private float speed, length, width, expiresAt, poisonDuration, poisonInterval, cameraMargin, launchFloor;
    private int directDamage, poisonDamage, enemyMask, obstacleMask;
    private bool initialized, consumed, ignoreWalkableGround;
    private Transform owner;
    private Camera gameplayCamera;
    private PlayerMapBounds2D mapBounds;
    private Material trailMaterial;
    private readonly List<RaycastHit2D> hits = new();
    private readonly List<Vector2> points = new();
    private readonly Dictionary<Sprite, Vector2[][]> shapes = new();
    public Vector2 Direction => direction;
    public float Speed => speed;
    public bool IsConsumed => consumed;

    public static PlayerPoisonDaggerProjectile Launch(Vector2 position, Vector2 heading, GameObject source,
        PoisonDaggerSettings settings, int hitDamage, int tickDamage)
    {
        var projectile = settings.projectilePrefab != null ? Instantiate(settings.projectilePrefab)
            : new GameObject("Poison Dagger", typeof(CapsuleCollider2D)).AddComponent<PlayerPoisonDaggerProjectile>();
        if (source != null && source.scene.GetPhysicsScene2D() != Physics2D.defaultPhysicsScene)
            SceneManager.MoveGameObjectToScene(projectile.gameObject, source.scene);
        projectile.Initialize(position, heading, source, settings, hitDamage, tickDamage);
        return projectile;
    }
    private void Initialize(Vector2 position, Vector2 heading, GameObject source, PoisonDaggerSettings settings, int hitDamage, int tickDamage)
    {
        direction = heading.sqrMagnitude > .001f ? heading.normalized : Vector2.right;
        speed = Mathf.Max(.01f, settings.projectileSpeed);
        length = Mathf.Max(.01f, settings.daggerLength); width = Mathf.Clamp(settings.hitboxWidth, .01f, length);
        directDamage = hitDamage; poisonDamage = tickDamage;
        poisonDuration = settings.poisonDuration; poisonInterval = settings.poisonTickInterval;
        expiresAt = Time.time + Mathf.Max(.01f, settings.maxLifetime);
        enemyMask = settings.enemyLayers; obstacleMask = settings.obstacleLayers;
        ignoreWalkableGround = settings.ignoreWalkableGround;
        cameraMargin = Mathf.Max(0, settings.cameraDespawnMargin);
        owner = source != null ? source.transform : null;
        mapBounds = source != null ? source.GetComponent<PlayerMapBounds2D>() : null;
        var sourceBox = source != null ? source.GetComponent<Collider2D>() : null;
        launchFloor = sourceBox != null ? sourceBox.bounds.min.y : position.y - 1f;
        gameplayCamera = Camera.main;
        transform.SetPositionAndRotation(position, Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg));
        var collider = GetComponent<CapsuleCollider2D>();
        collider.direction = CapsuleDirection2D.Horizontal; collider.size = new Vector2(length, width);
        // The swept query below owns hits. No pickup/AI sensor callbacks or physics bounces.
        collider.enabled = false;
        if (TryGetComponent<Rigidbody2D>(out var body)) { body.gravityScale = 0; body.simulated = false; }
        var visual = transform.Find("Visual");
        if (visual == null) { visual = new GameObject("Visual", typeof(SpriteRenderer)).transform; visual.SetParent(transform, false); }
        var renderer = visual.GetComponent<SpriteRenderer>();
        if (renderer == null) renderer = visual.gameObject.AddComponent<SpriteRenderer>();
        renderer.sprite = settings.daggerSprite;
        visual.localRotation = Quaternion.Euler(0, 0, -settings.spriteForwardAngle);
        float nativeLength = settings.daggerSprite != null ? settings.daggerSprite.bounds.size.magnitude : 1;
        visual.localScale = Vector3.one * (length / Mathf.Max(.001f, nativeLength));
        var sourceSprite = source != null ? source.GetComponent<SpriteRenderer>() : null;
        if (sourceSprite != null) { renderer.sharedMaterial = sourceSprite.sharedMaterial; renderer.sortingLayerID = sourceSprite.sortingLayerID; }
        renderer.sortingOrder = sourceSprite != null ? sourceSprite.sortingOrder + 5 : 5;
        if (settings.showTrail)
        {
            var trail = GetComponent<TrailRenderer>();
            if (trail == null) trail = gameObject.AddComponent<TrailRenderer>();
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            trailMaterial = new Material(shader); trail.sharedMaterial = trailMaterial;
            trail.time = .12f; trail.minVertexDistance = .03f; trail.startWidth = .035f; trail.endWidth = 0;
            trail.startColor = new Color(.3f, .9f, .4f, .35f); trail.endColor = new Color(.2f, .7f, .3f, 0);
            trail.sortingLayerID = renderer.sortingLayerID; trail.sortingOrder = renderer.sortingOrder - 1;
        }
        consumed = false; initialized = true;
    }
    private void FixedUpdate()
    {
        if (!initialized || consumed) return;
        if (ShouldDespawn()) { Consume(); return; }
        float travel = speed * Time.fixedDeltaTime;
        float radius = width * .5f, halfSegment = Mathf.Max(0, length * .5f - radius);
        Vector2 start = transform.position;
        Vector2 sweepOrigin = start - direction * halfSegment;
        Vector2 sweep = direction * (travel + halfSegment * 2);
        var filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(enemyMask | obstacleMask);
        gameObject.scene.GetPhysicsScene2D().CircleCast(sweepOrigin, radius, direction, sweep.magnitude, filter, hits);
        float earliest = float.PositiveInfinity;
        PoisonDaggerTarget victim = default;
        foreach (var hit in hits)
        {
            var collider = hit.collider;
            if (collider == null || owner != null && (collider.transform == owner || collider.transform.IsChildOf(owner))
                || PoisonDaggerTarget.IsFriendly(collider.transform)) continue;
            float fraction;
            PoisonDaggerTarget candidate = default;
            if (PoisonDaggerTarget.TryResolve(collider, enemyMask, out candidate))
            {
                fraction = PoisonDaggerGeometry.Sweep(candidate.Component.transform, sweepOrigin, sweep, radius, shapes, points);
                if (float.IsPositiveInfinity(fraction)) continue;
                fraction = fraction < 0 ? hit.distance / sweep.magnitude : fraction;
            }
            else
            {
                if (!Blocks(collider)) continue;
                fraction = hit.distance / sweep.magnitude;
            }
            float time = Mathf.Clamp01((fraction * sweep.magnitude - halfSegment * 2) / travel);
            if (time < earliest) { earliest = time; victim = candidate; }
        }
        if (!float.IsPositiveInfinity(earliest))
        {
            transform.position = start + direction * (travel * earliest);
            consumed = true; // Guard before invoking any receiver callbacks.
            if (victim.IsAlive)
            {
                victim.Damage(directDamage, true, direction);
                PoisonStatus2D.Apply(victim, poisonDamage, poisonDuration, poisonInterval);
            }
            Destroy(gameObject); return;
        }
        transform.position = start + direction * travel;
    }
    private bool Blocks(Collider2D collider)
    {
        if ((obstacleMask & (1 << collider.gameObject.layer)) == 0 || collider.isTrigger) return false;
        var marker = collider.GetComponentInParent<PoisonDaggerSurface2D>();
        if (marker != null) return marker.kind == PoisonDaggerSurface2D.SurfaceKind.Obstacle;
        if (collider.GetComponent<PlatformEffector2D>() != null) return false;
        // Current Ground is shared by side-view floors and walls. Only low, wide walkable slabs are floors.
        var bounds = collider.bounds;
        if (ignoreWalkableGround && collider.gameObject.layer == LayerMask.NameToLayer("Ground")
            && bounds.size.x >= bounds.size.y * 4 && bounds.max.y <= launchFloor + .12f) return false;
        return true;
    }
    private bool ShouldDespawn()
    {
        if (Time.time >= expiresAt) return true;
        Vector3 position = transform.position;
        if (mapBounds != null && mapBounds.HasHorizontalBounds
            && (position.x < mapBounds.HorizontalBounds.x - length * .5f || position.x > mapBounds.HorizontalBounds.y + length * .5f)) return true;
        if (gameplayCamera == null || !gameplayCamera.isActiveAndEnabled) return false;
        Vector3 viewport = gameplayCamera.WorldToViewportPoint(position);
        if (viewport.z <= 0) return true;
        float height = gameplayCamera.orthographic ? gameplayCamera.orthographicSize * 2
            : 2 * viewport.z * Mathf.Tan(gameplayCamera.fieldOfView * .5f * Mathf.Deg2Rad);
        float marginY = (cameraMargin + length * .5f) / Mathf.Max(.01f, height);
        float marginX = marginY / Mathf.Max(.01f, gameplayCamera.aspect);
        return viewport.x < -marginX || viewport.x > 1 + marginX || viewport.y < -marginY || viewport.y > 1 + marginY;
    }
    private void Consume() { if (consumed) return; consumed = true; Destroy(gameObject); }
    private void OnDestroy() { if (trailMaterial != null) Destroy(trailMaterial); }
}
