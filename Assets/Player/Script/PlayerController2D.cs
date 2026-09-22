using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>2D side-scroller player movement. Input is intentionally limited to the chosen control scheme.</summary>
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
public sealed class PlayerController2D : MonoBehaviour
{
    public enum ControlScheme { WASD, ArrowKeys }

    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveSpeed = 7f;
    [SerializeField, Min(0f)] private float jumpVelocity = 12f;
    [SerializeField, Range(0f, 1f)] private float crouchSpeedMultiplier = .7f;
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField] private LayerMask oneWayPlatformLayers;
    [SerializeField, Min(.01f)] private float groundCheckDistance = .08f;
    [SerializeField, Min(.05f)] private float dropThroughDuration = .25f;

    [Header("Runtime (read only)")]
    [SerializeField] private ControlScheme controlScheme;
    [SerializeField] private bool controlsEnabled;

    [Header("Attack Input Events")]
    [SerializeField] private UnityEvent onAttackZ;
    [SerializeField] private UnityEvent onAttackX;
    [SerializeField] private UnityEvent onAttackC;
    [SerializeField] private UnityEvent onAttackV;
    [SerializeField] private UnityEvent onPrimaryClick;
    [SerializeField] private UnityEvent onSecondaryClick;

    private Rigidbody2D body;
    private BoxCollider2D capsule;
    private SpriteRenderer playerRenderer;
    private Vector2 standingSize;
    private Vector2 standingOffset;
    private bool moveLeft;
    private bool moveRight;
    private bool downHeld;
    private bool jumpPressed;
    private bool dropping;
    private readonly List<Collider2D> ignoredPlatforms = new();

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        capsule = GetComponent<BoxCollider2D>();
        playerRenderer = GetComponent<SpriteRenderer>();
        standingSize = capsule.size;
        standingOffset = capsule.offset;
        body.freezeRotation = true;
        controlsEnabled = true; // Both keyboard layouts are always available.
    }

    private void Update()
    {
        if (!controlsEnabled || Keyboard.current == null) return;

        moveLeft = Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed;
        moveRight = Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed;
        downHeld = Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed;
        // Space is the common jump key, regardless of the selected movement scheme.
        if (Keyboard.current.wKey.wasPressedThisFrame || Keyboard.current.upArrowKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame)
            jumpPressed = true;

        if (Keyboard.current.zKey.wasPressedThisFrame) AttackZ();
        if (Keyboard.current.xKey.wasPressedThisFrame) AttackX();
        if (Keyboard.current.cKey.wasPressedThisFrame) AttackC();
        if (Keyboard.current.vKey.wasPressedThisFrame) AttackV();
        if (Mouse.current?.leftButton.wasPressedThisFrame == true) PrimaryClick();
        if (Mouse.current?.rightButton.wasPressedThisFrame == true) SecondaryClick();
    }

    private void FixedUpdate()
    {
        if (!controlsEnabled) return;

        bool grounded = IsGrounded();
        UpdateCrouch(downHeld && !dropping);

        float direction = (moveRight ? 1f : 0f) - (moveLeft ? 1f : 0f);
        float speed = moveSpeed * (downHeld ? crouchSpeedMultiplier : 1f);
        body.linearVelocity = new Vector2(direction * speed, body.linearVelocity.y);

        if (playerRenderer != null && direction != 0f)
            playerRenderer.flipX = direction < 0f;

        if (!jumpPressed) return;
        jumpPressed = false;
        if (!grounded) return; // Single jump only: no air jump.

        if (downHeld)
            StartCoroutine(DropThroughOneWayPlatform());
        else
            body.linearVelocity = new Vector2(body.linearVelocity.x, jumpVelocity);
    }

    public void SetControlScheme(ControlScheme scheme)
    {
        controlScheme = scheme;
        controlsEnabled = true;
    }

    // These methods are intentionally separate so attack logic, animation, or effects can be
    // assigned independently in the Inspector through the corresponding UnityEvent fields.
    public void AttackZ() => onAttackZ?.Invoke();
    public void AttackX() => onAttackX?.Invoke();
    public void AttackC() => onAttackC?.Invoke();
    public void AttackV() => onAttackV?.Invoke();
    public void PrimaryClick() => onPrimaryClick?.Invoke();
    public void SecondaryClick() => onSecondaryClick?.Invoke();

    private void UpdateCrouch(bool shouldCrouch)
    {
        if (shouldCrouch)
        {
            capsule.size = new Vector2(standingSize.x, standingSize.y * .5f);
            capsule.offset = standingOffset + Vector2.down * (standingSize.y * .25f);
        }
        else
        {
            capsule.size = standingSize;
            capsule.offset = standingOffset;
        }
    }

    private bool IsGrounded()
    {
        // Collider2D.Cast excludes this collider. The previous world BoxCast could hit the
        // player's own BoxCollider2D, incorrectly allowing an unlimited number of jumps.
        ContactFilter2D filter = new();
        filter.SetLayerMask(groundLayers);
        filter.useTriggers = false;
        RaycastHit2D[] hits = new RaycastHit2D[4];
        return capsule.Cast(Vector2.down, filter, hits, groundCheckDistance) > 0;
    }

    private IEnumerator DropThroughOneWayPlatform()
    {
        if (dropping) yield break;
        dropping = true;
        ignoredPlatforms.Clear();
        foreach (Collider2D platform in FindOverlappingOneWayPlatforms())
        {
            Physics2D.IgnoreCollision(capsule, platform, true);
            ignoredPlatforms.Add(platform);
        }

        yield return new WaitForSeconds(dropThroughDuration);

        foreach (Collider2D platform in ignoredPlatforms)
            Physics2D.IgnoreCollision(capsule, platform, false);
        ignoredPlatforms.Clear();
        dropping = false;
    }

    private Collider2D[] FindOverlappingOneWayPlatforms()
    {
        int layerMask = oneWayPlatformLayers.value == 0 ? ~0 : oneWayPlatformLayers.value;
        Collider2D[] hits = Physics2D.OverlapBoxAll(capsule.bounds.center, capsule.bounds.size, 0f, layerMask);
        return System.Array.FindAll(hits, hit => hit != capsule && hit.GetComponent<PlatformEffector2D>() != null);
    }
}
