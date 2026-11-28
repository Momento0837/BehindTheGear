using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public sealed class MonsterFollowController : MonoBehaviour
{
    [Header("Stats")]
    [SerializeField, Min(1)] private int maxHealth = 10;
    [SerializeField, Min(0f)] private float moveSpeed = 3f;
    [SerializeField, Min(1)] private int attackPower = 10;
    [SerializeField, Min(.01f)] private float attackRecovery = .7f;

    [Header("Ranges")]
    [SerializeField] private BoxCollider2D recognitionRange;
    [SerializeField] private BoxCollider2D attackRange;
    [SerializeField] private Transform player;
    [SerializeField] private LayerMask sightBlockingLayers;

    [Header("Drops")]
    [SerializeField] private GameObject[] dropPrefabs;
    [Tooltip("Reward tier for coins / base parts / AP (Resources/Monsters/RewardTable).")]
    [SerializeField] private MonsterRank rank = MonsterRank.Common;

    [Header("Patrol")]
    [SerializeField, Min(0f)] private float patrolRadius = 2f;
    [SerializeField, Min(.01f)] private float patrolMoveDuration = 4.5f;
    [SerializeField] private Vector2 patrolWaitRange = new Vector2(1f, 4f);

    [Header("Lunge (off by default; enabled by fast monsters such as the wildcat)")]
    [SerializeField] private bool lungeEnabled;
    [SerializeField, Min(0f)] private float lungeTriggerDistance = 4.5f;
    [SerializeField, Min(0f)] private float lungeSpeed = 12f;
    [SerializeField, Min(0f)] private float lungeDuration = .22f;
    [SerializeField, Min(0f)] private float lungeCooldown = 2.5f;
    private float lungeUntil;
    private float nextLungeTime;
    private float lungeDirection;

    [Header("Crowding (off by default; small pack monsters such as the wildcat)")]
    [SerializeField] private bool keepSpacing;
    [SerializeField, Min(0f)] private float spacing = 1.6f;
    // Every enabled monster, so spacing monsters can see who is ahead of them on the way to the player.
    private static readonly List<MonsterFollowController> activeMonsters = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetActiveMonsters() => activeMonsters.Clear();

    private MonsterRangeTrigger2D attackSensor;
    private MonsterRangeTrigger2D recognitionSensor;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private string animationState;
    private float actionUntil;
    private float patrolHomeX;
    private float patrolStartX;
    private float patrolElapsed;
    private float patrolWaitDuration;
    private int patrolDirection = 1;
    private bool patrolWaiting;
    private bool wasChasing;
    private Rigidbody2D body;
    private PlayerHealth playerHealth;
    private readonly RaycastHit2D[] sightHits = new RaycastHit2D[16];
    private float nextAttackTime;
    private bool attackPending;
    [Header("Hit Feedback")]
    [SerializeField, Min(0f)] private float hitStopDuration = .05f;
    [SerializeField, Min(0f)] private float knockbackSpeed = 3f;
    [SerializeField, Min(0f)] private float knockbackDuration = .12f;
    private Material originalMaterial;
    private Material flashMaterial;
    private SpriteRenderer flashOverlay;
    private bool IsAttacking => animationState == "Attack" && Time.time < actionUntil;
    private float hitStopUntil;
    private float knockbackUntil;
    private float knockbackDirection;
    private float skillStaggerUntil;
    private float skillPushUntil;
    private float skillPushVelocity;
    private Color normalColor;
    private float normalAnimatorSpeed;
    private Coroutine hitFeedback;
    private bool deathLandingPlayed;
    private bool usesGangBAnimationSounds = true;
    private AudioSource movementSoundSource;
    private int lastWalkDirection;
    private int lastFootContactFrame = -1;

    public int CurrentHealth { get; private set; }
    public int MaxHealth => maxHealth;
    public bool IsDevelopmentSpawn { get; private set; }
    // Extra field mobs placed at runtime (e.g. Forest wildcats): not part of story clear checks and not saved in field state.
    public bool IsAmbientSpawn { get; private set; }
    public void MarkAmbientSpawn() => IsAmbientSpawn = true;
    // Development test spawns never advance quests, drop loot or enter field saves.
    public void MarkDevelopmentSpawn()
    {
        IsDevelopmentSpawn = true;
        loot = System.Array.Empty<InventoryItemDefinition>();
    }

    // Applies a MonsterProfile (stats, lunge, animation, body/attack box). Call it before Awake, e.g. on a clone
    // built under an inactive parent, so the range sensors and patrol state are created from the profile values.
    public void ApplyProfile(MonsterProfile profile, float speedMultiplier = 1f)
    {
        if (profile == null) return;
        // Wildcat shares the attack callback, but must not inherit GangB's swing or movement sounds.
        var wildcatProfile = Resources.Load<MonsterProfile>(WildcatFieldSpawner.ProfilePath);
        usesGangBAnimationSounds = profile != wildcatProfile
            && (wildcatProfile == null || wildcatProfile.animatorController == null
                || profile.animatorController != wildcatProfile.animatorController);
        tutorialProtected = false;
        rank = profile.rank;
        maxHealth = Mathf.Max(1, profile.maxHealth);
        attackPower = Mathf.Max(1, profile.attackPower);
        moveSpeed = Mathf.Max(0f, profile.moveSpeed * Mathf.Max(0f, speedMultiplier));
        attackRecovery = Mathf.Max(.01f, profile.attackRecovery);
        lungeEnabled = profile.lunge;
        lungeTriggerDistance = profile.lungeTriggerDistance;
        lungeSpeed = profile.lungeSpeed * Mathf.Max(0f, speedMultiplier);
        lungeDuration = profile.lungeDuration;
        lungeCooldown = profile.lungeCooldown;
        keepSpacing = profile.keepSpacing;
        spacing = profile.spacing;
        CurrentHealth = maxHealth;
        var profileRenderer = GetComponent<SpriteRenderer>();
        if (profileRenderer != null) profileRenderer.sortingOrder = profile.sortingOrder;
        var profileAnimator = GetComponent<Animator>();
        if (profileAnimator != null && profile.animatorController != null)
            profileAnimator.runtimeAnimatorController = profile.animatorController;
        if (!Mathf.Approximately(profile.visualScale, 1f))
            transform.localScale = Vector3.Scale(transform.localScale, new Vector3(profile.visualScale, profile.visualScale, 1f));
        if (profile.showHealthBar && GetComponent<MonsterHealthBar>() == null)
            gameObject.AddComponent<MonsterHealthBar>();
        if (attackRange != null)
        {
            attackRange.size = profile.attackRangeSize;
            attackRange.offset = profile.attackRangeOffset;
        }
        foreach (var box in GetComponents<BoxCollider2D>())
        {
            if (box.isTrigger || box == recognitionRange || box == attackRange) continue;
            box.size = profile.bodySize;
            box.offset = profile.bodyOffset;
        }
    }
    private bool tutorialProtected;
    private bool impactHitInProgress; // set by TakeMeleeDamage so TakeDamage can tell direct hits from poison ticks
    private InventoryItemDefinition[] loot = System.Array.Empty<InventoryItemDefinition>();
    public void ConfigureLoot(InventoryItemDefinition[] items) => loot = items ?? System.Array.Empty<InventoryItemDefinition>();
    public void SetTutorialProtection(bool value)
    {
        tutorialProtected = value;
        if (CurrentHealth > 0) CurrentHealth = value ? 999 : 1;
    }

    public void ConfigureOpeningHealth(int playerDamage)
    {
        tutorialProtected = false;
        maxHealth = Mathf.Max(1, playerDamage) * 2;
        CurrentHealth = maxHealth;
    }

    public void RestoreEncounterHealth(int health)
    {
        tutorialProtected = false;
        CurrentHealth = Mathf.Max(1, health);
        maxHealth = Mathf.Max(maxHealth, CurrentHealth);
    }

    public void BindPlayer(Transform target)
    {
        player = target;
        playerHealth = target != null ? target.GetComponent<PlayerHealth>() : null;
        IgnorePlayerBodyCollision();
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        body.freezeRotation = true;
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        normalColor = spriteRenderer != null ? spriteRenderer.color : Color.white;
        if (spriteRenderer != null)
        {
            originalMaterial = spriteRenderer.sharedMaterial;
            Shader shader = Resources.Load<Shader>("Effects/MonsterHitFlash");
            if (shader != null)
            {
                flashMaterial = new Material(shader);
                var overlay = new GameObject("White Hit Flash");
                overlay.transform.SetParent(transform, false);
                flashOverlay = overlay.AddComponent<SpriteRenderer>();
                flashOverlay.sharedMaterial = flashMaterial;
                flashOverlay.enabled = false;
            }
        }
        normalAnimatorSpeed = animator != null ? animator.speed : 1f;
        patrolHomeX = patrolStartX = body.position.x;
        patrolDirection = Random.value < .5f ? -1 : 1;
        BeginPatrolWait();
        CurrentHealth = maxHealth;
        recognitionSensor = CreateSensor(recognitionRange, false);
        attackSensor = CreateSensor(attackRange, true);
        playerHealth = player != null ? player.GetComponent<PlayerHealth>() : null;
        IgnorePlayerBodyCollision();
        PlayAnimation("Idle");
    }

    private void OnEnable()
    {
        IgnorePlayerBodyCollision();
        if (!activeMonsters.Contains(this)) activeMonsters.Add(this);
        IgnoreMonsterBodyCollisions();
    }

    // Spacing monsters keep their distance by AI instead of shoving: body colliders between them and other monsters
    // are ignored so packs never jitter or tangle (Monster collides with Monster in the project's physics matrix).
    private void IgnoreMonsterBodyCollisions()
    {
        foreach (var other in activeMonsters)
        {
            if (other == null || other == this || !(keepSpacing || other.keepSpacing)) continue;
            foreach (var mine in GetComponentsInChildren<Collider2D>(true))
            {
                if (mine.isTrigger) continue;
                foreach (var theirs in other.GetComponentsInChildren<Collider2D>(true))
                    if (!theirs.isTrigger) Physics2D.IgnoreCollision(mine, theirs, true);
            }
        }
    }

    // True when another living pack member (keepSpacing) stands between this one and the player, closer than `spacing`.
    // Other monsters (e.g. GangB) never block: their bodies are ignored, so a small monster slips past them to the player.
    // Equal positions are broken by instance id so two stacked spawns separate instead of moving as one.
    private bool IsBlockedByMonsterAhead()
    {
        if (!keepSpacing || player == null) return false;
        float x = body.position.x;
        float direction = player.position.x < x ? -1f : 1f;
        foreach (var other in activeMonsters)
        {
            if (other == null || other == this || !other.keepSpacing || other.CurrentHealth <= 0 || !other.isActiveAndEnabled) continue;
            float dx = (other.body != null ? other.body.position.x : other.transform.position.x) - x;
            if (Mathf.Abs(dx) >= spacing) continue;
            if (Mathf.Abs(dx) < .01f) { if (other.GetInstanceID() < GetInstanceID()) return true; continue; }
            if (dx * direction > 0f) return true;
        }
        return false;
    }

    private void OnDisable()
    {
        activeMonsters.Remove(this);
        lastWalkDirection = 0;
        lastFootContactFrame = -1;
        if (movementSoundSource != null) movementSoundSource.Stop();
        attackPending = false;
        lungeUntil = 0f;
        if (hitFeedback != null) StopCoroutine(hitFeedback);
        hitFeedback = null;
        if (animator != null) animator.speed = normalAnimatorSpeed;
        if (spriteRenderer != null && CurrentHealth > 0) spriteRenderer.color = normalColor;
        if (spriteRenderer != null) spriteRenderer.sharedMaterial = originalMaterial;
        if (flashOverlay != null) flashOverlay.enabled = false;
        Stop();
        if (CurrentHealth > 0) PlayAnimation("Idle");
    }

    private void IgnorePlayerBodyCollision()
    {
        if (player == null) return;
        foreach (var monsterCollider in GetComponentsInChildren<Collider2D>(true))
        {
            if (monsterCollider.isTrigger) continue;
            foreach (var playerCollider in player.GetComponentsInChildren<Collider2D>(true))
                if (!playerCollider.isTrigger)
                    Physics2D.IgnoreCollision(monsterCollider, playerCollider, true);
        }
    }

    private void FixedUpdate()
    {
        if (playerHealth == null && player != null) playerHealth = player.GetComponent<PlayerHealth>();
        bool targetHidden = PlayerSkills.IsHiddenFromMonsters(playerHealth);
        if (CurrentHealth > 0 && targetHidden)
        {
            attackPending = false;
            if (IsAttacking)
            {
                actionUntil = 0f;
                PlayAnimation("Idle");
            }
        }
        if (CurrentHealth > 0 && Time.time < skillStaggerUntil)
        {
            body.linearVelocity = new Vector2(Time.time < skillPushUntil ? skillPushVelocity : 0f, body.linearVelocity.y);
            return;
        }
        if (CurrentHealth > 0 && Time.unscaledTime < hitStopUntil)
        {
            Stop();
            return;
        }
        if (CurrentHealth > 0 && Time.time < knockbackUntil)
        {
            body.linearVelocity = new Vector2(knockbackDirection * knockbackSpeed, body.linearVelocity.y);
            return;
        }
        if (CurrentHealth > 0 && targetHidden)
        {
            // Do not patrol toward the invisible player by coincidence. Check visibility every tick.
            wasChasing = false;
            patrolStartX = body.position.x;
            Stop();
            if (Time.time >= actionUntil) PlayAnimation("Idle");
            return;
        }
        if (CurrentHealth <= 0 || Time.time < actionUntil)
        {
            Stop();
            return;
        }

        bool playerInRange = player != null && Mathf.Abs(player.position.x - body.position.x) <= 8f;
        bool canChase = playerInRange && player != null && playerHealth != null && !playerHealth.IsDead
            && !PlayerSkills.IsHiddenFromMonsters(playerHealth) && !IsSightBlocked();
        if (!canChase)
        {
            if (wasChasing)
            {
                patrolStartX = body.position.x;
                BeginPatrolWait();
            }
            wasChasing = false;
            lungeUntil = 0f;
            Patrol();
            return;
        }
        wasChasing = true;

        if (attackSensor != null && attackSensor.HasPlayer)
        {
            lungeUntil = 0f;
            Stop();
            Face(player.position.x - body.position.x);
            PlayAnimation("Idle");
            Attack();
            return;
        }

        if (IsBlockedByMonsterAhead())
        {
            // Queue behind the pack member in front instead of pushing through it.
            lungeUntil = 0f;
            Stop();
            Face(player.position.x - body.position.x);
            if (Time.time >= actionUntil) PlayAnimation("Idle");
            return;
        }
        if (TryLunge()) return;
        MoveTowardsCurrentPlayerPosition();
    }

    // Wildcat-style dash: no wind-up, horizontal burst toward the player, then the normal attack takes over in range.
    private bool TryLunge()
    {
        if (!lungeEnabled || player == null) return false;
        if (Time.time < lungeUntil)
        {
            body.linearVelocity = new Vector2(lungeDirection * lungeSpeed, body.linearVelocity.y);
            return true;
        }
        float offset = player.position.x - body.position.x;
        if (Time.time < nextLungeTime || Mathf.Abs(offset) > lungeTriggerDistance) return false;
        lungeDirection = offset < 0f ? -1f : 1f;
        lungeUntil = Time.time + lungeDuration;
        nextLungeTime = lungeUntil + lungeCooldown;
        Face(lungeDirection);
        PlayAnimation("Walk");
        body.linearVelocity = new Vector2(lungeDirection * lungeSpeed, body.linearVelocity.y);
        return true;
    }

    public void RecognizePlayer(PlayerHealth health)
    {
        if (CurrentHealth <= 0 || health == null || health.IsDead || PlayerSkills.IsHiddenFromMonsters(health)) return;
        player = health.transform;
        playerHealth = health;
        IgnorePlayerBodyCollision();
    }

    private MonsterRangeTrigger2D CreateSensor(BoxCollider2D source, bool attack)
    {
        if (source == null) return null;
        string sensorName = attack ? "Attack Trigger" : "Recognition Trigger";
        Transform child = transform.Find(sensorName);
        if (child == null)
        {
            child = new GameObject(sensorName).transform;
            child.SetParent(transform, false);
        }
        child.gameObject.layer = gameObject.layer;
        var collider = child.GetComponent<BoxCollider2D>();
        if (collider == null) collider = child.gameObject.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(source.size.x, source.size.y * (attack ? .7f : 1f));
        collider.offset = source.offset;
        collider.isTrigger = true;
        source.enabled = false;
        var sensor = child.GetComponent<MonsterRangeTrigger2D>();
        if (sensor == null) sensor = child.gameObject.AddComponent<MonsterRangeTrigger2D>();
        sensor.Configure(this, attack);
        return sensor;
    }
    private bool IsSightBlocked()
    {
        Vector2 origin = body.position;
        Vector2 target = player.position;
        Vector2 direction = target - origin;
        if (direction.sqrMagnitude <= Mathf.Epsilon) return false;
        ContactFilter2D filter = new();
        filter.SetLayerMask(sightBlockingLayers);
        filter.useTriggers = false;
        int count = Physics2D.Raycast(origin, direction.normalized, filter, sightHits, direction.magnitude);
        for (int i = 0; i < count; i++)
        {
            Collider2D hit = sightHits[i].collider;
            if (hit.attachedRigidbody == body || hit.GetComponentInParent<PlayerHealth>() != null) continue;
            return true;
        }
        return false;
    }

    private void Attack()
    {
        if (Time.time < nextAttackTime) return;
        if (playerHealth == null && player != null) playerHealth = player.GetComponent<PlayerHealth>();
        if (playerHealth == null || playerHealth.IsDead || PlayerSkills.IsHiddenFromMonsters(playerHealth)
            || Time.time < skillStaggerUntil) return;

        PlayAnimation("Attack", true);
        attackPending = true;
        actionUntil = Time.time + ClipLength("Attack");
        nextAttackTime = actionUntil + attackRecovery;
    }

    // Called by Attack.anim on the first impact sprite, not when the wind-up begins.
    public void ApplyAttackHit()
    {
        if (!isActiveAndEnabled || !attackPending || CurrentHealth <= 0 || animationState != "Attack") return;
        attackPending = false;
        // The frame-34 swing is audible even if the player has moved out of the hit range.
        if (usesGangBAnimationSounds) GameSoundService.PlayGangBAttack();
        if (attackSensor == null || !attackSensor.HasPlayer || playerHealth == null
            || playerHealth.IsDead || PlayerSkills.IsHiddenFromMonsters(playerHealth)
            || Time.time < skillStaggerUntil || IsSightBlocked()) return;
        int damage = Mathf.Max(1, attackPower - playerHealth.Defense);
        playerHealth.TakeDamage(attackPower);
        Debug.Log($"[Monster] {name} attacked {player.name}: {damage} damage", this);
    }

    public void TakeDamage(int damage)
    {
        if (damage <= 0 || CurrentHealth <= 0) return;
        lungeUntil = 0f; // any hit cancels a lunge in progress
        MonsterDamageNumbers.Show(this, damage, !impactHitInProgress);

        CurrentHealth = tutorialProtected ? 999 : Mathf.Max(0, CurrentHealth - damage);
        Debug.Log($"[Monster] {name} HP: {CurrentHealth} / {maxHealth}", this);
        if (CurrentHealth == 0) Die();
        else if (!IsAttacking)
        {
            Stop();
            PlayAnimation("Damaged", true);
            actionUntil = Time.time + ClipLength("Damaged");
        }
    }

    public void TakeMeleeDamage(int damage, float direction)
    {
        if (damage <= 0 || CurrentHealth <= 0) return;
        bool attacking = IsAttacking;
        hitStopUntil = attacking ? 0f : Time.unscaledTime + hitStopDuration;
        knockbackUntil = attacking ? 0f : Time.time + hitStopDuration + knockbackDuration;
        knockbackDirection = Mathf.Sign(direction);
        patrolStartX = body.position.x;
        BeginPatrolWait();
        impactHitInProgress = true;
        try { TakeDamage(damage); }
        finally { impactHitInProgress = false; }
        if (hitFeedback != null) StopCoroutine(hitFeedback);
        hitFeedback = StartCoroutine(HitFeedback());
    }

    public void TakeShockwaveDamage(int damage, float direction, float pushSpeed, float pushDuration, float staggerDuration)
    {
        if (!isActiveAndEnabled || damage <= 0 || CurrentHealth <= 0) return;
        // Shockwave interrupts wind-up; ordinary melee keeps its existing super-armor rules.
        attackPending = false;
        actionUntil = 0f;
        PlayAnimation("Idle", true);
        skillPushVelocity = (direction < 0f ? -1f : 1f) * Mathf.Max(0f, pushSpeed);
        skillPushUntil = Time.time + Mathf.Max(0f, pushDuration);
        skillStaggerUntil = Time.time + Mathf.Max(pushDuration, staggerDuration);
        TakeMeleeDamage(damage, direction);
    }

    private IEnumerator HitFeedback()
    {
        if (flashOverlay != null && flashMaterial != null)
        {
            SyncFlashOverlay();
            flashOverlay.enabled = true;
            flashMaterial.SetFloat("_FlashAmount", 1f);
        }
        if (!IsAttacking)
        {
            if (animator != null) animator.speed = 0f;
            yield return new WaitForSecondsRealtime(hitStopDuration);
        }
        if (animator != null) animator.speed = normalAnimatorSpeed;
        for (float elapsed = 0f; elapsed < .25f; elapsed += Time.deltaTime)
        {
            if (flashMaterial != null) flashMaterial.SetFloat("_FlashAmount", 1f - elapsed / .25f);
            yield return null;
        }
        if (spriteRenderer != null) spriteRenderer.sharedMaterial = originalMaterial;
        if (flashOverlay != null) flashOverlay.enabled = false;
        hitFeedback = null;
    }

    private void LateUpdate() => SyncFlashOverlay();

    private void SyncFlashOverlay()
    {
        if (flashOverlay == null || spriteRenderer == null) return;
        flashOverlay.sprite = spriteRenderer.sprite;
        flashOverlay.flipX = spriteRenderer.flipX;
        flashOverlay.flipY = spriteRenderer.flipY;
        flashOverlay.sortingLayerID = spriteRenderer.sortingLayerID;
        flashOverlay.sortingOrder = spriteRenderer.sortingOrder + 1;
        flashOverlay.color = new Color(1f, 1f, 1f, spriteRenderer.color.a);
    }

    private void OnDestroy()
    {
        if (flashMaterial != null) Destroy(flashMaterial);
    }

    private void Die()
    {
        deathLandingPlayed = false;
        if (movementSoundSource != null) movementSoundSource.Stop();
        if (!IsDevelopmentSpawn) QuestJournal.Record("kill", "monster", 1);
        Stop();
        if (!IsDevelopmentSpawn) CreateDrops();
        if (!IsDevelopmentSpawn) MonsterRewards.Drop(transform.position, rank);
        foreach (var collider in GetComponentsInChildren<Collider2D>()) collider.enabled = false;
        body.simulated = false;
        StartCoroutine(DeathSequence());
    }

    // The GangB body Dead clip calls this at frame 32.
    public void OnDeathLanding(float volume)
    {
        if (!usesGangBAnimationSounds || CurrentHealth > 0 || deathLandingPlayed) return;
        deathLandingPlayed = true;
        GameSoundService.PlayDeathLanding(volume);
    }

    private IEnumerator DeathSequence()
    {
        PlayAnimation("Dead", true);
        if (Time.unscaledTime < hitStopUntil)
            yield return new WaitForSecondsRealtime(hitStopDuration);
        yield return new WaitForSeconds(ClipLength("Dead"));
        yield return new WaitForSeconds(1f);
        var renderers = GetComponentsInChildren<SpriteRenderer>();
        var colors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++) colors[i] = renderers[i].color;
        float elapsed = 0f;
        while (elapsed < 1f)
        {
            elapsed += Time.deltaTime;
            for (int i = 0; i < renderers.Length; i++)
            {
                Color color = colors[i];
                color.a *= 1f - Mathf.Clamp01(elapsed);
                renderers[i].color = color;
            }
            yield return null;
        }
        Destroy(gameObject);
    }

    private float ClipLength(string state)
    {
        if (animator != null && animator.runtimeAnimatorController != null)
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
                if (clip.name == state) return clip.length;
        return 0f;
    }

    private void PlayAnimation(string state, bool restart = false)
    {
        if (state != "Attack") attackPending = false;
        if (animator == null || animator.runtimeAnimatorController == null) return;
        if (!restart && animationState == state) return;
        animator.Play("Base Layer." + state, 0, 0f);
        animationState = state;
    }

    private void Face(float direction)
    {
        if (spriteRenderer != null && Mathf.Abs(direction) > .001f)
            spriteRenderer.flipX = direction > 0f;
    }

    private bool CanHearWalking => usesGangBAnimationSounds && isActiveAndEnabled && CurrentHealth > 0
        && body != null && body.simulated && Mathf.Abs(body.linearVelocity.y) <= .2f
        && Time.timeScale > 0f && Time.time >= actionUntil && Time.time >= skillStaggerUntil
        && Time.time >= knockbackUntil && Time.unscaledTime >= hitStopUntil
        && !SceneFadeTransition.IsRunning && !CutsceneVideoSequence.IsAnyVisible
        && GameSoundService.CurrentMap != GameSoundMap.None;

    // GangB Walk.anim invokes this at frames 0 and 42; pauses and speed changes follow the Animator.
    public void OnFootstepContact()
    {
        if (!CanHearWalking || animationState != "Walk" || Mathf.Abs(body.linearVelocity.x) <= .001f
            || lastFootContactFrame == Time.frameCount) return;
        lastFootContactFrame = Time.frameCount;
        PlayMovementSound(false);
    }

    private void RecordWalkDirection(float velocity)
    {
        if (Mathf.Abs(velocity) <= .001f) return;
        int direction = velocity < 0f ? -1 : 1;
        bool reversed = lastWalkDirection != 0 && lastWalkDirection != direction;
        lastWalkDirection = direction;
        if (reversed && CanHearWalking) PlayMovementSound(true);
    }

    private void PlayMovementSound(bool loose)
    {
        if (player == null) return;
        // Use XY distance: the 2D camera's Z offset must not quiet nearby footsteps.
        float distance = Vector2.Distance(transform.position, player.position);
        float gain = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3f, 18f, distance));
        if (gain <= 0f) return;
        if (movementSoundSource == null)
        {
            var child = new GameObject("Movement Sounds");
            child.transform.SetParent(transform, false);
            movementSoundSource = child.AddComponent<AudioSource>();
            movementSoundSource.playOnAwake = false;
            movementSoundSource.spatialBlend = 0f;
            movementSoundSource.dopplerLevel = 0f;
            movementSoundSource.priority = 128;
        }
        // A per-monster source keeps player StopFootstep() from cutting off these sounds.
        GameSoundService.PlayMonsterFootstep(movementSoundSource, loose, gain);
    }

    private void BeginPatrolWait()
    {
        patrolWaiting = true;
        patrolElapsed = 0f;
        float minWait = Mathf.Max(0f, Mathf.Min(patrolWaitRange.x, patrolWaitRange.y));
        float maxWait = Mathf.Max(minWait, Mathf.Max(patrolWaitRange.x, patrolWaitRange.y));
        patrolWaitDuration = Random.Range(minWait, maxWait);
    }

    private void Patrol()
    {
        patrolElapsed += Time.fixedDeltaTime;
        if (patrolWaiting)
        {
            Stop();
            PlayAnimation("Idle");
            if (patrolElapsed >= patrolWaitDuration)
            {
                patrolWaiting = false;
                patrolElapsed = 0f;
                patrolStartX = body.position.x;
            }
            return;
        }
        float target = Mathf.Lerp(patrolStartX, patrolHomeX + patrolDirection * patrolRadius,
            Mathf.Clamp01(patrolElapsed / patrolMoveDuration));
        float velocity = (target - body.position.x) / Time.fixedDeltaTime;
        body.linearVelocity = new Vector2(velocity, body.linearVelocity.y);
        RecordWalkDirection(velocity);
        Face(velocity);
        PlayAnimation(Mathf.Abs(velocity) > .001f ? "Walk" : "Idle");
        if (patrolElapsed >= patrolMoveDuration)
        {
            BeginPatrolWait();
            patrolDirection *= -1;
        }
    }

    private void CreateDrops()
    {
        // The basic drop is independent of scene loot and always awards one cloth.
        var cloth = Resources.Load<InventoryItemDefinition>("Story/RedCloth");
        if (cloth != null) CreateDrop(cloth, 0f);
        else Debug.LogError("Missing monster drop: Resources/Story/RedCloth", this);

        // Preserve the existing 1% bonus equipment chance; potions are not monster loot.
        if (Random.Range(0, 100) != 0) return;
        var pool = loot.Where(item => item != null && item.IsEquipment
            && item.Effect == InventoryItemDefinition.ConsumableEffect.None).ToArray();
        if (pool.Length > 0) CreateDrop(pool[Random.Range(0, pool.Length)], .4f);
    }

    private void CreateDrop(InventoryItemDefinition item, float offsetX)
    {
        var drop = new GameObject("Monster Loot");
        drop.transform.position = transform.position + new Vector3(offsetX, .5f, 0f);
        drop.AddComponent<WorldItemPickup>().Configure(item, 1);
    }

    private void CreateTemporaryDrop()
    {
        GameObject drop = GameObject.CreatePrimitive(PrimitiveType.Quad);
        drop.name = "Monster Drop";
        drop.transform.position = transform.position + Vector3.up * .5f;
        drop.transform.localScale = Vector3.one * .35f;
        drop.GetComponent<Renderer>().material.color = Color.yellow;
    }

    private void MoveTowardsCurrentPlayerPosition()
    {
        float direction = Mathf.Sign(player.position.x - body.position.x);
        body.linearVelocity = new Vector2(direction * moveSpeed, body.linearVelocity.y);
        RecordWalkDirection(direction * moveSpeed);
        Face(direction);
        PlayAnimation("Walk");
    }

    private void Stop()
    {
        if (body != null) body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
    }

    private void OnDrawGizmos()
    {
        if (attackRange == null) return;

        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(attackRange.bounds.center, attackRange.bounds.size);
    }
}
