using UnityEngine;

/// <summary>Rolls MonsterRewardTable for a defeated monster and scatters the pickups. Called from MonsterFollowController.Die.</summary>
public static class MonsterRewards
{
    private static MonsterRewardTable table;

    public static void Drop(Vector3 position, MonsterRank rank)
    {
        if (table == null) table = Resources.Load<MonsterRewardTable>(MonsterRewardTable.ResourcePath);
        var entry = table != null ? table.For(rank) : null;
        if (entry == null) return;

        // EXP drops with the same pop-and-fall as coins; the amount depends on the monster's rank.
        // A player already at the maximum level gets none (it could never be used).
        var playerLevel = Object.FindFirstObjectByType<PlayerLevel>();
        if (playerLevel == null || !playerLevel.IsMaxLevel) Scatter(RewardPickup.Kind.Experience, Roll(table.ExperienceFor(entry)), position, table.maxExperiencePickups, 2);
        if (Random.value < entry.coinChance)
            Scatter(RewardPickup.Kind.Coin, Roll(entry.coins), position, table.maxCoinPickups);
        if (Random.value < entry.actionPointChance)
            Scatter(RewardPickup.Kind.ActionPoint, Roll(entry.actionPoints), position, 3);
        if (table.basePart != null && Random.value < entry.basePartChance)
        {
            // Same path as the existing monster loot: an inventory item pickup.
            var drop = new GameObject("Monster Loot");
            drop.transform.position = position + new Vector3(Random.Range(-.4f, .4f), .5f, 0f);
            drop.AddComponent<WorldItemPickup>().Configure(table.basePart, Roll(entry.baseParts));
        }
    }

    private static int Roll(Vector2Int range) => Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y) + 1);

    // Splits `total` over up to `maxPickups` pickups that pop up and fall around the monster.
    // `minimumValue` keeps tiny amounts together (2-3 EXP from a weak monster is one shard, not three).
    private static void Scatter(RewardPickup.Kind kind, int total, Vector3 position, int maxPickups, int minimumValue = 1)
    {
        if (total <= 0) return;
        int count = Mathf.Clamp(total / Mathf.Max(1, minimumValue), 1, Mathf.Max(1, maxPickups));
        for (int i = 0; i < count; i++)
        {
            int value = total / count + (i < total % count ? 1 : 0);
            var velocity = new Vector2(Random.Range(-2.2f, 2.2f), Random.Range(5f, 7.5f));
            RewardPickup.Spawn(kind, value, position + new Vector3(0f, .6f, 0f), velocity);
        }
    }
}
