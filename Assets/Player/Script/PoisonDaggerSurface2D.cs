using UnityEngine;

/// <summary>Optional per-surface override. Does not change collisions for players or other weapons.</summary>
public sealed class PoisonDaggerSurface2D : MonoBehaviour
{
    public enum SurfaceKind { Floor, Obstacle }
    public SurfaceKind kind = SurfaceKind.Obstacle;
}
