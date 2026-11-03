using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using System.Collections.Generic;

/// <summary>Follows the player horizontally and frames jumps with a small vertical camera shift.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
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

    [Header("NPC dialogue framing")]
    [SerializeField, Range(.3f, 1f)] private float dialogueZoomRatio = .75f;
    [SerializeField, Min(.01f)] private float dialogueSmoothTime = .8f;

    private Rigidbody2D targetBody;
    private Camera attachedCamera;
    private Vector3 followVelocity;
    private float lookAhead;
    private float lookAheadVelocity;
    private float cameraZ;
    private float nextTargetSearchTime;
    private readonly List<Bounds> mapArtwork = new();
    private ConversationDialogue dialogueOwner;
    private Transform dialoguePlayer;
    private bool hasDialogueLens;
    private float normalOrthographicSize;
    private float normalFieldOfView;
    private float lensVelocity;
    private Transform deathTarget;
    private readonly List<Vector2> focusFloorPlanes = new();

    private void CaptureFocusFloor()
    {
        focusFloorPlanes.Clear();
        RecordFocusFloor(0f);
        foreach (Bounds art in mapArtwork) RecordFocusFloor(art.center.z);
    }

    private void RecordFocusFloor(float z)
    {
        var plane = new Plane(Vector3.forward, new Vector3(0f, 0f, z));
        float bottom = float.PositiveInfinity;
        for (int i = 0; i < 2; i++)
        {
            Ray ray = attachedCamera.ViewportPointToRay(new Vector3(i, 0f, 0f));
            if (plane.Raycast(ray, out float distance)) bottom = Mathf.Min(bottom, ray.GetPoint(distance).y);
        }
        if (!float.IsInfinity(bottom)) focusFloorPlanes.Add(new Vector2(z, bottom));
    }

    private float ClampFocusHeight(float y)
    {
        if (!hasDialogueLens || attachedCamera == null) return y;
        float renderedY = attachedCamera.cameraToWorldMatrix.MultiplyPoint(Vector3.zero).y;
        foreach (Vector2 floor in focusFloorPlanes)
        {
            var plane = new Plane(Vector3.forward, new Vector3(0f, 0f, floor.x));
            for (int i = 0; i < 2; i++)
            {
                Ray ray = attachedCamera.ViewportPointToRay(new Vector3(i, 0f, 0f));
                if (plane.Raycast(ray, out float distance))
                    y = Mathf.Max(y, floor.y - (ray.GetPoint(distance).y - renderedY));
            }
        }
        return y;
    }

    public void BeginDeathFocus(Transform subject)
    {
        BeginDialogueFocusForDeath();
        deathTarget = subject;
    }

    private void BeginDialogueFocusForDeath()
    {
        if (attachedCamera == null) return;
        if (!hasDialogueLens)
        {
            normalOrthographicSize = attachedCamera.orthographicSize;
            normalFieldOfView = attachedCamera.fieldOfView;
            CaptureFocusFloor();
            hasDialogueLens = true;
        }
        lensVelocity = 0f;
    }

    public void BeginDialogueFocus(ConversationDialogue owner, Transform player)
    {
        if (owner == null || attachedCamera == null || !isActiveAndEnabled) return;
        if (!hasDialogueLens)
        {
            normalOrthographicSize = attachedCamera.orthographicSize;
            normalFieldOfView = attachedCamera.fieldOfView;
            CaptureFocusFloor();
            hasDialogueLens = true;
            lensVelocity = 0f;
        }
        dialogueOwner = owner;
        dialoguePlayer = player;
    }

    public void EndDialogueFocus(ConversationDialogue owner)
    {
        if (dialogueOwner != owner) return;
        dialogueOwner = null;
        dialoguePlayer = null;
    }

    private void RestoreDialogueLens()
    {
        deathTarget = null;
        if (hasDialogueLens && attachedCamera != null)
        {
            attachedCamera.orthographicSize = normalOrthographicSize;
            attachedCamera.fieldOfView = normalFieldOfView;
        }
        hasDialogueLens = false;
        dialogueOwner = null;
        dialoguePlayer = null;
        lensVelocity = 0f;
    }

    private static Bounds SubjectBounds(Transform subject)
    {
        var sprite = subject.GetComponent<SpriteRenderer>();
        return sprite != null && sprite.enabled && sprite.sprite != null
            ? sprite.bounds : new Bounds(subject.position, Vector3.one);
    }

    private void UpdateDialogueFraming(ref Vector3 desiredPosition)
    {
        if (!hasDialogueLens || attachedCamera == null) return;
        float desiredLens = attachedCamera.orthographic ? normalOrthographicSize : normalFieldOfView;
        if (deathTarget != null)
        {
            desiredPosition.x = SubjectBounds(deathTarget).center.x;
            desiredPosition.y = fallingCameraY;
            float zoom = Mathf.SmoothDamp(attachedCamera.orthographic ? attachedCamera.orthographicSize : attachedCamera.fieldOfView,
                desiredLens * .9f, ref lensVelocity, 1f, Mathf.Infinity, Time.unscaledDeltaTime);
            if (attachedCamera.orthographic) attachedCamera.orthographicSize = zoom;
            else attachedCamera.fieldOfView = zoom;
            return;
        }
        Transform player = dialoguePlayer != null ? dialoguePlayer : target;
        if (dialogueOwner != null && player != null)
        {
            Bounds subjects = SubjectBounds(player);
            subjects.Encapsulate(SubjectBounds(dialogueOwner.transform));
            float distance = Mathf.Max(.01f, Mathf.Abs(subjects.center.z - cameraZ));
            float normalHalfHeight = attachedCamera.orthographic ? normalOrthographicSize
                : distance * Mathf.Tan(normalFieldOfView * .5f * Mathf.Deg2Rad);
            // Leave room for both bodies while preserving the gameplay camera height.
            float halfHeight = Mathf.Max(normalHalfHeight * dialogueZoomRatio,
                (subjects.extents.x + 1f) / Mathf.Max(.1f, attachedCamera.aspect),
                (subjects.extents.y + 1f) / .85f);
            desiredLens = attachedCamera.orthographic ? halfHeight
                : Mathf.Clamp(2f * Mathf.Atan(halfHeight / distance) * Mathf.Rad2Deg, 1f, 179f);
            desiredPosition.x = subjects.center.x;
            desiredPosition.y -= .35f;
        }
        float currentLens = attachedCamera.orthographic ? attachedCamera.orthographicSize : attachedCamera.fieldOfView;
        float lens = Mathf.SmoothDamp(currentLens, desiredLens, ref lensVelocity,
            dialogueSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
        if (attachedCamera.orthographic) attachedCamera.orthographicSize = lens;
        else attachedCamera.fieldOfView = lens;
        if (dialogueOwner == null && Mathf.Abs(lens - desiredLens) < .001f)
            RestoreDialogueLens();
    }

    public void SetStoryBounds(Vector2 bounds)
    {
        horizontalWorldBounds = bounds;
        clampToHorizontalBounds = true;
        mapArtwork.Clear();
        foreach (var renderer in FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            if (renderer.gameObject.scene != gameObject.scene || !renderer.enabled || renderer.sprite == null) continue;
            bool isMapArt = renderer.sprite.name.Contains("Layer") ||
                (renderer.sprite.texture != null && renderer.sprite.texture.name.Contains("Layer"));
            Bounds art = renderer.bounds;
            float overlap = Mathf.Min(bounds.y, art.max.x) - Mathf.Max(bounds.x, art.min.x);
            if (isMapArt && art.size.x > 10f && overlap > (bounds.y - bounds.x) * .5f)
                mapArtwork.Add(art);
        }
        target = null;
        if (TryFindTarget()) SnapToTarget();
    }

    private void Awake()
    {
        cameraZ = transform.position.z;
        attachedCamera = GetComponent<Camera>();
        CacheTargetBody();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        RenderPipelineManager.beginCameraRendering -= ClampAtRender;
        RenderPipelineManager.beginCameraRendering += ClampAtRender;
    }

    private void Start()
    {
        if (TryFindTarget()) SnapToTarget();
        RenderPipelineManager.beginCameraRendering -= ClampAtRender;
        RenderPipelineManager.beginCameraRendering += ClampAtRender;
    }

    private void LateUpdate()
    {
        if (attachedCamera != null) attachedCamera.ResetWorldToCameraMatrix();
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
        bool dialogueTransition = hasDialogueLens;
        UpdateDialogueFraming(ref desiredPosition);
        desiredPosition.z = cameraZ;
        Vector3 smoothedPosition = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref followVelocity,
            dialogueTransition ? dialogueSmoothTime : smoothTime,
            maxFollowSpeed,
            dialogueTransition ? Time.unscaledDeltaTime : Time.deltaTime);

        float clampedX = ClampHorizontalPosition(smoothedPosition.x);
        if (!Mathf.Approximately(clampedX, smoothedPosition.x)) followVelocity.x = 0f;
        smoothedPosition.x = clampedX;
        float clampedY = ClampFocusHeight(smoothedPosition.y);
        if (!Mathf.Approximately(clampedY, smoothedPosition.y)) followVelocity.y = 0f;
        smoothedPosition.y = clampedY;
        transform.position = smoothedPosition;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RestoreDialogueLens();
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
        position.z = cameraZ;
        transform.position = position;
        if (attachedCamera != null) attachedCamera.ResetWorldToCameraMatrix();
        position.x = ClampHorizontalPosition(position.x);
        transform.position = position;
    }

    private float ClampHorizontalPosition(float x)
    {
        if (!clampToHorizontalBounds || attachedCamera == null)
            return x;

        float minCameraX = float.NegativeInfinity;
        float maxCameraX = float.PositiveInfinity;
        LimitForPlane(horizontalWorldBounds.x, horizontalWorldBounds.y, 0f, ref minCameraX, ref maxCameraX);
        foreach (Bounds art in mapArtwork)
            LimitForPlane(art.min.x + .03f, art.max.x - .03f, art.center.z, ref minCameraX, ref maxCameraX);
        if (minCameraX > maxCameraX)
            return (minCameraX + maxCameraX) * .5f;

        return Mathf.Clamp(x, minCameraX, maxCameraX);
    }

    private void LimitForPlane(float left, float right, float z, ref float minX, ref float maxX)
    {
        Plane plane = new Plane(Vector3.forward, new Vector3(0, 0, z));
        float renderedX = attachedCamera.cameraToWorldMatrix.MultiplyPoint(Vector3.zero).x;
        float leftOffset = float.PositiveInfinity;
        float rightOffset = float.NegativeInfinity;
        for (int i = 0; i < 4; i++)
        {
            Ray ray = attachedCamera.ViewportPointToRay(new Vector3(i % 2, i / 2, 0));
            if (!plane.Raycast(ray, out float distance)) return;
            float offset = ray.GetPoint(distance).x - renderedX;
            leftOffset = Mathf.Min(leftOffset, offset);
            rightOffset = Mathf.Max(rightOffset, offset);
        }
        minX = Mathf.Max(minX, left - leftOffset);
        maxX = Mathf.Min(maxX, right - rightOffset);
    }

    private void ClampAtRender(ScriptableRenderContext context, Camera camera)
    {
        if (camera != attachedCamera || !clampToHorizontalBounds) return;
        float renderedX = camera.cameraToWorldMatrix.MultiplyPoint(Vector3.zero).x;
        float deltaX = ClampHorizontalPosition(renderedX) - renderedX;
        float renderedY = camera.cameraToWorldMatrix.MultiplyPoint(Vector3.zero).y;
        float deltaY = ClampFocusHeight(renderedY) - renderedY;
        if (Mathf.Abs(deltaX) < .00001f && Mathf.Abs(deltaY) < .00001f) return;
        Matrix4x4 view = camera.worldToCameraMatrix;
        Vector3 correction = new Vector3(deltaX, deltaY, 0f);
        transform.position += correction;
        camera.worldToCameraMatrix = view * Matrix4x4.Translate(-correction);
        followVelocity.x = 0f;
    }

    private void OnDisable()
    {
        RestoreDialogueLens();
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        RenderPipelineManager.beginCameraRendering -= ClampAtRender;
        if (attachedCamera != null) attachedCamera.ResetWorldToCameraMatrix();
    }
}
