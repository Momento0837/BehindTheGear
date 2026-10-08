using UnityEngine;

[RequireComponent(typeof(SpriteRenderer), typeof(PlayerInputReader))]
public sealed class PlayerSpriteFacing : MonoBehaviour
{
    public float FacingDirection { get; private set; } = 1f;

    private void Awake()
    {
        SpriteRenderer renderer = GetComponent<SpriteRenderer>();
        GetComponent<PlayerInputReader>().MoveChanged += direction =>
        {
            if (direction == 0f) return;
            FacingDirection = Mathf.Sign(direction);
            renderer.flipX = FacingDirection < 0f;
        };
    }
}
