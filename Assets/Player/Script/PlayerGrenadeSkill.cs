using System;
using BehindTheGear.HideOut;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Hold E/V to sweep the throw angle, release to spend one of five carried grenades.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerSkills), typeof(PlayerInputReader))]
public sealed class PlayerGrenadeSkill : MonoBehaviour, IHideOutSaveParticipant
{
    [Header("Ammunition - never refills on scene travel or enable")]
    [SerializeField, Min(1)] private int capacity = 5;
    [SerializeField, Min(0)] private int remainingGrenades = 5;
    [SerializeField, Min(.05f)] private float throwCooldown = .45f;
    [Header("Ballistic throw")]
    [SerializeField, Min(.1f)] private float throwSpeed = 10.5f;
    [SerializeField, Min(.1f)] private float gravityScale = 1.6f;
    [SerializeField, Range(5, 85)] private float minimumAngle = 20;
    [SerializeField, Range(5, 85)] private float maximumAngle = 75;
    [SerializeField, Min(.1f)] private float angleSweepTime = 1.1f;
    [SerializeField] private LayerMask trajectoryBlockers = 1 << 6;
    [Header("Explosion")]
    [SerializeField, Min(1)] private int damage = 24;
    [SerializeField, Min(.1f)] private float blastRadius = 2.4f;
    [SerializeField, Min(.1f)] private float fuseSeconds = 1.6f;
    [Tooltip("Passed to existing PlayerHealth: default defense 11 reduces 15 attack power to 4 HP.")]
    [SerializeField, Min(1)] private int selfAttackPower = 15;
    [SerializeField, Min(.1f)] private float selfDamageRadius = 1.6f;
    [Header("Replaceable presentation")]
    [SerializeField] private Sprite grenadeSprite;
    [SerializeField] private Color trajectoryColor = new(1, .76f, .3f, .7f);
    private PlayerSkills skills;
    private PlayerInputReader input;
    private PlayerMovement2D movement;
    private Collider2D playerCollider;
    private LineRenderer trajectory;
    private Material trajectoryMaterial;
    private float aimStartedAt, readyAt, noticeUntil;
    private float facing = 1;
    private PlayerControlScheme aimScheme;
    private readonly Vector3[] trajectoryPoints = new Vector3[30];
    public bool IsAiming { get; private set; }
    public int Remaining => remainingGrenades;
    public int Capacity => capacity;
    public int Damage => damage;
    public float BlastRadius => blastRadius;
    public float FuseSeconds => fuseSeconds;
    public float Cooldown => throwCooldown;
    public float CooldownRemaining => Mathf.Max(0, readyAt - Time.time);
    public float AimAngle => Mathf.Lerp(Mathf.Min(minimumAngle, maximumAngle), Mathf.Max(minimumAngle, maximumAngle),
        Mathf.PingPong((Time.time - aimStartedAt) / Mathf.Max(.1f, angleSweepTime), 1));
    public float NoticeRemaining => Mathf.Max(0, noticeUntil - Time.unscaledTime);
    public string Notice { get; private set; }
    public string SaveKey => "player_grenades_v1";
    private bool CanAim => skills != null && skills.CanUseSkills && (movement == null || !movement.IsAttacking);

