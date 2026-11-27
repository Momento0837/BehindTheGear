using System;
using System.Linq;
using UnityEngine;

/// <summary>How strong / rare a monster is. Higher ranks drop more coins and better upgrade materials.</summary>
public enum MonsterRank
{
    Swarm = 4,    // 물량 잡몹 (살쾡이): 대부분 코인 없음, 가끔 1개
    Common = 0,   // 잡몹
    Elite = 1,    // 중간 몹
    MiniBoss = 2, // 중간 보스
    Boss = 3,     // 보스
}

/// <summary>
/// Facility-upgrade rewards dropped by monsters (Resources/Monsters/RewardTable), per MonsterRank:
/// coins always, the base part (기지 부품, hideout_scrap) and AP by chance. Common monsters drop 1-2 coins and only
/// rarely a base part or AP. The existing cloth/equipment drop in MonsterFollowController is unchanged.
/// </summary>
[CreateAssetMenu(menuName = "BehindTheGear/Monster Reward Table", fileName = "RewardTable")]
public sealed class MonsterRewardTable : ScriptableObject
{
    public const string ResourcePath = "Monsters/RewardTable";

    [Serializable]
    public sealed class Entry
    {
        public MonsterRank rank;
        [Tooltip("Coins dropped (min..max, inclusive).")]
        public Vector2Int coins = new(1, 2);
        [Tooltip("Chance that any coins drop at all.")]
        [Range(0f, 1f)] public float coinChance = 1f;
        [Range(0f, 1f)] public float basePartChance;
        public Vector2Int baseParts = new(1, 1);
        [Range(0f, 1f)] public float actionPointChance;
        public Vector2Int actionPoints = new(1, 1);
    }

    [Tooltip("기지 부품 item (HideOutScrap, id hideout_scrap). Picked up like other items into the inventory.")]
    public InventoryItemDefinition basePart;
    [Tooltip("Large coin amounts are split over at most this many coin pickups.")]
    [Min(1)] public int maxCoinPickups = 8;

    public Entry[] entries =
    {
        new() { rank = MonsterRank.Swarm, coins = new(1, 1), coinChance = .1f, basePartChance = .01f, baseParts = new(1, 1), actionPointChance = .01f, actionPoints = new(1, 1) },
        new() { rank = MonsterRank.Common, coins = new(1, 2), basePartChance = .03f, baseParts = new(1, 1), actionPointChance = .25f, actionPoints = new(1, 1) },
        new() { rank = MonsterRank.Elite, coins = new(4, 7), basePartChance = .3f, baseParts = new(1, 1), actionPointChance = .15f, actionPoints = new(1, 2) },
        new() { rank = MonsterRank.MiniBoss, coins = new(12, 20), basePartChance = 1f, baseParts = new(1, 2), actionPointChance = .5f, actionPoints = new(2, 3) },
        new() { rank = MonsterRank.Boss, coins = new(30, 50), basePartChance = 1f, baseParts = new(3, 5), actionPointChance = 1f, actionPoints = new(3, 5) },
    };

    public Entry For(MonsterRank rank) => entries?.FirstOrDefault(e => e != null && e.rank == rank) ?? entries?.FirstOrDefault();
}
