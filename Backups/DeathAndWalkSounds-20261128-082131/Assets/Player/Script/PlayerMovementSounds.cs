using UnityEngine;

/// <summary>Foot contacts follow the body animation; loose accents follow turns and successful jumps.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMovement2D), typeof(PlayerInputReader), typeof(Rigidbody2D))]
public sealed class PlayerMovementSounds : MonoBehaviour
{
    private PlayerMovement2D movement;
    private PlayerInputReader input;
    private PlayerControlLock controls;
    private PlayerHealth health;
    private Rigidbody2D body;
    private float horizontalInput;
    private int lastInputDirection;
    private int lastContactFrame = -1;
    private float previousX;
    private float cinematicMotionUntil = float.NegativeInfinity;
    private bool wasCinematicWalking;
    private bool cinematicFirstContactPending;
    private bool footstepsActive;
    private bool fallingInGarbage;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement2D>();
        input = GetComponent<PlayerInputReader>();
        controls = GetComponent<PlayerControlLock>();
        health = GetComponent<PlayerHealth>();
        body = GetComponent<Rigidbody2D>();
    }

    private void OnEnable()
    {
        previousX = transform.position.x;
        horizontalInput = 0f;
        lastInputDirection = 0;
        lastContactFrame = -1;
        fallingInGarbage = false;
        cinematicMotionUntil = float.NegativeInfinity;
        wasCinematicWalking = false;
        cinematicFirstContactPending = false;
        input.MoveChanged += OnMoveChanged;
        movement.JumpStarted += OnJumpStarted;
    }

    private bool CanHearMovement => GameSoundService.CurrentMap != GameSoundMap.None
        && Time.timeScale > 0f && !SceneFadeTransition.IsRunning && !CutsceneVideoSequence.IsAnyVisible
        && (health == null || !health.IsDead) && !movement.IsDashing;

    private bool CanWalkFromInput => movement.isActiveAndEnabled && input.isActiveAndEnabled
        && body.simulated && (controls == null || !controls.IsLocked)
        && !InventoryUI.GameplayInputSuppressedThisFrame && !PlayerSkillDevelopmentMenu.IsOpen
        && GetComponent<InventoryUI>()?.EquipmentTutorialActive != true;

    private void OnMoveChanged(float value)
    {
        horizontalInput = value;
        if (Mathf.Approximately(value, 0f))
        {
            // Remember the previous direction across key release (A -> neutral -> D).
            StopFootsteps();
            return;
        }
        if (!CanHearMovement || !CanWalkFromInput)
        {
            lastInputDirection = 0;
            return;
        }
        int direction = value < 0f ? -1 : 1;
        bool reversed = lastInputDirection != 0 && direction != lastInputDirection;
        lastInputDirection = direction;
        if (reversed && movement.IsGroundedForStory && body.linearVelocity.y <= .2f)
            GameSoundService.PlayLooseFootstep();
    }

    private void OnJumpStarted()
    {
        // Fired by movement only after a grounded jump succeeds, not by an airborne W press
        // or W interacting with a portal. Its separate source survives stopping foot contacts.
        if (!CanHearMovement || !CanWalkFromInput) return;
        StopFootsteps();
        GameSoundService.PlayLooseFootstep();
    }

    private void Update()
    {
        RefreshCinematicStart();
        float distance = Mathf.Abs(transform.position.x - previousX);
        previousX = transform.position.x;
        bool audible = CanHearMovement;
        bool grounded = movement.IsGroundedForStory;

        // The arrival sequence explicitly plays its impact before HardLanding; do not duplicate it.
        if (!audible || GameSoundService.CurrentMap != GameSoundMap.Garbage
            || StorySceneFlow.GarbageArrivalOwnsMotion || movement.IsCinematicWalking || !CanWalkFromInput)
            fallingInGarbage = false;
        else if (!grounded && body.linearVelocity.y < -.2f)
            fallingInGarbage = true;
        else if (grounded && fallingInGarbage)
        {
            fallingInGarbage = false;
            GameSoundService.PlayGarbageLanding();
        }

        if (!audible || !CanWalkFromInput) lastInputDirection = 0;
        // Story walking disables the input/movement scripts; portal walks also disable body simulation.
        // Only actual short travel counts, so a teleport cannot emit a footstep.
        float delta = Mathf.Max(.001f, Time.unscaledDeltaTime);
        if (!movement.IsCinematicWalking) cinematicMotionUntil = float.NegativeInfinity;
        else if (distance > .0005f && distance <= Mathf.Max(.1f, movement.RunningSpeed * delta * 2f))
            cinematicMotionUntil = Time.unscaledTime + .08f;
        // Rendering can sample between physics ticks; avoid cutting a clip on those stationary frames.
        if (!audible || !CanHearFootContact) StopFootsteps();
    }

    private bool CanHearFootContact
    {
        get
        {
            bool grounded = movement.IsGroundedForStory;
            // Ground probes can still touch the floor during the first physics tick of takeoff.
            if (body.simulated && body.linearVelocity.y > .2f) return false;
            bool manualWalk = CanWalkFromInput && !Mathf.Approximately(horizontalInput, 0f) && grounded;
            bool cinematicWalk = movement.IsCinematicWalking
                && (cinematicFirstContactPending || Time.unscaledTime < cinematicMotionUntil)
                && (grounded || !body.simulated);
            return manualWalk || cinematicWalk;
        }
    }

    // Running.anim invokes this at frames 0 and 21. Animator speed/pauses/loops own the timing.
    public void OnFootstepContact()
    {
        RefreshCinematicStart();
        if (!isActiveAndEnabled || lastContactFrame == Time.frameCount || !CanHearMovement || !CanHearFootContact) return;
        lastContactFrame = Time.frameCount;
        cinematicFirstContactPending = false;
        footstepsActive = GameSoundService.PlayFootstep() != null;
    }

    private void RefreshCinematicStart()
    {
        if (wasCinematicWalking == movement.IsCinematicWalking) return;
        wasCinematicWalking = movement.IsCinematicWalking;
        // Frame 0 can be evaluated before the first physics displacement is sampled.
        cinematicFirstContactPending = wasCinematicWalking;
        cinematicMotionUntil = float.NegativeInfinity;
    }

    private void StopFootsteps()
    {
        if (footstepsActive) GameSoundService.StopFootstep();
        footstepsActive = false;
    }

    private void OnDisable()
    {
        if (input != null) input.MoveChanged -= OnMoveChanged;
        if (movement != null) movement.JumpStarted -= OnJumpStarted;
        horizontalInput = 0f;
        lastInputDirection = 0;
        fallingInGarbage = false;
        StopFootsteps();
    }
}
