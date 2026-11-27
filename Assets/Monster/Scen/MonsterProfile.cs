using UnityEngine;

/// <summary>
/// Per-monster tuning data applied to a MonsterFollowController (HP, attack, speed, lunge, animation, body size).
/// The first profile is Resources/Monsters/Wildcat (살쾡이): fast, fragile, weak, attacks often and comes in packs.
/// </summary>
[CreateAssetMenu(menuName = "BehindTheGear/Monster Profile", fileName = "MonsterProfile")]
public sealed class MonsterProfile : ScriptableObject
{
    [Tooltip("Name shown in logs and test messages.")]
    public string displayName = "Monster";

    [Tooltip("잡몹(Common) / 중간 몹(Elite) / 중간 보스(MiniBoss) / 보스(Boss): picks the reward row in Resources/Monsters/RewardTable.")]
    public MonsterRank rank = MonsterRank.Common;

    [Header("Stats")]
    [Min(1)] public int maxHealth = 20;
    [Min(1)] public int attackPower = 8;
    [Min(0f)] public float moveSpeed = 5.5f;
    [Tooltip("Pause after the attack animation before the next attack (GangB uses 0.7).")]
    [Min(.01f)] public float attackRecovery = .6f;

    [Header("Lunge (no wind-up dash toward the player)")]
    public bool lunge = true;
    [Tooltip("Start a lunge when the player is within this horizontal distance.")]
    [Min(0f)] public float lungeTriggerDistance = 4.5f;
    [Min(0f)] public float lungeSpeed = 12f;
    [Min(0f)] public float lungeDuration = .22f;
    [Min(0f)] public float lungeCooldown = 2.5f;

    [Header("Look and Body")]
    [Tooltip("Override controller that keeps the shared Idle/Walk/Attack/Damaged/Dead state names.")]
    public RuntimeAnimatorController animatorController;
    [Tooltip("Multiplies the cloned monster's scale (the Forest monster is 0.9). Colliders shrink with it.")]
    [Min(.1f)] public float visualScale = 1f;
    [Tooltip("Show a small HP bar above the monster (MonsterHealthBar).")]
    public bool showHealthBar;
    [Tooltip("SpriteRenderer sorting order. Smaller monsters use higher numbers so they draw in front (GangB in the scenes is 1).")]
    public int sortingOrder = 1;

    [Header("Crowding")]
    [Tooltip("Do not push through other monsters: wait behind a monster that is closer to the player instead of overlapping it.")]
    public bool keepSpacing;
    [Tooltip("Minimum horizontal gap to another monster ahead (world units).")]
    [Min(0f)] public float spacing = 1.6f;
    public Vector2 bodySize = new(2.6f, 1.8f);
    public Vector2 bodyOffset = new(0f, -1.1f);
    public Vector2 attackRangeSize = new(4f, 3f);
    public Vector2 attackRangeOffset = new(0f, -.6f);

    [Header("Field placement (WildcatFieldSpawner)")]
    [Tooltip("How many are placed in the Forest when combat starts. 0 = none.")]
    [Min(0)] public int fieldCount = 5;
    [Tooltip("Random horizontal distance from the story monster each one is placed next to.")]
    [Min(0f)] public float fieldSpread = 3f;
    [Tooltip("Never placed closer than this to the player.")]
    [Min(0f)] public float fieldMinPlayerDistance = 5f;

    [Header("Pack (development test spawn)")]
    [Min(1)] public int packSize = 3;
    [Min(0f)] public float packSpacing = .9f;
    [Tooltip("Each pack member moves at moveSpeed x (1 +/- this), so they do not stack on one pixel.")]
    [Range(0f, .5f)] public float speedJitter = .1f;
}
