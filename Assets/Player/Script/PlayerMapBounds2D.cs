using UnityEngine;

/// <summary>Keeps the entire player hitbox inside the current map, including during jumps.</summary>
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
[DefaultExecutionOrder(500)]
public sealed class PlayerMapBounds2D : MonoBehaviour
{
    private Rigidbody2D body;
    private BoxCollider2D hitbox;
    private Vector2 horizontalBounds;
    private bool configured;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        hitbox = GetComponent<BoxCollider2D>();
    }

    public void SetHorizontalBounds(Vector2 bounds)
    {
        horizontalBounds = new Vector2(Mathf.Min(bounds.x, bounds.y), Mathf.Max(bounds.x, bounds.y));
        configured = true;
        Constrain(false);
    }

    private void FixedUpdate() => Constrain(true);

    // Correct externally applied movement before the camera follows the player.
    private void LateUpdate() => Constrain(false);

    private void Constrain(bool limitNextStep)
    {
        if (!configured || body == null || hitbox == null) return;
        float halfWidth = hitbox.bounds.extents.x;
        float offset = transform.TransformVector(hitbox.offset).x;
        float minX = horizontalBounds.x + halfWidth - offset;
        float maxX = horizontalBounds.y - halfWidth - offset;
        if (minX > maxX) minX = maxX = (horizontalBounds.x + horizontalBounds.y) * .5f - offset;

        Vector2 position = body.position;
        float clampedX = Mathf.Clamp(position.x, minX, maxX);
        if (!Mathf.Approximately(position.x, clampedX))
        {
            position.x = clampedX;
            body.position = position;
        }

        Vector2 velocity = body.linearVelocity;
        if (limitNextStep)
            velocity.x = Mathf.Clamp(velocity.x,
                (minX - clampedX) / Time.fixedDeltaTime,
                (maxX - clampedX) / Time.fixedDeltaTime);
        else if ((clampedX <= minX && velocity.x < 0f) || (clampedX >= maxX && velocity.x > 0f))
            velocity.x = 0f;
        body.linearVelocity = velocity;
    }
}
