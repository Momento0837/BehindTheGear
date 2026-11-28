using System;
using UnityEngine;

/// <summary>
/// Forest boss: Bartol on the Forge Goliath. Owns the HP, the phase and what the machine is doing.
/// This file: life cycle, damage in and out, and the idle motion of every part no pattern is using.
/// BossGoliath.Patterns.cs: the fight loop and the attack patterns.
/// It is not a MonsterFollowController: monster tools (health bars, wildcat spawner, field save) do not see it.
/// </summary>
[DisallowMultipleComponent]
public sealed partial class BossGoliath : MonoBehaviour
{
    public static BossGoliath Active { get; private set; }

    public BossProfile Profile { get; private set; }
    public BossGoliathRig Rig { get; private set; }
    public BossWeakPoint WeakPoint { get; private set; }
    public int MaxHealth { get; private set; }
    public int CurrentHealth { get; private set; }
    public bool IsDead { get; private set; }
    /// <summary>True while a cinematic owns the boss (entrance, phase change): hits are ignored.</summary>
    public bool Invulnerable { get; set; }
    public bool Overheated { get; private set; }
    /// <summary>1, then 2 once the HP falls to BossProfile.phaseTwoAt.</summary>
    public int Phase { get; private set; } = 1;

    /// <summary>Damage actually taken (after clamping to the remaining HP).</summary>
    public event Action<int> Damaged;
    public event Action Died;
    /// <summary>Raised once, when phase 2 begins (the boss is invulnerable while it changes).</summary>
    public event Action<int> PhaseChanged;

    // A pattern takes the parts it animates; everything else keeps idling.
    private bool bodyControlled, pilotControlled, furnaceControlled;
    private readonly bool[] fistControlled = new bool[2];
    private float gearSpeed = 1f, gearTurn;
    private float hitPoseUntil;
    private float spawnedAt;
    /// <summary>World X of the arena centre the fight started at (the Goliath roams around it in phase 2).</summary>
    public float HomeX { get; set; }
    private PlayerHealth playerHealth;
    private Collider2D playerCollider;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Active = null; // domain reload is off in this project

