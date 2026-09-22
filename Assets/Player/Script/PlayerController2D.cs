using System.Collections;
using System.Collections.Generic;
using UnityEngine;
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
    }

    private void Update()
    {
        if (!controlsEnabled || Keyboard.current == null) return;

        Key left = controlScheme == ControlScheme.WASD ? Key.A : Key.LeftArrow;
        Key right = controlScheme == ControlScheme.WASD ? Key.D : Key.RightArrow;
        Key up = controlScheme == ControlScheme.WASD ? Key.W : Key.UpArrow;
        Key down = controlScheme == ControlScheme.WASD ? Key.S : Key.DownArrow;

        moveLeft = Keyboard.current[left].isPressed;
        moveRight = Keyboard.current[right].isPressed;
        downHeld = Keyboard.current[down].isPressed;
        // Space is the common jump key, regardless of the selected movement scheme.
        if (Keyboard.current[up].wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame)
            jumpPressed = true;
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
