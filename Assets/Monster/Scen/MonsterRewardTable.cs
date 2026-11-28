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
/// Every monster also drops experience (EXP) by rank; the player level curve (PlayerLevel) is tuned here too.
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
        [Tooltip("Experience (EXP) dropped (min..max, inclusive). (0, 0) = no EXP. Negative (-1, -1) = the built-in amount for the rank.")]
        public Vector2Int experience = new(-1, -1);
    }

    [Tooltip("기지 부품 item (HideOutScrap, id hideout_scrap). Picked up like other items into the inventory.")]
    public InventoryItemDefinition basePart;
    [Tooltip("Large coin amounts are split over at most this many coin pickups.")]
    [Min(1)] public int maxCoinPickups = 8;
    [Tooltip("Large EXP amounts are split over at most this many EXP pickups.")]
    [Min(1)] public int maxExperiencePickups = 5;

    [Header("Player level (read by PlayerLevel)")]
    [Tooltip("EXP needed to go from level 1 to level 2.")]
    [Min(1)] public int firstLevelExperience = 20;
    [Tooltip("Every further level needs this much more EXP than the one before.")]
    [Min(0)] public int experiencePerLevel = 12;
    [Tooltip("Every further level also multiplies the first-level amount by this (1 = no extra growth).")]
    [Min(1f)] public float experienceGrowth = 1.12f;
    [Min(1)] public int maxLevel = 30;
    [Tooltip("Action points (AP) granted for each level gained.")]
    [Min(0)] public int actionPointsPerLevel = 1;

    [Header("Level badge (ring next to the player's head)")]
    [Tooltip("Badge centre relative to the top-centre of the player's body collider, in world units (11 px = 1 unit).")]
    public Vector2 levelBadgeOffset = new(-.91f, .2f);

    public Entry[] entries =
    {
        new() { rank = MonsterRank.Swarm, coins = new(1, 1), coinChance = .1f, basePartChance = .01f, baseParts = new(1, 1), actionPointChance = .01f, actionPoints = new(1, 1) },
        new() { rank = MonsterRank.Common, coins = new(1, 2), basePartChance = .03f, baseParts = new(1, 1), actionPointChance = .25f, actionPoints = new(1, 1) },
        new() { rank = MonsterRank.Elite, coins = new(4, 7), basePartChance = .3f, baseParts = new(1, 1), actionPointChance = .15f, actionPoints = new(1, 2) },
        new() { rank = MonsterRank.MiniBoss, coins = new(12, 20), basePartChance = 1f, baseParts = new(1, 2), actionPointChance = .5f, actionPoints = new(2, 3) },
        new() { rank = MonsterRank.Boss, coins = new(30, 50), basePartChance = 1f, baseParts = new(3, 5), actionPointChance = 1f, actionPoints = new(3, 5) },
    };

    public Entry For(MonsterRank rank) => entries?.FirstOrDefault(e => e != null && e.rank == rank) ?? entries?.FirstOrDefault();

    /// <summary>EXP range of an entry. Negative (the field default, also for a table saved before EXP existed) uses the
    /// rank default; (0, 0) means the rank drops no EXP.</summary>
    public Vector2Int ExperienceFor(Entry entry)
    {
        if (entry == null) return Vector2Int.zero;
        if (entry.experience.x < 0 && entry.experience.y < 0) return DefaultExperience(entry.rank);
        return new Vector2Int(Mathf.Max(0, entry.experience.x), Mathf.Max(0, entry.experience.y));
    }

    /// <summary>Weak monsters give little EXP; each rank above gives clearly more.</summary>
    public static Vector2Int DefaultExperience(MonsterRank rank) => rank switch
    {
        MonsterRank.Swarm => new Vector2Int(2, 3),
        MonsterRank.Common => new Vector2Int(6, 8),
        MonsterRank.Elite => new Vector2Int(20, 26),
        MonsterRank.MiniBoss => new Vector2Int(60, 80),
        MonsterRank.Boss => new Vector2Int(180, 240),
        _ => new Vector2Int(6, 8),
    };

    /// <summary>EXP needed to leave `level` (1-based). 0 once the maximum level is reached.</summary>
    public int ExperienceToNext(int level)
    {
        if (level >= maxLevel) return 0;
        int steps = Mathf.Max(0, level - 1);
        double need = firstLevelExperience * Math.Pow(Mathf.Max(1f, experienceGrowth), steps) + (double)experiencePerLevel * steps;
        return (int)Math.Min(1_000_000d, Math.Max(1d, Math.Round(need)));
    }
}
