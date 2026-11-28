using UnityEngine;

/// <summary>
/// Tuning and art of the Forest boss "Bartol the Iron Foreman" (Resources/Monsters/BossBartol).
/// Every number here is a starting point from Docs/BossImplementationPlan.md and is meant to be changed in the Inspector.
/// Sprite sheets are horizontal strips made by Tools/BossArt/boss_sprites.py; BossSpriteLayout holds their frame sizes.
/// </summary>
[CreateAssetMenu(menuName = "BehindTheGear/Boss Profile", fileName = "BossBartol")]
public sealed class BossProfile : ScriptableObject
{
    public const string ResourcePath = "Monsters/BossBartol";

    [Header("Name (HP bar and title card)")]
    public string title = "붉은 완장의 두목";
    public string displayName = "강철 공방장 바르톨";

    [Header("Sprite sheets (Assets/!Animation/Boss)")]
    public Texture2D body;
    public Texture2D pilot;
    public Texture2D gear;
    public Texture2D furnace;
    public Texture2D fist;
    public Texture2D chain;
    public Texture2D fxDust, fxShockwave, fxSlag, fxMarker, fxSteam, fxSpark, fxExplosion, fxShadow;

    [Header("Sound (Assets/!Sounds/Boss, synthesised by Tools/BossArt/boss_audio.py)")]
    public AudioClip bgm;
    public AudioClip sfxTread;
    public AudioClip sfxThud, sfxSlam, sfxClank, sfxChain, sfxSteam, sfxWhistle, sfxCharge, sfxExplosion, sfxSlagHit, sfxSweep, sfxRoar;
    [Range(0f, 1f)] public float musicVolume = .5f;
    [Range(0f, 1f)] public float effectsVolume = .85f;

    [Header("Entrance")]
    [Tooltip("The boss starts this far to the right of the arena centre (off screen) and walks in.")]
    [Min(0f)] public float entranceWalkDistance = 15f;
    [Tooltip("Walking speed of the entrance (world units per second).")]
    [Min(.5f)] public float entranceWalkSpeed = 5.5f;
    [Min(0f)] public float titleHoldSeconds = 1.4f;

    [Header("Defeat")]
    [Tooltip("Freeze on the finishing blow (real seconds).")]
    [Min(0f)] public float defeatHitStopSeconds = .25f;
    [Range(.05f, 1f)] public float defeatSlowScale = .35f;
    [Tooltip("Length of the slow-motion stretch (real seconds).")]
    [Min(0f)] public float defeatSlowSeconds = 1.2f;
    [Min(1)] public int defeatExplosions = 6;

    [Header("Health")]
    [Tooltip("Boss HP = the player's basic attack damage x this (about this many basic hits).")]
    [Min(1)] public int healthInBasicHits = 32;
    [Tooltip("Used when no player attack can be found (e.g. spawned in an empty test scene).")]
    [Min(1)] public int fallbackAttackDamage = 10;
    [Tooltip("Phase 2 starts at or below this share of the maximum HP.")]
    [Range(.05f, .95f)] public float phaseTwoAt = .5f;
    [Tooltip("Share of a poison tick that reaches the boss (the plan uses one third).")]
    [Range(0f, 1f)] public float poisonTickShare = 1f / 3f;

    [Header("Look")]
    [Tooltip("The boss is drawn unlit (the forest's strong spotlights washed it out) and multiplied by this colour, so it\n"
        + "still sits in the dim, warm light of the scene. Brighter = more contrast against the background.")]
    public Color sceneTone = new(.86f, .80f, .76f, 1f);
    [Tooltip("Pixels the boss is lowered into the ground: the ground collider sits a little above the painted ground.")]
    [Min(0)] public int groundSinkPixels = 1;

    [Header("Arena")]
    [Tooltip("World X of the middle of the fight (the boss body stands here).")]
    public float arenaCenterX = 2f;
    [Tooltip("The player may move this far to each side of the centre during the fight.")]
    [Min(1f)] public float arenaHalfWidth = 8.5f; // the fixed fight camera shows about 8.9 units to each side

