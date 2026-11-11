using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(PlayerInputReader))]
public sealed class PlayerMovement2D : MonoBehaviour
{
    [SerializeField, Min(0f)] private float moveSpeed = 7f;
    [Header("Walk - hold Ctrl")]
    [SerializeField, Min(0f)] private float walkSpeed = 5.25f;
    [Header("Dash - press Shift")]
    [SerializeField, Min(0f)] private float dashDistance = 3f;
    [SerializeField, Min(0f)] private float dashCooldown = 1f;
    [SerializeField, Min(.01f)] private float dashDuration = .25f;
    [Header("Dash afterimages")]
    [SerializeField] private bool dashAfterimages = false;
    [SerializeField, Min(.01f)] private float afterimageInterval = .025f;
    [SerializeField, Min(.01f)] private float afterimageLifetime = .22f;
    [SerializeField] private Color afterimageTint = new(.3f, .85f, 1f, .45f);
    [Header("Jump and crouch")]
    [SerializeField, Min(0f)] private float jumpVelocity = 12f;
    [SerializeField, Range(0f, 1f)] private float crouchSpeedMultiplier = .7f;
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField] private LayerMask oneWayPlatformLayers;
    [SerializeField, Min(.01f)] private float groundCheckDistance = .08f;
    [SerializeField, Min(.05f)] private float dropThroughDuration = .25f;

    private Rigidbody2D body;
    private BoxCollider2D box;
    private Vector2 standingSize;
    private Vector2 standingOffset;
    private float horizontalInput;
    private bool crouchHeld;
    private bool jumpRequested;
    private bool dropping;
    private bool attackAnimationPlaying;
    public bool IsAttacking => attackAnimationPlaying;
    private bool attackStateObserved;
    private bool bufferedNextAttack;
    private int comboAttackIndex;
    private float attackAnimationEndedAt = float.NegativeInfinity;
    private Animator animator;
    private PlayerInputReader playerInput;
    private PlayerMeleeAttack meleeAttack;
    private PlayerHealth playerHealth;
    private PlayerControlLock controlLock;
    private PlayerSpriteFacing spriteFacing;
    private SpriteRenderer spriteRenderer;
    private float dashDirection, dashSpeed, dashTimeRemaining, dashReadyAt, nextAfterimageAt;
    private CollisionDetectionMode2D collisionModeBeforeDash;
    public bool IsDashing { get; private set; }
    public float DashCooldownRemaining => Mathf.Max(0f, dashReadyAt - Time.time);
    private int lastKnownHealth;
    [SerializeField, Range(0f, 1f)] private float meleeImpactNormalizedTime = .48f;
    private float hitStopUntil;
    private float animatorSpeedBeforeHitStop;
    private bool hitStopped;

    public void ApplyHitStop(float duration)
    {
        if (!hitStopped && animator != null)
        {
            animatorSpeedBeforeHitStop = animator.speed;
            animator.speed = 0f;
        }
        hitStopped = true;
        hitStopUntil = Mathf.Max(hitStopUntil, Time.unscaledTime + duration);
    }

    private void OnDisable()
    {
        EndDash();
        if (hitStopped && animator != null) animator.speed = animatorSpeedBeforeHitStop;
        hitStopped = false;
        meleeAttack?.CancelComboSwing();
    }
    private bool hasIsMovingParameter;
    private bool hasDamagedTriggerParameter;
    private bool hasDeadTriggerParameter;
    private bool hasAttackTriggerParameter;
    private bool hasAttackIndexParameter;
    private bool hasJumpTriggerParameter;
    private bool hasIsGroundedParameter;
    private static readonly int IsMovingParameterHash = Animator.StringToHash("IsMoving");
    private static readonly int DamagedTriggerHash = Animator.StringToHash("Damaged");
    private static readonly int DeadTriggerHash = Animator.StringToHash("Dead");
    private static readonly int AttackTriggerHash = Animator.StringToHash("Attack");
    private static readonly int AttackIndexParameterHash = Animator.StringToHash("AttackIndex");
    private static readonly int JumpTriggerHash = Animator.StringToHash("Jump");
    private static readonly int IsGroundedParameterHash = Animator.StringToHash("IsGrounded");
    private static readonly int[] AttackStateHashes =
    {
        Animator.StringToHash("Base Layer.Attack_1"),
        Animator.StringToHash("Base Layer.Attack_2"),
        Animator.StringToHash("Base Layer.Attack_3")
    };
    private static readonly int[] RunAttackStateHashes =
    {
        Animator.StringToHash("Base Layer.Run_Attack_1"),
        Animator.StringToHash("Base Layer.Run_Attack_2"),
        Animator.StringToHash("Base Layer.Run_Attack_3")
    };
    private const float ComboInputWindow = .3f;
    private readonly List<Collider2D> ignoredPlatforms = new();
    private readonly RaycastHit2D[] groundHits = new RaycastHit2D[4];

    private void Awake()
    {
        // Also migrate already-loaded scene instances carrying the previous defaults.
        if (Mathf.Approximately(dashDuration, .15f)) dashDuration = .25f;
        if (Mathf.Approximately(walkSpeed, 3.5f)) walkSpeed = 5.25f;
        dashAfterimages = false;
        body = GetComponent<Rigidbody2D>();
        // Match the rendered player position to the camera's per-frame follow updates.
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        box = GetComponent<BoxCollider2D>();
        animator = GetComponent<Animator>();
        if (animator != null)
        {
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.nameHash == IsMovingParameterHash && parameter.type == AnimatorControllerParameterType.Bool)
                    hasIsMovingParameter = true;
                else if (parameter.nameHash == DamagedTriggerHash && parameter.type == AnimatorControllerParameterType.Trigger)
                    hasDamagedTriggerParameter = true;
                else if (parameter.nameHash == DeadTriggerHash && parameter.type == AnimatorControllerParameterType.Trigger)
                    hasDeadTriggerParameter = true;
                else if (parameter.nameHash == AttackTriggerHash && parameter.type == AnimatorControllerParameterType.Trigger)
                    hasAttackTriggerParameter = true;
                else if (parameter.nameHash == AttackIndexParameterHash && parameter.type == AnimatorControllerParameterType.Int)
                    hasAttackIndexParameter = true;
                else if (parameter.nameHash == JumpTriggerHash && parameter.type == AnimatorControllerParameterType.Trigger)
                    hasJumpTriggerParameter = true;
                else if (parameter.nameHash == IsGroundedParameterHash && parameter.type == AnimatorControllerParameterType.Bool)
                    hasIsGroundedParameter = true;
            }
        }
        playerHealth = GetComponent<PlayerHealth>();
        if (playerHealth != null)
        {
            lastKnownHealth = playerHealth.MaxHealth;
            playerHealth.HealthChanged += HandleHealthChanged;
        }
        standingSize = box.size;
        standingOffset = box.offset;
        body.freezeRotation = true;
        playerInput = GetComponent<PlayerInputReader>();
        controlLock = GetComponent<PlayerControlLock>();
        spriteFacing = GetComponent<PlayerSpriteFacing>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        meleeAttack = GetComponent<PlayerMeleeAttack>();
        playerInput.MoveChanged += value => horizontalInput = value;
        playerInput.CrouchChanged += value => crouchHeld = value;
        playerInput.JumpPressed += () => jumpRequested = true;
        playerInput.BasicAttackPressed += HandleAttackPressed;
        playerInput.DashPressed += HandleDashPressed;
    }

    private void HandleDashPressed() => TryStartDash();

    public bool TryStartDash()
    {
        if (!isActiveAndEnabled || playerInput == null || !playerInput.isActiveAndEnabled
            || !body.simulated || IsDashing || DashCooldownRemaining > 0f || dashDistance <= 0f
            || playerHealth != null && playerHealth.IsDead || controlLock != null && controlLock.IsLocked
            || hitStopped || InventoryUI.GameplayInputSuppressedThisFrame || SceneFadeTransition.IsRunning || Time.timeScale <= 0f) return false;
        dashDirection = spriteFacing != null ? spriteFacing.FacingDirection
            : spriteRenderer != null ? (spriteRenderer.flipX ? -1f : 1f) : (horizontalInput < 0f ? -1f : 1f);
        dashTimeRemaining = Mathf.Max(.01f, dashDuration);
        dashSpeed = dashDistance / dashTimeRemaining;
        dashReadyAt = Time.time + Mathf.Max(0f, dashCooldown);
        collisionModeBeforeDash = body.collisionDetectionMode;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        IsDashing = true;
        EmitAfterimage();
        return true;
    }

    private void EndDash()
    {
        if (!IsDashing) return;
        IsDashing = false;
        dashTimeRemaining = 0f;
        if (body != null)
        {
            body.collisionDetectionMode = collisionModeBeforeDash;
            body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
        }
        spriteFacing?.RefreshFacingFromInput();
    }

    private void EmitAfterimage()
    {
        nextAfterimageAt = Time.time + Mathf.Max(.01f, afterimageInterval);
        if (dashAfterimages && spriteRenderer != null && spriteRenderer.enabled && spriteRenderer.sprite != null)
            PlayerDashAfterimage.Create(spriteRenderer, afterimageLifetime, afterimageTint);
    }

    private void FixedUpdate()
    {
        if (hitStopped && Time.unscaledTime < hitStopUntil)
        {
            EndDash();
            body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
            return;
        }
        if (playerHealth != null && playerHealth.IsDead)
        {
            EndDash();
            jumpRequested = false;
            horizontalInput = 0f;
            body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
            if (hasIsMovingParameter) animator.SetBool(IsMovingParameterHash, false);
            return;
        }

        if (IsDashing && (dashTimeRemaining <= 0f || !playerInput.isActiveAndEnabled
            || controlLock != null && controlLock.IsLocked || InventoryUI.GameplayInputSuppressedThisFrame
            || SceneFadeTransition.IsRunning || Time.timeScale <= 0f)) EndDash();
        UpdateCrouch(crouchHeld && !dropping);
        float horizontalVelocity = horizontalInput * (playerInput.WalkHeld ? walkSpeed : moveSpeed) * (crouchHeld ? crouchSpeedMultiplier : 1f);
        if (IsDashing)
        {
            float stepTime = Mathf.Min(Time.fixedDeltaTime, dashTimeRemaining);
            // Cap the final partial physics step so distance stays correct for decimal durations.
            horizontalVelocity = dashDirection * dashSpeed * (stepTime / Time.fixedDeltaTime);
            dashTimeRemaining = Mathf.Max(0f, dashTimeRemaining - stepTime);
        }
        body.linearVelocity = new Vector2(horizontalVelocity, body.linearVelocity.y);
        bool grounded = IsGrounded();
        if (jumpRequested)
        {
            jumpRequested = false;
            if (grounded)
            {
                if (crouchHeld) StartCoroutine(DropThroughOneWayPlatform());
                else
                {
                    attackAnimationPlaying = false;
                    meleeAttack?.CancelComboSwing();
                    attackStateObserved = false;
                    bufferedNextAttack = false;
                    comboAttackIndex = 0;
                    attackAnimationEndedAt = float.NegativeInfinity;
                    if (hasAttackTriggerParameter) animator.ResetTrigger(AttackTriggerHash);
                    body.linearVelocity = new Vector2(body.linearVelocity.x, jumpVelocity);
                    grounded = false;
                    if (hasJumpTriggerParameter)
                    {
                        animator.ResetTrigger(JumpTriggerHash);
                        animator.SetTrigger(JumpTriggerHash);
                    }
                }
            }
        }
        if (hasIsMovingParameter)
            animator.SetBool(IsMovingParameterHash, IsDashing || !Mathf.Approximately(horizontalInput, 0f));
        if (hasIsGroundedParameter) animator.SetBool(IsGroundedParameterHash, grounded);
    }

    private void LateUpdate()
    {
        if (animator != null && !hitStopped && !attackAnimationPlaying && playerHealth != null && !playerHealth.IsDead)
        {
            var locomotion = animator.GetCurrentAnimatorStateInfo(0);
            bool walkingAnimation = locomotion.IsName("Running") || locomotion.IsName("Run") || locomotion.IsName("Walk");
            animator.speed = walkingAnimation && playerInput.WalkHeld && !IsDashing ? walkSpeed / Mathf.Max(.01f, moveSpeed) : 1f;
        }
        if (IsDashing && Time.time >= nextAfterimageAt) EmitAfterimage();
        if (hitStopped && Time.unscaledTime >= hitStopUntil)
        {
            if (animator != null) animator.speed = animatorSpeedBeforeHitStop;
            hitStopped = false;
        }
        if (!attackAnimationPlaying || animator == null) return;

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (IsCurrentAttackState(state))
        {
            attackStateObserved = true;
            if (state.normalizedTime >= meleeImpactNormalizedTime)
                meleeAttack?.ResolveComboHit(comboAttackIndex);
            if (state.normalizedTime >= 1f) FinishAttackAnimation();
            return;
        }

        if (animator.IsInTransition(0))
        {
            if (IsCurrentAttackState(animator.GetNextAnimatorStateInfo(0)))
                attackStateObserved = true;
            return;
        }

        if (attackStateObserved) FinishAttackAnimation();
    }

    private bool IsCurrentAttackState(AnimatorStateInfo state)
    {
        if (comboAttackIndex < 1 || comboAttackIndex > AttackStateHashes.Length) return false;
        int index = comboAttackIndex - 1;
        return state.fullPathHash == AttackStateHashes[index]
            || state.fullPathHash == RunAttackStateHashes[index];
    }

    private void FinishAttackAnimation()
    {
        meleeAttack?.CancelComboSwing();
        attackAnimationPlaying = false;
        attackStateObserved = false;
        attackAnimationEndedAt = Time.time;

        if (bufferedNextAttack)
        {
            bufferedNextAttack = false;
            StartAttack(comboAttackIndex % AttackStateHashes.Length + 1);
        }
        // Holding the same movement key does not emit another MoveChanged event.
        // Restore its direction after the swing; a buffered combo keeps its attack facing.
        spriteFacing?.RefreshFacingFromInput();
    }

    private void OnDestroy()
    {
        if (playerHealth != null) playerHealth.HealthChanged -= HandleHealthChanged;
        if (playerInput != null) playerInput.BasicAttackPressed -= HandleAttackPressed;
        if (playerInput != null) playerInput.DashPressed -= HandleDashPressed;
    }

    private void HandleHealthChanged(int currentHealth, int maxHealth)
    {
        bool tookDamage = currentHealth < lastKnownHealth;
        lastKnownHealth = currentHealth;
        if (!tookDamage || animator == null) return;
        meleeAttack?.CancelComboSwing();

        attackAnimationPlaying = false;
        attackStateObserved = false;
        bufferedNextAttack = false;
        comboAttackIndex = 0;
        attackAnimationEndedAt = float.NegativeInfinity;
        if (hasAttackTriggerParameter) animator.ResetTrigger(AttackTriggerHash);
        if (hasJumpTriggerParameter) animator.ResetTrigger(JumpTriggerHash);
        if (currentHealth <= 0)
        {
            if (hasDamagedTriggerParameter) animator.ResetTrigger(DamagedTriggerHash);
            if (hasDeadTriggerParameter) animator.SetTrigger(DeadTriggerHash);
            return;
        }

        if (hasDamagedTriggerParameter) animator.SetTrigger(DamagedTriggerHash);
    }

    private void HandleAttackPressed()
    {
        if (animator == null || (playerHealth != null && playerHealth.IsDead)) return;

        if (attackAnimationPlaying)
        {
            bufferedNextAttack = true;
            return;
        }

        bool canContinueCombo = Time.time - attackAnimationEndedAt <= ComboInputWindow;
        int nextAttackIndex = canContinueCombo ? comboAttackIndex % AttackStateHashes.Length + 1 : 1;
        StartAttack(nextAttackIndex);
    }

    private void StartAttack(int attackIndex)
    {
        bool isMoving = !Mathf.Approximately(horizontalInput, 0f);
        if (animator == null || (playerHealth != null && playerHealth.IsDead)
            || !hasAttackTriggerParameter || !hasAttackIndexParameter) return;

        comboAttackIndex = attackIndex;
        attackAnimationPlaying = true;
        attackStateObserved = false;
        attackAnimationEndedAt = float.NegativeInfinity;
        if (hasIsMovingParameter) animator.SetBool(IsMovingParameterHash, isMoving);
        animator.SetInteger(AttackIndexParameterHash, comboAttackIndex);
        animator.ResetTrigger(AttackTriggerHash);
        animator.SetTrigger(AttackTriggerHash);
        meleeAttack?.BeginComboSwing();
    }

    private void UpdateCrouch(bool shouldCrouch)
    {
        box.size = shouldCrouch ? new Vector2(standingSize.x, standingSize.y * .5f) : standingSize;
        box.offset = shouldCrouch ? standingOffset + Vector2.down * (standingSize.y * .25f) : standingOffset;
    }

    public bool SettleOnGround(float maxDistance = 10f)
    {
        Physics2D.SyncTransforms();
        ContactFilter2D filter = new();
        int mask = groundLayers.value;
        int monsterLayer = LayerMask.NameToLayer("Monster");
        if (monsterLayer >= 0) mask &= ~(1 << monsterLayer);
        filter.SetLayerMask(mask);
        filter.useTriggers = false;
        var hits = new RaycastHit2D[16];
        int count = box.Cast(Vector2.down, filter, hits, maxDistance);
        float distance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
            if (hits[i].normal.y > .5f) distance = Mathf.Min(distance, hits[i].distance);
        if (float.IsPositiveInfinity(distance)) return false;
        Vector3 position = transform.position + Vector3.down * Mathf.Max(0, distance - .01f);
        transform.position = position;
        body.position = position;
        body.linearVelocity = Vector2.zero;
        Physics2D.SyncTransforms();
        RefreshAnimationWhileControlLocked();
        return true;
    }

    private bool IsGrounded()
    {
        ContactFilter2D filter = new();
        int groundMask = groundLayers.value;
        int monsterLayer = LayerMask.NameToLayer("Monster");
        if (monsterLayer >= 0) groundMask &= ~(1 << monsterLayer);
        filter.SetLayerMask(groundMask);
        filter.useTriggers = false;
        return box.Cast(Vector2.down, filter, groundHits, groundCheckDistance) > 0;
    }

    /// <summary>Persistent players keep their Animator when a death reloads the scene.</summary>
    internal void ResetAfterRevive()
    {
        EndDash();
        horizontalInput = 0f;
        crouchHeld = false;
        jumpRequested = false;
        if (playerHealth != null) lastKnownHealth = playerHealth.CurrentHealth;
        RefreshAnimationWhileControlLocked();
        UpdateCrouch(false);
        if (body != null) body.linearVelocity = Vector2.zero;
        if (animator == null || animator.runtimeAnimatorController == null) return;
        if (hasDeadTriggerParameter) animator.ResetTrigger(DeadTriggerHash);
        if (hasDamagedTriggerParameter) animator.ResetTrigger(DamagedTriggerHash);
        if (hasJumpTriggerParameter) animator.ResetTrigger(JumpTriggerHash);
        if (hasAttackIndexParameter) animator.SetInteger(AttackIndexParameterHash, 0);
        int idle = Animator.StringToHash("Base Layer.Idle");
        if (animator.HasState(0, idle))
        {
            // Evaluate immediately, before the death fade reveals the revived player.
            animator.Play(idle, 0, 0f);
            animator.Update(0f);
        }
    }

    internal void RefreshAnimationWhileControlLocked()
    {
        EndDash();
        meleeAttack?.CancelComboSwing();
        jumpRequested = false;
        bufferedNextAttack = false;
        attackAnimationPlaying = false;
        attackStateObserved = false;
        comboAttackIndex = 0;
        attackAnimationEndedAt = float.NegativeInfinity;
        if (animator == null) return;
        if (hasAttackTriggerParameter) animator.ResetTrigger(AttackTriggerHash);
        if (hasIsMovingParameter) animator.SetBool(IsMovingParameterHash, false);
        if (hasIsGroundedParameter) animator.SetBool(IsGroundedParameterHash, IsGrounded());
    }

    private IEnumerator DropThroughOneWayPlatform()
    {
        if (dropping) yield break;
        dropping = true;
        ignoredPlatforms.Clear();
        int mask = oneWayPlatformLayers.value == 0 ? ~0 : oneWayPlatformLayers.value;
        foreach (Collider2D hit in Physics2D.OverlapBoxAll(box.bounds.center, box.bounds.size, 0f, mask))
            if (hit != box && hit.GetComponent<PlatformEffector2D>() != null)
            {
                Physics2D.IgnoreCollision(box, hit, true);
                ignoredPlatforms.Add(hit);
            }
        yield return new WaitForSeconds(dropThroughDuration);
        foreach (Collider2D hit in ignoredPlatforms) Physics2D.IgnoreCollision(box, hit, false);
        ignoredPlatforms.Clear();
        dropping = false;
    }
}
