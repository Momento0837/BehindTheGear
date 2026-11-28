using UnityEngine;

/// <summary>
/// The only place the boss can be hurt: Bartol in his bucket. A solid (non-trigger) box on the Monster layer, because
/// the player's melee query ignores triggers; it excludes every layer from contacts so it never pushes anything.
/// The box follows the pilot sprite, and grows to the whole bucket while the machine is overheated.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public sealed class BossWeakPoint : MonoBehaviour, ICombatHitReceiver
{
    private const int MonsterLayer = 9;
    private BossGoliath boss;
    private BoxCollider2D box;

    public static BossWeakPoint Create(BossGoliath owner, Transform pilot)
    {
        var go = new GameObject("Weak Point") { layer = MonsterLayer };
        go.transform.SetParent(pilot, false);
        var box = go.AddComponent<BoxCollider2D>();
        box.isTrigger = false;
        box.excludeLayers = ~0; // found by overlap queries, but makes no contacts
        var point = go.AddComponent<BossWeakPoint>();
        point.boss = owner;
        point.box = box;
        return point;
    }

    /// <summary>`rect` is in pixels of the 52x40 pilot frame (origin top-left, y down).</summary>
    public void SetArea(RectInt rect)
    {
        float ppu = BossSpriteLayout.PixelsPerUnit;
        Vector2Int frame = BossSpriteLayout.PilotSize;
        box.size = new Vector2(rect.width / ppu, rect.height / ppu);
        box.offset = new Vector2((rect.x + rect.width * .5f - frame.x * .5f) / ppu, (frame.y * .5f - rect.y - rect.height * .5f) / ppu);
    }

    public Bounds Bounds => box.bounds;

    public void ReceiveHit(CombatHit hit) { if (boss != null) boss.TakeHit(hit.Damage); }
    public void ReceiveMeleeDamage(int damage) { if (boss != null) boss.TakeHit(damage); }
}