    [Header("Weak point: Bartol in the bucket (pixels inside the 52x40 pilot frame, origin top-left)")]
    [Tooltip("Normal: head and shoulders only, so a jump from the ground cannot reach it but a jump from a stuck fist can.")]
    public RectInt weakPointNormal = new(10, 2, 32, 20);
    [Tooltip("Overheated: the whole pilot and bucket, low enough for a basic attack from the ground.")]
    public RectInt weakPointOverheated = new(4, 2, 43, 37);

    [Header("Idle motion")]
    [Min(.05f)] public float idleFrameSeconds = .55f;
    [Min(.02f)] public float gearFrameSeconds = .14f;
    [Tooltip("Resting place of the right fist's top-left pixel, relative to the right drum anchor (x right, y down). The left fist mirrors it.")]
    public Vector2Int fistRest = new(8, -4);
    [Tooltip("The fists drift this many pixels up and down while idle.")]
    [Min(0)] public int fistHoverPixels = 2;
    [Min(.1f)] public float fistHoverSeconds = 1.6f;
    [Min(1)] public int chainLinks = 5;

    [Header("Fight flow")]
    [Min(0f)] public float firstPatternDelay = .6f;
    [Min(0f)] public float patternGap = 1f;

    [Header("A. Hammer slam (damage is what the player loses, after defence)")]
    [Min(1)] public int slamCount = 2;
    [Min(0)] public int slamDamage = 3;
    [Min(0)] public int slamDustDamage = 1;
    [Tooltip("Both fists rise for this long before the first slam.")]
    [Min(.05f)] public float slamRaiseSeconds = .5f;
    [Tooltip("The fist follows the player while its shadow darkens; it stops following for the last part.")]
    [Min(.1f)] public float slamShadowSeconds = .7f;
    [Min(0f)] public float slamLockSeconds = .18f;
    [Min(.02f)] public float slamDropSeconds = .12f;
    [Tooltip("How long the fist stays stuck in the ground (it is a platform during this time).")]
    [Min(0f)] public float slamStuckSeconds = .9f;
    [Tooltip("Pixels above the top of the body frame where a raised fist waits.")]
    public int slamRaisePixels = 4;
    [Tooltip("Pixels per second the raised fist can move sideways while it follows the player.")]
    [Min(1f)] public float slamTrackSpeed = 170f;
    [Tooltip("Width and height (world units) of the landing hit, centred on the fist.")]
    public Vector2 slamHitSize = new(2.4f, 2f);
    [Tooltip("Width (world units) of the weaker dust hit on each side of the landing.")]
    [Min(0f)] public float slamDustWidth = 1.2f;

    [Header("B. Crane sweep")]
    [Min(1)] public int sweepCount = 2;
    [Min(0)] public int sweepDamage = 2;
    [Tooltip("The body leans and that smokestack steams for this long before the fist moves.")]
    [Min(.1f)] public float sweepTelegraphSeconds = .8f;
    [Min(.1f)] public float sweepSeconds = 1.1f;
    [Min(0f)] public float sweepEndPause = .6f;
    [Tooltip("Height (world units) of the sweeping hit above the ground: a jump clears it.")]
    [Min(.1f)] public float sweepHitHeight = 1.3f;

    [Header("C. Furnace shot, then the overheat window")]
    public Vector2Int slagCount = new(6, 8);
    [Min(0)] public int slagDamage = 1;
    [Min(.1f)] public float slagChargeSeconds = 1.2f;
    [Tooltip("Flight time of a glob (min..max seconds).")]
    public Vector2 slagFlightSeconds = new(1.1f, 1.6f);
    [Tooltip("The landing mark blinks on the ground this long before the glob lands.")]
    [Min(.1f)] public float slagMarkerSeconds = .6f;
    [Tooltip("How high above a straight line the glob arcs (world units).")]
    [Min(0f)] public float slagArcHeight = 4.5f;
    [Tooltip("Width and height (world units) of a landing hit.")]
    public Vector2 slagHitSize = new(1.2f, 1f);
    [Tooltip("The machine sags and the bucket comes down for this long: the time to attack Bartol from the ground.")]
    [Min(.5f)] public float overheatSeconds = 3.5f;

