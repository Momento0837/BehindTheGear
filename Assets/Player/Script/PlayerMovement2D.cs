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
    private readonly List<Collider2D> ignoredPlatforms = new();

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        box = GetComponent<BoxCollider2D>();
        standingSize = box.size;
        standingOffset = box.offset;
        body.freezeRotation = true;
        PlayerInputReader input = GetComponent<PlayerInputReader>();
        input.MoveChanged += value => horizontalInput = value;
        input.CrouchChanged += value => crouchHeld = value;
        input.JumpPressed += () => jumpRequested = true;
    }

    private void FixedUpdate()
    {
        UpdateCrouch(crouchHeld && !dropping);
        body.linearVelocity = new Vector2(horizontalInput * moveSpeed * (crouchHeld ? crouchSpeedMultiplier : 1f), body.linearVelocity.y);
        if (!jumpRequested) return;
        jumpRequested = false;
        if (!IsGrounded()) return;
        if (crouchHeld) StartCoroutine(DropThroughOneWayPlatform());
        else body.linearVelocity = new Vector2(body.linearVelocity.x, jumpVelocity);
    }

    private void UpdateCrouch(bool shouldCrouch)
    {
        box.size = shouldCrouch ? new Vector2(standingSize.x, standingSize.y * .5f) : standingSize;
        box.offset = shouldCrouch ? standingOffset + Vector2.down * (standingSize.y * .25f) : standingOffset;
    }

    private bool IsGrounded()
    {
        ContactFilter2D filter = new();
        filter.SetLayerMask(groundLayers);
        filter.useTriggers = false;
        return box.Cast(Vector2.down, filter, new RaycastHit2D[4], groundCheckDistance) > 0;
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
