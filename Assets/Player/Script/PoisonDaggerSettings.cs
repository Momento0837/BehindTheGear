using UnityEngine;

[CreateAssetMenu(menuName = "Behind The Gear/Poison Dagger Settings")]
public sealed class PoisonDaggerSettings : ScriptableObject
{
    public const string ResourcePath = "PlayerSkills/PoisonDaggerSettings";
    [Header("Throw")]
    [Min(0)] public float cooldown = 30f;
    [Min(.01f)] public float projectileSpeed = 12f;
    [Min(0)] public float spawnOffset = .6f;
    [Min(0)] public float centerLeadDistance = .4f;
    [Min(0)] public float sideOffset = .35f;
    [Header("Damage - snapshot at launch")]
    [Min(0)] public float hitDamageMultiplier = .4f;
    [Min(.01f)] public float poisonDuration = 4f;
    [Min(.01f)] public float poisonTickInterval = 1f;
    [Min(0)] public float poisonDamageMultiplier = .1f;
    [Header("Cleanup")]
    [Min(0)] public float cameraDespawnMargin = 3f;
    [Min(.01f)] public float maxLifetime = 5f;
    [Header("Projectile and collision")]
    public PlayerPoisonDaggerProjectile projectilePrefab;
    public LayerMask enemyLayers = 1 << 9;
    public LayerMask obstacleLayers = (1 << 6) | (1 << 8);
    [Tooltip("Existing side-view Ground platforms below the player's feet are floors, not top-down projectile blockers. Explicit surface markers override this.")]
    public bool ignoreWalkableGround = true;
    [Header("Presentation")]
    public Sprite daggerSprite;
    [Tooltip("The supplied dagger points diagonally up-right in its original image.")]
    public float spriteForwardAngle = 45f;
    [Min(.01f)] public float daggerLength = .75f;
    [Min(.01f)] public float hitboxWidth = .12f;
    public bool showTrail = true;
}
