using System.Collections.Generic;
using UnityEngine;

internal readonly struct PoisonDaggerTarget
{
    public readonly MonoBehaviour Component;
    private readonly MonsterFollowController monster;
    private readonly IMeleeDamageReceiver receiver;
    private PoisonDaggerTarget(MonoBehaviour component, MonsterFollowController enemy, IMeleeDamageReceiver damageReceiver)
    { Component = component; monster = enemy; receiver = damageReceiver; }
    public bool IsAlive => Component != null && Component.isActiveAndEnabled
        && !IsFriendly(Component.transform) && (monster == null || monster.CurrentHealth > 0);

    public static bool IsFriendly(Transform source)
    {
        if (source.GetComponentInParent<PlayerHealth>() != null) return true;
        int player = LayerMask.NameToLayer("Player"), ally = LayerMask.NameToLayer("Ally"), friendly = LayerMask.NameToLayer("Friendly");
        for (var parent = source; parent != null; parent = parent.parent)
            if (parent.gameObject.layer == player || ally >= 0 && parent.gameObject.layer == ally
                || friendly >= 0 && parent.gameObject.layer == friendly) return true;
        return false;
    }
    public static bool TryResolve(Collider2D collider, int enemyMask, out PoisonDaggerTarget target)
    {
        target = default;
        if (collider == null || collider.isTrigger || IsFriendly(collider.transform)) return false;
        var monster = collider.GetComponentInParent<MonsterFollowController>();
        if (monster != null)
        {
            if ((enemyMask & ((1 << monster.gameObject.layer) | (1 << collider.gameObject.layer))) == 0) return false;
            target = new PoisonDaggerTarget(monster, monster, null); return target.IsAlive;
        }
        foreach (var component in collider.GetComponentsInParent<MonoBehaviour>())
            if (component is IMeleeDamageReceiver receiver
                && (enemyMask & ((1 << component.gameObject.layer) | (1 << collider.gameObject.layer))) != 0)
            { target = new PoisonDaggerTarget(component, null, receiver); return target.IsAlive; }
        return false;
    }
    public void Damage(int amount, bool impact, Vector2 direction)
    {
        if (!IsAlive || amount <= 0) return;
        if (monster != null)
        {
            if (impact) monster.TakeMeleeDamage(amount, direction.x < 0 ? -1 : 1);
            else monster.TakeDamage(amount);
        }
        else if (receiver is ICombatHitReceiver detailed) detailed.ReceiveHit(new CombatHit(amount));
        else receiver?.ReceiveMeleeDamage(amount);
    }
}

/// <summary>Uses the same swept-polygon primitive as grenades, avoiding oversized movement boxes.</summary>
internal static class PoisonDaggerGeometry
{
    public static float Sweep(Transform target, Vector2 origin, Vector2 step, float radius,
        Dictionary<Sprite, Vector2[][]> cache, List<Vector2> points)
    {
        float earliest = float.PositiveInfinity;
        bool geometry = false;
        foreach (var polygon in target.GetComponentsInChildren<PolygonCollider2D>())
        {
            if (!polygon.enabled || polygon.isTrigger) continue;
            geometry = true;
            for (int path = 0; path < polygon.pathCount; path++)
            {
                points.Clear();
                foreach (var point in polygon.GetPath(path)) points.Add(polygon.transform.TransformPoint(point + polygon.offset));
                earliest = Mathf.Min(earliest, GrenadeImpactGeometry.SweepPolygon(origin, step, radius, points));
            }
        }
        if (geometry) return earliest;
        var main = target.GetComponent<SpriteRenderer>();
        var renderers = main != null ? new[] { main } : target.GetComponentsInChildren<SpriteRenderer>();
        foreach (var renderer in renderers)
        {
            if (!renderer.enabled || renderer.sprite == null || renderer.color.a <= .001f) continue;
            geometry = true;
            if (!cache.TryGetValue(renderer.sprite, out var shapes))
            {
                var paths = new List<Vector2[]>();
                for (int i = 0; i < renderer.sprite.GetPhysicsShapeCount(); i++)
                { renderer.sprite.GetPhysicsShape(i, points); paths.Add(points.ToArray()); }
                if (paths.Count == 0)
                {
                    var vertices = renderer.sprite.vertices; var triangles = renderer.sprite.triangles;
                    for (int i = 0; i < triangles.Length; i += 3)
                        paths.Add(new[] { vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]] });
                }
                cache[renderer.sprite] = shapes = paths.ToArray();
            }
            foreach (var shape in shapes)
            {
                points.Clear();
                foreach (var p in shape) points.Add(renderer.transform.TransformPoint(new Vector2(renderer.flipX ? -p.x : p.x, renderer.flipY ? -p.y : p.y)));
                earliest = Mathf.Min(earliest, GrenadeImpactGeometry.SweepPolygon(origin, step, radius, points));
            }
        }
        return geometry ? earliest : -1f; // No visible geometry: use the authored physical hitbox.
    }
}
