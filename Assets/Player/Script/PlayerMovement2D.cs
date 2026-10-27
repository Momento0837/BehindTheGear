using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(PlayerInputReader))]
public sealed class PlayerMovement2D : MonoBehaviour
{
    [SerializeField, Min(0f)] private float moveSpeed = 7f;
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
    private bool attackStateObserved;
    private bool bufferedNextAttack;
    private int comboAttackIndex;
    private float attackAnimationEndedAt = float.NegativeInfinity;
    private Animator animator;
    private PlayerInputReader playerInput;
    private PlayerMeleeAttack meleeAttack;
    private PlayerHealth playerHealth;
    private int lastKnownHealth;
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
        body = GetComponent<Rigidbody2D>();
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
        meleeAttack = GetComponent<PlayerMeleeAttack>();
        playerInput.MoveChanged += value => horizontalInput = value;
        playerInput.CrouchChanged += value => crouchHeld = value;
        playerInput.JumpPressed += () => jumpRequested = true;
        playerInput.BasicAttackPressed += HandleAttackPressed;
    }

    private void FixedUpdate()
    {
        if (playerHealth != null && playerHealth.IsDead)
        {
            jumpRequested = false;
            horizontalInput = 0f;
            body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
            if (hasIsMovingParameter) animator.SetBool(IsMovingParameterHash, false);
            return;
        }

        UpdateCrouch(crouchHeld && !dropping);
        body.linearVelocity = new Vector2(horizontalInput * moveSpeed * (crouchHeld ? crouchSpeedMultiplier : 1f), body.linearVelocity.y);
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
            animator.SetBool(IsMovingParameterHash, !Mathf.Approximately(horizontalInput, 0f));
        if (hasIsGroundedParameter) animator.SetBool(IsGroundedParameterHash, grounded);
    }

    private void LateUpdate()
    {
        if (!attackAnimationPlaying || animator == null) return;

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (IsCurrentAttackState(state))
        {
            attackStateObserved = true;
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
        attackAnimationPlaying = false;
        attackStateObserved = false;
        attackAnimationEndedAt = Time.time;

        if (bufferedNextAttack)
        {
            bufferedNextAttack = false;
            StartAttack(comboAttackIndex % AttackStateHashes.Length + 1);
        }
    }

    private void OnDestroy()
    {
        if (playerHealth != null) playerHealth.HealthChanged -= HandleHealthChanged;
        if (playerInput != null) playerInput.BasicAttackPressed -= HandleAttackPressed;
    }

    private void HandleHealthChanged(int currentHealth, int maxHealth)
    {
        bool tookDamage = currentHealth < lastKnownHealth;
        lastKnownHealth = currentHealth;
        if (!tookDamage || animator == null) return;

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
        meleeAttack?.PlayComboEffect(comboAttackIndex);
    }

    private void UpdateCrouch(bool shouldCrouch)
    {
        box.size = shouldCrouch ? new Vector2(standingSize.x, standingSize.y * .5f) : standingSize;
        box.offset = shouldCrouch ? standingOffset + Vector2.down * (standingSize.y * .25f) : standingOffset;
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

    internal void RefreshAnimationWhileControlLocked()
    {
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
