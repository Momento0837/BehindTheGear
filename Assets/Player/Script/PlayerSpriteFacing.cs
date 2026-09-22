using UnityEngine;

[RequireComponent(typeof(SpriteRenderer), typeof(PlayerInputReader))]
public sealed class PlayerSpriteFacing : MonoBehaviour
{
    private void Awake()
    {
        SpriteRenderer renderer = GetComponent<SpriteRenderer>();
        GetComponent<PlayerInputReader>().MoveChanged += direction =>
        {
            if (direction != 0f) renderer.flipX = direction < 0f;
        };
    }
}