    private void Awake()
    {
        skills = GetComponent<PlayerSkills>(); input = GetComponent<PlayerInputReader>();
        movement = GetComponent<PlayerMovement2D>(); playerCollider = GetComponent<Collider2D>();
        remainingGrenades = Mathf.Clamp(remainingGrenades, 0, capacity);
    }
    private void OnEnable()
    {
        input.MoveChanged += UpdateFacing;
        SceneManager.sceneLoaded += SceneLoaded;
    }
    private void OnDisable()
    {
        CancelAim(); input.MoveChanged -= UpdateFacing; SceneManager.sceneLoaded -= SceneLoaded;
    }
    private void OnApplicationFocus(bool focused) { if (!focused) CancelAim(); }
    private void SceneLoaded(Scene scene, LoadSceneMode mode) => CancelAim();
    private void UpdateFacing(float direction) { if (direction != 0) facing = Mathf.Sign(direction); }
    private void Update()
    {
        if (IsAiming && (!CanAim || aimScheme != PlayerControlSchemeSettings.Current)) CancelAim();
        if (IsAiming) UpdateTrajectory();
    }
    public bool BeginAim()
    {
        if (!isActiveAndEnabled || !CanAim || IsAiming) return false;
        if (remainingGrenades <= 0)
        {
            ShowExhaustedNotice(); return false;
        }
        if (CooldownRemaining > 0) return false;
        IsAiming = true; aimStartedAt = Time.time; aimScheme = PlayerControlSchemeSettings.Current;
        if (TryGetComponent<PlayerSpriteFacing>(out var spriteFacing)) facing = spriteFacing.FacingDirection;
        EnsureTrajectory(); trajectory.enabled = true; UpdateTrajectory(); return true;
    }
    public void CancelAim()
    {
        IsAiming = false;
        if (trajectory != null) trajectory.enabled = false;
    }
    public bool TryThrow()
    {
        if (!IsAiming) return false;
        if (!CanAim || aimScheme != PlayerControlSchemeSettings.Current || remainingGrenades <= 0 || CooldownRemaining > 0) { CancelAim(); return false; }
        Vector2 origin = ThrowOrigin;
        Vector2 velocity = ThrowVelocity;
        CancelAim();
        // Spend on release only; cancellation, holding and scene changes consume nothing.
        remainingGrenades--; readyAt = Time.time + throwCooldown;
        if (remainingGrenades == 0) ShowExhaustedNotice();
        skills.BreakStealth();
        PlayerGrenadeProjectile.Launch(origin, velocity, gravityScale, fuseSeconds, damage, blastRadius,
            selfAttackPower, Mathf.Min(selfDamageRadius, blastRadius), gameObject, grenadeSprite);
        return true;
    }
    private void ShowExhaustedNotice() { Notice = "수류탄을 모두 소진했습니다."; noticeUntil = Time.unscaledTime + 2.5f; }
    private Vector2 ThrowOrigin => (playerCollider != null ? (Vector2)playerCollider.bounds.center : (Vector2)transform.position) + Vector2.up * .35f;
    private Vector2 ThrowVelocity
    {
        get
        {
            float radians = AimAngle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians) * facing, Mathf.Sin(radians)) * throwSpeed;
        }
    }
    private void EnsureTrajectory()
    {
        if (trajectory != null) return;
        var go = new GameObject("Grenade aim trajectory"); go.transform.SetParent(transform, false);
        trajectory = go.AddComponent<LineRenderer>();
        trajectoryMaterial = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
        trajectory.sharedMaterial = trajectoryMaterial; trajectory.useWorldSpace = true;
        trajectory.startWidth = .045f; trajectory.endWidth = .018f;
        trajectory.startColor = trajectoryColor; trajectory.endColor = new Color(trajectoryColor.r, trajectoryColor.g, trajectoryColor.b, .15f);
        trajectory.sortingOrder = 20;
        if (TryGetComponent<SpriteRenderer>(out var sprite)) trajectory.sortingLayerID = sprite.sortingLayerID;
    }
    private void UpdateTrajectory()
    {
        Vector2 origin = ThrowOrigin, velocity = ThrowVelocity, previous = origin;
        int count = trajectoryPoints.Length;
        trajectoryPoints[0] = origin;
        for (int i = 1; i < trajectoryPoints.Length; i++)
        {
            float t = fuseSeconds * i / (trajectoryPoints.Length - 1);
            Vector2 point = origin + velocity * t + .5f * Physics2D.gravity * gravityScale * t * t;
            var hit = Physics2D.Linecast(previous, point, trajectoryBlockers);
            trajectoryPoints[i] = hit.collider != null && !hit.collider.isTrigger ? hit.point : point;
            if (hit.collider != null && !hit.collider.isTrigger) { count = i + 1; break; }
            previous = point;
        }
        trajectory.positionCount = count;
        for (int i = 0; i < count; i++) trajectory.SetPosition(i, trajectoryPoints[i]);
    }
    [Serializable] private sealed class GrenadeSave { public int version = 1; public int remaining; }
    public string CaptureJson() => JsonUtility.ToJson(new GrenadeSave { remaining = remainingGrenades });
    public void ValidateJson(string json)
    {
        var data = JsonUtility.FromJson<GrenadeSave>(json);
        if (data == null || data.version != 1 || data.remaining < 0 || data.remaining > capacity)
            throw new ArgumentException("수류탄 저장 개수가 올바르지 않습니다.");
    }
    public void RestoreJson(string json)
    {
        ValidateJson(json); CancelAim(); remainingGrenades = JsonUtility.FromJson<GrenadeSave>(json).remaining;
    }
    private void OnDestroy() { if (trajectoryMaterial != null) Destroy(trajectoryMaterial); }
}