    /// <summary>Builds the boss with its feet on `groundPoint` (the middle of the arena, on the ground line).</summary>
    public static BossGoliath Spawn(BossProfile profile, Vector3 groundPoint)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        if (Active != null) Destroy(Active.gameObject);
        var go = new GameObject("Boss - Bartol");
        go.transform.position = Snap(groundPoint);
        var boss = go.AddComponent<BossGoliath>();
        boss.HomeX = groundPoint.x;
        boss.Initialize(profile);
        return boss;
    }

    private void Initialize(BossProfile profile)
    {
        Profile = profile;
        Active = this;
        spawnedAt = Time.time;

        Rig = gameObject.AddComponent<BossGoliathRig>();
        Rig.Build(profile);
        WeakPoint = BossWeakPoint.Create(this, Rig.PilotTransform);
        WeakPoint.SetArea(profile.weakPointNormal);

        MaxHealth = Mathf.Max(1, PlayerAttackDamage(profile) * profile.healthInBasicHits);
        CurrentHealth = MaxHealth;
        Audio = BossAudio.Create(this);
        hullArmor = BossArmor.Create(this, Rig);
        if (FindPlayer()) playerHealth.Died += OnPlayerDied;
        CameraDirector = BossCameraDirector.Create(this);
        // The HP bar is shown by whoever starts the fight (after the entrance): ShowHealthBar().
    }

    private void SetOverheated(bool value)
    {
        if (Overheated == value) return;
        Overheated = value;
        WeakPoint.SetArea(value ? Profile.weakPointOverheated : Profile.weakPointNormal);
        // The lowered bucket hangs in front of the boiler: the boiler plate steps aside so nothing shields Bartol.
        if (hullArmor != null) hullArmor.SetBlocking(!value);
    }

    /// <summary>All damage to the boss ends here (melee now; grenade, dagger and shockwave join in milestone 6).</summary>
    public void TakeHit(int damage, bool damageOverTime = false)
    {
        if (IsDead || Invulnerable || damage <= 0) return;
        if (damageOverTime) damage = Mathf.Max(1, Mathf.RoundToInt(damage * Profile.poisonTickShare));
        int taken = Mathf.Min(damage, CurrentHealth);
        CurrentHealth -= taken;
        hitPoseUntil = Time.time + Profile.hitPoseSeconds;
        lastWeakHitFrame = Time.frameCount;
        var area = WeakPoint.Bounds;
        MonsterDamageNumbers.Show(new Vector3(area.center.x, area.max.y, 0f), damage, damageOverTime);
        Damaged?.Invoke(taken);
        if (CurrentHealth <= 0) Die();
        else if (Phase == 1 && CurrentHealth <= MaxHealth * Profile.phaseTwoAt) BeginPhaseTwo();
    }

    /// <summary>
    /// Hurts the player when their body overlaps `area` (world units). `damage` is what they lose: the player's
    /// defence is added back because PlayerHealth subtracts it. Returns true when the area touched the player.
    /// </summary>
    public bool HitPlayer(Rect area, int damage)
    {
        if (damage <= 0 || !FindPlayer() || playerHealth.IsDead) return false;
        Bounds bounds = playerCollider.bounds;
        if (!area.Overlaps(new Rect(bounds.min, bounds.size))) return false;
        playerHealth.TakeDamage(playerHealth.Defense + damage);
        return true;
    }

    private bool FindPlayer()
    {
        if (playerHealth != null && playerCollider != null) return true;
        var player = PlayerTargetRegistry.Current;
        if (player == null) return false;
        playerHealth = player.GetComponentInParent<PlayerHealth>();
        if (playerHealth == null) playerHealth = player.GetComponentInChildren<PlayerHealth>();
        playerCollider = playerHealth != null ? playerHealth.GetComponent<Collider2D>() : null;
        return playerHealth != null && playerCollider != null;
    }

    private bool PlayerAlive => FindPlayer() && !playerHealth.IsDead;
    /// <summary>Player X in world units (the arena centre when there is no player).</summary>
    private float PlayerX => FindPlayer() ? playerCollider.bounds.center.x : transform.position.x;

    private void Update()
    {
        if (IsDead || Rig == null) return;
        UpdateRoll();
        // Phase 1: the player is fenced in around the standing boss. Phase 2: the tank roams, the camera frames tank
        // and player together, and the fence is simply what that camera shows.
        if (Fighting)
        {
            if (Phase >= 2) ForestBossEncounter.MoveArena(CameraDirector.CentreX, CameraDirector.HalfWidth - .6f);
            else ForestBossEncounter.MoveArena(transform.position.x, Profile.arenaHalfWidth);
        }
        float time = Time.time - spawnedAt; // scaled time: the boss freezes with the game (pause, hit-stop)
        bool second = Mathf.FloorToInt(time / Profile.idleFrameSeconds) % 2 == 1;

        if (!dormant) gearTurn += Time.deltaTime * gearSpeed / Profile.gearFrameSeconds;
        Rig.GearStep = Mathf.FloorToInt(gearTurn);
        bool rage = Phase >= 2; // torn banner, open furnace, brighter fire, visor down
        if (!bodyControlled)
            Rig.BodyFrame = rage ? (rolling ? BossSpriteLayout.Body.RageRoll0 + TreadStep : second ? BossSpriteLayout.Body.Rage1 : BossSpriteLayout.Body.Rage0)
                : (second ? BossSpriteLayout.Body.Idle1 : BossSpriteLayout.Body.Idle0);
        if (!furnaceControlled) Rig.FurnaceStage = dormant ? -1 : (int)(rage ? BossSpriteLayout.Furnace.Charge : BossSpriteLayout.Furnace.Idle);

        bool hit = Time.time < hitPoseUntil;
        if (hit) Rig.PilotFrame = BossSpriteLayout.Pilot.Hit;
        else if (Overheated) Rig.PilotFrame = second ? BossSpriteLayout.Pilot.Panic1 : BossSpriteLayout.Pilot.Panic0;
        else if (!pilotControlled)
            Rig.PilotFrame = rage ? BossSpriteLayout.Pilot.Rage0
                : Mathf.FloorToInt((time + .2f) / (Profile.idleFrameSeconds * 1.3f)) % 2 == 1 ? BossSpriteLayout.Pilot.Idle1 : BossSpriteLayout.Pilot.Idle0;
        else Rig.PilotFrame = rage ? RagePose(pilotPose) : pilotPose;
        Rig.PilotNudge = hit ? new Vector2Int(Mathf.FloorToInt(Time.time * 40f) % 2 == 0 ? 1 : -1, 0) : Vector2Int.zero;

        // Free fists hang beside the shoulders and drift a little, out of step with each other.
        for (int side = 0; side < 2; side++)
        {
            if (fistControlled[side]) continue;
            var rest = Rig.RestFist(side);
            float phase = time / Profile.fistHoverSeconds * Mathf.PI * 2f + side * 1.9f;
            Rig.FistFrame[side] = BossSpriteLayout.Fist.Clench;
            Rig.FistPixel[side] = new Vector2(rest.x, rest.y + Mathf.Round(Mathf.Sin(phase) * Profile.fistHoverPixels));
        }
    }

    // ---- phase 2: crawler tracks. The tank drives past the player, turns round a little beyond them and comes back,
    // patterns or not. It only brakes when a pattern needs the hull still (a fist stuck in the ground, the overheat).
    private float rollSpeed, exactX, treadDistance, nextClank;
    private int rollDirection;
    private bool rolling, brake;

    private int TreadStep
    {
        get
        {
            int step = Mathf.FloorToInt(treadDistance * BossSpriteLayout.PixelsPerUnit / 2f) % 4;
            return rollSpeed < 0f ? 3 - step : step; // the belt runs the other way when reversing
        }
    }

    private void UpdateRoll()
    {
        var profile = Profile;
        bool drive = Phase >= 2 && Fighting && !Invulnerable && !Overheated && !brake && PlayerAlive && profile.rageRollSpeed > 0f;
        float x = transform.position.x;
        if (Mathf.Approximately(rollSpeed, 0f)) exactX = x; // not moving: follow wherever the body was put
        float desired = 0f;
        if (drive)
        {
            if (rollDirection == 0) rollDirection = PlayerX >= x ? 1 : -1;
            float min = HomeX - profile.roamHalfRange, max = HomeX + profile.roamHalfRange;
            float target = Mathf.Clamp(PlayerX + rollDirection * profile.rageRollOvershoot, min, max);
            if ((target - exactX) * rollDirection <= .1f)
            {
                // Past the turning point (or at the edge of its range): come back the other way.
                rollDirection = -rollDirection;
                target = Mathf.Clamp(PlayerX + rollDirection * profile.rageRollOvershoot, min, max);
            }
            desired = Mathf.Sign(target - exactX) * profile.rageRollSpeed;
        }
        // A pattern that plants the hull stops it dead (a fist in the ground must not be dragged); otherwise it
        // gathers speed and coasts down.
        rollSpeed = brake ? 0f : Mathf.MoveTowards(rollSpeed, desired, profile.rageRollAcceleration * Time.deltaTime);
        rolling = Mathf.Abs(rollSpeed) > .2f;
        if (Mathf.Approximately(rollSpeed, 0f)) return;

        float step = rollSpeed * Time.deltaTime;
        exactX += step;
        transform.position = Snap(new Vector3(exactX, transform.position.y, transform.position.z));
        treadDistance += Mathf.Abs(step);
        if (treadDistance < nextClank) return;
        nextClank = treadDistance + profile.treadClankDistance;
        Audio.Play(profile.sfxTread, .55f);
        CameraDirector.Shake(.035f, .12f);
        // dirt thrown out behind the tracks
        Dust(Rig.PixelToWorld(rollSpeed > 0f ? 12 : 100, Rig.GroundPixelY), rollSpeed > 0f ? -1f : 1f);
    }

    // Phase 2 keeps the visor down: the lever poses have their own frames, the roar is shared.
    private static BossSpriteLayout.Pilot RagePose(BossSpriteLayout.Pilot pose) => pose switch
    {
        BossSpriteLayout.Pilot.Pull0 => BossSpriteLayout.Pilot.Rage0,
        BossSpriteLayout.Pilot.Pull1 => BossSpriteLayout.Pilot.Rage1,
        _ => pose,
    };

    // Dying to the boss resumes at the boss (short entrance), not at the start of the Forest fight.
    private void OnPlayerDied() { if (!IsDead) ForestBossEncounter.MarkRetry(); }

    private int lastWeakHitFrame = -1, armorHitFrame = -1;
    private Vector3 armorHitPoint;
    private BossArmor hullArmor;

    /// <summary>A hit landed on a fist or the boiler: no damage. Reported once per frame, and not at all when the
    /// same swing also reached Bartol (the weak point wins).</summary>
    public void ArmorStruck(Vector3 point)
    {
        if (IsDead) return;
        armorHitFrame = Time.frameCount;
        armorHitPoint = point;
    }

    private void LateUpdate()
    {
        if (armorHitFrame != Time.frameCount || lastWeakHitFrame == Time.frameCount) return;
        armorHitFrame = -1;
        BossFx.Play(Rig, Rig.Spark, armorHitPoint, .06f, Profile.fxSortingOrder);
        Audio.Play(Profile.sfxClank, .8f);
        MonsterDamageNumbers.ShowLabel(armorHitPoint + Vector3.up * .5f, "0", new Color(.8f, .77f, .78f), .8f);
    }

    private void OnDestroy()
    {
        if (Active == this) Active = null;
        if (playerHealth != null) playerHealth.Died -= OnPlayerDied;
        RestoreTimeIfOwned();                 // never leave the game in slow motion
        ForestBossEncounter.RestoreArena();   // nor the player fenced into an arena that is gone
    }

    // The boss must be lit like everything else in the scene: reuse the player's (or any monster's) sprite material.
    private static Material SceneSpriteMaterial()
    {
        var player = PlayerTargetRegistry.Current;
        var renderer = player != null ? player.GetComponentInChildren<SpriteRenderer>() : null;
        if (renderer == null) renderer = FindFirstObjectByType<SpriteRenderer>();
        return renderer != null ? renderer.sharedMaterial : null;
    }

    private static int PlayerAttackDamage(BossProfile profile)
    {
        var player = PlayerTargetRegistry.Current;
        var melee = player != null ? player.GetComponentInParent<PlayerMeleeAttack>() : null;
        if (melee == null && player != null) melee = player.GetComponentInChildren<PlayerMeleeAttack>();
        if (melee == null) melee = FindFirstObjectByType<PlayerMeleeAttack>();
        return melee != null ? Mathf.Max(1, melee.ResolvedDamage) : profile.fallbackAttackDamage;
    }

    /// <summary>Whole pixels only (11 px per unit), so the boss lines up with the pixel grid of the scene art.</summary>
    public static Vector3 Snap(Vector3 position)
    {
        float ppu = BossSpriteLayout.PixelsPerUnit;
        return new Vector3(Mathf.Round(position.x * ppu) / ppu, Mathf.Round(position.y * ppu) / ppu, position.z);
    }
}
