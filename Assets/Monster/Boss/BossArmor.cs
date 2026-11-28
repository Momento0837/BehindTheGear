using UnityEngine;

/// <summary>
/// The parts of the boss that cannot be hurt: the boiler and the two fists. Each gets a solid box on the Monster layer
/// (found by the player's attacks, never pushing anything) that answers a hit with sparks, a clank and a "0" instead
/// of damage, so the player learns that only Bartol himself counts.
/// Daggers stop on armour like on anything else they hit; grenades and the shockwave reach past it by their radius.
/// </summary>
public sealed class BossArmor : MonoBehaviour, ICombatHitReceiver
{
    private const int MonsterLayer = 9;
    private BossGoliath boss;
    private BoxCollider2D box;

    public static BossArmor Create(BossGoliath boss, BossGoliathRig rig)
    {
        float ppu = BossSpriteLayout.PixelsPerUnit;
        // Boiler: the hull only (body pixels 18..94 x 40..76), below the bucket so it never covers the weak point.
        var hull = Plate("Armor - Boiler", boss, boss.transform, new Vector2(76f / ppu, 36f / ppu));
        hull.transform.localPosition = new Vector3(0f, (BossSpriteLayout.BodySize.y - 58f) / ppu, 0f);
        for (int side = 0; side < 2; side++)
            Plate("Armor - Fist", boss, rig.FistTransform(side), new Vector2((BossSpriteLayout.FistSize.x - 4) / ppu, (BossSpriteLayout.FistSize.y - 6) / ppu));
        return hull;
    }

    /// <summary>Switches this plate on or off as a target.</summary>
    public void SetBlocking(bool on) { if (box != null) box.enabled = on; }

    private static BossArmor Plate(string plateName, BossGoliath boss, Transform parent, Vector2 size)
    {
        var go = new GameObject(plateName) { layer = MonsterLayer };
        go.transform.SetParent(parent, false);
        var armor = go.AddComponent<BossArmor>();
        armor.boss = boss;
        armor.box = go.AddComponent<BoxCollider2D>();
        armor.box.size = size;
        armor.box.isTrigger = false;
        armor.box.excludeLayers = ~0; // overlap queries find it; it makes no contacts
        return armor;
    }

    public void ReceiveHit(CombatHit hit) => Struck();
    public void ReceiveMeleeDamage(int damage) => Struck();

    private void Struck()
    {
        if (boss == null) return;
        // Sparks on the side of the plate that faces the player.
        Bounds bounds = box.bounds;
        var player = PlayerTargetRegistry.Current;
        float x = player != null ? Mathf.Clamp(player.position.x, bounds.min.x, bounds.max.x) : bounds.center.x;
        float y = player != null ? Mathf.Clamp(player.position.y + .6f, bounds.min.y, bounds.max.y) : bounds.center.y;
        boss.ArmorStruck(new Vector3(x, y, 0f));
    }
}
