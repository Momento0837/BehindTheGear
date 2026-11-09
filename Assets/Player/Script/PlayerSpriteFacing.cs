using UnityEngine;

[RequireComponent(typeof(SpriteRenderer), typeof(PlayerInputReader))]
public sealed class PlayerSpriteFacing : MonoBehaviour
{
    public float FacingDirection { get; private set; } = 1f;
    public Vector2 AimDirection => input != null ? input.AimDirection : Vector2.right;
    private SpriteRenderer spriteRenderer;
    private PlayerMovement2D movement;
    private PlayerInputReader input;
    private float latestInput;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        movement = GetComponent<PlayerMovement2D>();
        input = GetComponent<PlayerInputReader>();
        input.MoveChanged += HandleMove;
    }

    private void HandleMove(float direction)
    {
        latestInput = direction;
        RefreshFacingFromInput();
    }

    internal void RefreshFacingFromInput()
    {
        if (latestInput == 0f || movement != null && movement.IsDashing) return;
        FacingDirection = Mathf.Sign(latestInput);
        spriteRenderer.flipX = FacingDirection < 0f;
    }

    private void OnDestroy() { if (input != null) input.MoveChanged -= HandleMove; }
}
