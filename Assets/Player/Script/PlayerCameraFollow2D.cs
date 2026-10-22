using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Follows the player horizontally and frames jumps with a small vertical camera shift.</summary>
[DisallowMultipleComponent]
public sealed class PlayerCameraFollow2D : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector2 followOffset = new(0f, -2.5f);
    [SerializeField] private bool followVertical;
    [SerializeField] private float risingCameraY = .5f;
    [SerializeField] private float fallingCameraY = .1f;
    [SerializeField, Min(0f)] private float verticalSpeedThreshold = .05f;
    [SerializeField] private bool clampToHorizontalBounds;
    [SerializeField] private Vector2 horizontalWorldBounds = new(-26.18f, 26.18f);
    [SerializeField, Min(.01f)] private float smoothTime = .22f;
    [SerializeField, Min(0f)] private float maxFollowSpeed = 100f;
    [SerializeField, Min(0f)] private float lookAheadDistance = 1.25f;
    [SerializeField, Min(.01f)] private float lookAheadSmoothTime = .25f;
    [SerializeField, Min(0f)] private float lookAheadSpeedThreshold = .1f;

    private Rigidbody2D targetBody;
    private Camera attachedCamera;
    private Vector3 followVelocity;
    private float lookAhead;
    private float lookAheadVelocity;
    private float cameraZ;
    private float nextTargetSearchTime;

    private void Awake()
    {
        cameraZ = transform.position.z;
        attachedCamera = GetComponent<Camera>();
        CacheTargetBody();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void Start()
    {
        if (TryFindTarget()) SnapToTarget();
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            if (Time.unscaledTime < nextTargetSearchTime || !TryFindTarget()) return;
            SnapToTarget();
        }

        float horizontalSpeed = targetBody != null ? targetBody.linearVelocity.x : 0f;
        float desiredLookAhead = Mathf.Abs(horizontalSpeed) >= lookAheadSpeedThreshold
            ? Mathf.Sign(horizontalSpeed) * lookAheadDistance
            : 0f;
        lookAhead = Mathf.SmoothDamp(
            lookAhead,
            desiredLookAhead,
            ref lookAheadVelocity,
            lookAheadSmoothTime,
            Mathf.Infinity,
            Time.deltaTime);

        Vector3 desiredPosition = target.position + new Vector3(followOffset.x + lookAhead, followOffset.y, 0f);
        if (!followVertical)
        {
            float verticalSpeed = targetBody != null ? targetBody.linearVelocity.y : 0f;
            float desiredCameraY = verticalSpeed > verticalSpeedThreshold
                ? risingCameraY
                : fallingCameraY;
            desiredPosition.y = desiredCameraY;
        }
        desiredPosition.z = cameraZ;
        Vector3 smoothedPosition = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref followVelocity,
            smoothTime,
            maxFollowSpeed,
            Time.deltaTime);

        float clampedX = ClampHorizontalPosition(smoothedPosition.x);
        if (!Mathf.Approximately(clampedX, smoothedPosition.x)) followVelocity.x = 0f;
        smoothedPosition.x = clampedX;
        transform.position = smoothedPosition;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (TryFindTarget()) SnapToTarget();
    }

    private bool TryFindTarget()
    {
        if (target == null)
        {
            PlayerMovement2D player = FindFirstObjectByType<PlayerMovement2D>();
            if (player == null)
            {
                nextTargetSearchTime = Time.unscaledTime + .5f;
                return false;
            }

            target = player.transform;
        }

        CacheTargetBody();
        nextTargetSearchTime = 0f;
        return true;
    }

    private void CacheTargetBody()
    {
        targetBody = target != null ? target.GetComponent<Rigidbody2D>() : null;
    }

    private void SnapToTarget()
    {
        lookAhead = 0f;
        lookAheadVelocity = 0f;
        followVelocity = Vector3.zero;
        Vector3 position = target.position + new Vector3(followOffset.x, followOffset.y, 0f);
        if (!followVertical) position.y = fallingCameraY;
        position.x = ClampHorizontalPosition(position.x);
        position.z = cameraZ;
        transform.position = position;
    }

    private float ClampHorizontalPosition(float x)
    {
        if (!clampToHorizontalBounds || attachedCamera == null || !attachedCamera.orthographic)
            return x;

        float halfCameraWidth = attachedCamera.orthographicSize * attachedCamera.aspect;
        float minCameraX = horizontalWorldBounds.x + halfCameraWidth;
        float maxCameraX = horizontalWorldBounds.y - halfCameraWidth;
        if (minCameraX > maxCameraX)
            return (horizontalWorldBounds.x + horizontalWorldBounds.y) * .5f;

        return Mathf.Clamp(x, minCameraX, maxCameraX);
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }
}