    [Header("Phase 2 (same patterns, faster; plus D)")]
    [Tooltip("The boss cannot be hurt while it changes phase.")]
    [Min(.5f)] public float phaseChangeSeconds = 2f;
    [Tooltip("A: shorter shadow warning.")]
    [Min(.1f)] public float rageSlamShadowSeconds = .5f;
    [Tooltip("A: the pair of slams is repeated this many times...")]
    [Min(1)] public int rageSlamSets = 2;
    [Tooltip("...with this pause between the sets.")]
    [Min(0f)] public float rageSlamSetGap = .6f;
    [Tooltip("B: the second sweep comes from the other side this long after the first one ends.")]
    [Min(.1f)] public float rageSweepGap = .45f;
    [Tooltip("C: globs rain over the whole arena.")]
    public Vector2Int rageSlagCount = new(10, 12);
    [Min(.1f)] public float rageSlagChargeSeconds = .8f;
    [Min(.5f)] public float rageOverheatSeconds = 2.5f;

    [Header("Walking (entrance, and chasing the player in phase 2)")]
    [Tooltip("Time one foot is in the air. A whole stride is four of these.")]
    [Min(.05f)] public float walkStepSeconds = .2f;
    [Header("Phase 2: the Goliath drops onto crawler tracks and never stands still")]
    [Tooltip("Top speed of the tank (world units per second). It drives past the player and comes back, again and again.")]
    [Min(0f)] public float rageRollSpeed = 3f;
    [Tooltip("How quickly it gets up to speed and brakes (units per second, per second).")]
    [Min(.1f)] public float rageRollAcceleration = 7f;
    [Tooltip("It turns round this far past the player.")]
    [Min(0f)] public float rageRollOvershoot = 3.5f;
    [Tooltip("One clank of the tracks per this many units travelled.")]
    [Min(.1f)] public float treadClankDistance = .9f;
    [Tooltip("Camera in phase 2: the view grows by this factor (1 = unchanged). The forest art is only as tall as the\n"
        + "normal view, so the extra height shows as black bars: a wide-screen frame.")]
    [Range(1f, 1.6f)] public float rageZoom = 1.25f;
    [Tooltip("The Goliath never walks further than this from the arena centre.")]
    [Min(0f)] public float roamHalfRange = 10f;

    [Header("D. Triple quake (phase 2 only)")]
    [Min(1)] public int quakeCount = 3;
    [Min(0)] public int quakeDamage = 2;
    [Tooltip("Both fists are held high while the ground seams glow.")]
    [Min(.1f)] public float quakeTelegraphSeconds = .8f;
    [Tooltip("Time from one slam to the next.")]
    [Min(.2f)] public float quakeInterval = .55f;
    [Tooltip("Speed of the ground wave (world units per second).")]
    [Min(1f)] public float quakeWaveSpeed = 9f;
    [Tooltip("Height of the ground wave's hit: a jump clears it.")]
    [Min(.1f)] public float quakeWaveHeight = .8f;
    [Min(0f)] public float quakeEndPause = 1f;
    [Tooltip("Where the two fists land, as a share of the half arena from the centre.")]
    [Range(.1f, .9f)] public float quakeFistSpread = .45f;

    [Header("Hit feedback")]
    [Min(0f)] public float hitPoseSeconds = .16f;

    [Header("Sorting (Default layer: background -2/-1, player and monsters 1)")]
    public int bodySortingOrder = 0;
    public int fistSortingOrder = 3;
    public int fxSortingOrder = 5;
}
