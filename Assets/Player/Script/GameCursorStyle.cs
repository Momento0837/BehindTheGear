using UnityEngine;

public sealed class GameCursorStyle : ScriptableObject
{
    public Sprite sprite;
    public Vector2 size = new(32f, 32f);
}
