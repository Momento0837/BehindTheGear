using UnityEngine;

/// <summary>Starts footsteps on movement input, then chains clips while the player walks.</summary>
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
    private float nextFootstepAt;
    private float previousX;
    private float cinematicMotionUntil = float.NegativeInfinity;
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
        fallingInGarbage = false;
        cinematicMotionUntil = float.NegativeInfinity;
        input.MoveChanged += OnMoveChanged;
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
        bool starting = Mathf.Approximately(horizontalInput, 0f) && !Mathf.Approximately(value, 0f);
        horizontalInput = value;
        if (Mathf.Approximately(value, 0f)) StopFootsteps();
        else if (starting && CanHearMovement && CanWalkFromInput && movement.IsGroundedForStory)
            PlayNextFootstep(); // Do not wait for the next animation or physics frame.
    }

    private void Update()
    {
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

        bool manualWalk = CanWalkFromInput && !Mathf.Approximately(horizontalInput, 0f);
        // Story walking disables the input/movement scripts; portal walks also disable body simulation.
        // Only actual short travel counts, so a teleport cannot emit a footstep.
        float delta = Mathf.Max(.001f, Time.unscaledDeltaTime);
        if (!movement.IsCinematicWalking) cinematicMotionUntil = float.NegativeInfinity;
        else if (distance > .0005f && distance <= Mathf.Max(.1f, movement.RunningSpeed * delta * 2f))
            cinematicMotionUntil = Time.unscaledTime + .08f;
        // Rendering can sample between physics ticks; avoid cutting a clip on those stationary frames.
        bool cinematicWalk = movement.IsCinematicWalking && Time.unscaledTime < cinematicMotionUntil
            && (grounded || !body.simulated);
        if (!audible || (!grounded && !cinematicWalk) || (!manualWalk && !cinematicWalk))
        {
            StopFootsteps();
            return;
        }
        if (!footstepsActive || Time.unscaledTime >= nextFootstepAt) PlayNextFootstep();
    }

    private void PlayNextFootstep()
    {
        AudioClip clip = GameSoundService.PlayFootstep();
        footstepsActive = true;
        nextFootstepAt = Time.unscaledTime + Mathf.Max(.08f, clip != null ? clip.length : .1f);
    }

    private void StopFootsteps()
    {
        if (footstepsActive) GameSoundService.StopFootstep();
        footstepsActive = false;
        nextFootstepAt = 0f;
    }

    private void OnDisable()
    {
        if (input != null) input.MoveChanged -= OnMoveChanged;
        horizontalInput = 0f;
        fallingInGarbage = false;
        StopFootsteps();
    }
}
