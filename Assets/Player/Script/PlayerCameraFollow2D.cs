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
    private const float MysteryDialogueLowerViewportFraction = .1f;

    private Rigidbody2D targetBody;
    private Camera attachedCamera;
    private UnityEngine.Rendering.Universal.PixelPerfectCamera perspectivePixelSnap;
    private Vector3 followVelocity;
    private float lookAhead;
    private float lookAheadVelocity;
    private float cameraZ;
    private float nextTargetSearchTime;
    private readonly List<Bounds> mapArtwork = new();
    private ConversationDialogue dialogueOwner;
    private Transform dialoguePlayer;
    private Bounds dialogueSubjects;
    private float? storyMinimumCameraX;
    private bool hasDialogueLens;
    private float normalOrthographicSize;
    private float normalFieldOfView;
    private float lensVelocity;
    private Transform deathTarget;
    private Transform walkingCompanion;
    private float walkingFocusWeight;
    private bool pursuitFocus;
    private float? pursuitWorldX;
    private bool pursuitTimedPan, pursuitPanBlur;
    private float pursuitPanDuration, pursuitPanElapsed, pursuitPanStartX;
    private float pursuitPanStartLens, pursuitZoomRatio = .9f;
    private float pursuitBlurVelocity;
    private bool mysteryDialogue;
    private float dialogueLowerOffset;
    private float dialogueLowerVelocity;
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
                    y = Mathf.Max(y, floor.y - dialogueLowerOffset - (ray.GetPoint(distance).y - renderedY));
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

    public void BeginDialogueFocus(ConversationDialogue owner, Transform player, bool lowerForMysteryNpc = false)
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
        mysteryDialogue = lowerForMysteryNpc;
        dialoguePlayer = player;
        // Animated sprite bounds vary between frames; keep a steady composition for this conversation.
        dialogueSubjects = SubjectBounds(player != null ? player : target);
        dialogueSubjects.Encapsulate(SubjectBounds(owner.transform));
    }

    public void EndDialogueFocus(ConversationDialogue owner)
    {
        if (dialogueOwner != owner) return;
        // Return to the actual speaker's player, without the NPC framing's residual motion.
        if (dialoguePlayer != null) { target = dialoguePlayer; CacheTargetBody(); }
        lookAhead = lookAheadVelocity = 0f;
        followVelocity = Vector3.zero;
        dialogueOwner = null;
        mysteryDialogue = false;
        dialoguePlayer = null;
    }

    public void BeginPursuitFocus()
    {
        if (attachedCamera == null || !isActiveAndEnabled) return;
        BeginDialogueFocusForDeath();
        pursuitFocus = true;
        pursuitZoomRatio = .9f;
        pursuitPanDuration = 0f;
        pursuitWorldX = null;
        pursuitTimedPan = pursuitPanBlur = false;
        PrototypeScreenEffects.StoryPanBlurPixels = 0f;
        pursuitBlurVelocity = 0f;
    }

    public void AimPursuitView(bool rightSide, float duration = 0f, bool motionBlur = false, bool returnToNormalZoom = false)
    {
        if (!pursuitFocus) return;
        float playerX = target != null ? target.position.x + followOffset.x : transform.position.x;
        pursuitWorldX = rightSide && clampToHorizontalBounds ? horizontalWorldBounds.y
            : rightSide ? playerX + 8f : playerX;
        pursuitPanDuration = Mathf.Max(0f, duration);
        pursuitPanElapsed = 0f;
        pursuitPanStartX = transform.position.x;
        pursuitPanStartLens = attachedCamera.orthographic ? attachedCamera.orthographicSize : attachedCamera.fieldOfView;
        pursuitZoomRatio = returnToNormalZoom ? 1f : .9f;
        pursuitTimedPan = pursuitPanDuration > 0f;
        pursuitPanBlur = pursuitTimedPan && motionBlur;
        PrototypeScreenEffects.StoryPanBlurPixels = 0f;
        pursuitBlurVelocity = 0f;
    }

    private float StepPursuitPan(float destinationX, float deltaTime)
    {
        pursuitPanElapsed = Mathf.Min(pursuitPanDuration, pursuitPanElapsed + Mathf.Max(0f, deltaTime));
        float progress = pursuitPanDuration > 0f ? pursuitPanElapsed / pursuitPanDuration : 1f;
        // Absorb floating-point accumulation at the end of an exact-duration pan.
        if (progress >= 1f - .00001f) progress = 1f;
        if (progress >= 1f) pursuitTimedPan = false;
        // Quintic easing starts and ends with zero velocity AND zero acceleration.
        // Pan and lens use the same clock, including the Garbage return zoom-out.
        float eased = progress * progress * progress * (progress * (progress * 6f - 15f) + 10f);
        float normalLens = attachedCamera.orthographic ? normalOrthographicSize : normalFieldOfView;
        float lens = Mathf.Lerp(pursuitPanStartLens, normalLens * pursuitZoomRatio, eased);
        if (attachedCamera.orthographic) attachedCamera.orthographicSize = lens;
        else attachedCamera.fieldOfView = lens;
        lensVelocity = 0f;
        // Zoom changes the visible map bounds; clamp after applying this frame's lens.
        return Mathf.Lerp(pursuitPanStartX, ClampHorizontalPosition(destinationX), eased);
    }

    private void UpdatePursuitBlur(float displacementX, float deltaTime)
    {
        if (!pursuitPanBlur || attachedCamera == null || deltaTime <= 0f) return;
        float z = target != null ? target.position.z : 0f;
        Vector3 center = new Vector3(transform.position.x, transform.position.y, z);
        float pixelsPerUnit = Mathf.Abs(attachedCamera.WorldToScreenPoint(center + Vector3.right).x
            - attachedCamera.WorldToScreenPoint(center).x);
        // A fixed exposure duration makes the blur proportional to actual screen-space speed,
        // independent of frame rate. Short or clamped movements naturally produce less blur.
        float desired = Mathf.Clamp(Mathf.Abs(displacementX) / deltaTime * pixelsPerUnit / 120f, 0f, 48f);
        float current = PrototypeScreenEffects.StoryPanBlurPixels;
        float smooth = desired > current ? .035f : .1f;
        current = Mathf.SmoothDamp(current, desired, ref pursuitBlurVelocity,
            smooth, Mathf.Infinity, deltaTime);
        if (!pursuitTimedPan && desired < .01f && current < .01f)
        {
            current = pursuitBlurVelocity = 0f;
            pursuitPanBlur = false;
        }
        PrototypeScreenEffects.StoryPanBlurPixels = Mathf.Clamp(current, 0f, 48f);
    }

    public bool PursuitPositionSettled
    {
        get
        {
            if (!pursuitFocus || !pursuitWorldX.HasValue || attachedCamera == null) return true;
            if (pursuitTimedPan || (pursuitPanBlur && PrototypeScreenEffects.StoryPanBlurPixels > .1f)) return false;
            return Mathf.Abs(transform.position.x - ClampHorizontalPosition(pursuitWorldX.Value)) < .12f;
        }
    }

    public bool PursuitViewSettled
    {
        get
        {
            if (!pursuitFocus || !pursuitWorldX.HasValue || attachedCamera == null) return true;
            if (!PursuitPositionSettled) return false;
            float lens = attachedCamera.orthographic ? attachedCamera.orthographicSize : attachedCamera.fieldOfView;
            float desiredLens = (attachedCamera.orthographic ? normalOrthographicSize : normalFieldOfView) * pursuitZoomRatio;
            return Mathf.Abs(lens - desiredLens) < .03f;
        }
    }

    public void EndPursuitFocus()
    {
        pursuitFocus = false;
        pursuitWorldX = null;
        pursuitTimedPan = pursuitPanBlur = false;
        pursuitPanDuration = 0f;
        pursuitBlurVelocity = 0f;
        PrototypeScreenEffects.StoryPanBlurPixels = 0f;
    }

    public void BeginWalkingFocus(Transform companion)
    {
        if (companion == null || attachedCamera == null || !isActiveAndEnabled) return;
        BeginDialogueFocusForDeath();
        walkingCompanion = companion;
        walkingFocusWeight = 0;
    }

    public void SetWalkingFocusWeight(float weight) => walkingFocusWeight = Mathf.Clamp01(weight);

    public void EndWalkingFocus()
    {
        walkingCompanion = null;
        walkingFocusWeight = 0;
        if (dialogueOwner == null && deathTarget == null && !pursuitFocus) RestoreDialogueLens();
    }

    private void RestoreDialogueLens()
    {
        EndPursuitFocus();
        mysteryDialogue = false;
        dialogueLowerOffset = dialogueLowerVelocity = 0f;
        deathTarget = null;
        walkingCompanion = null;
        walkingFocusWeight = 0;
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
        if (walkingCompanion != null && dialogueOwner == null && player != null)
        {
            // Shift the usual player-follow target toward the pair without losing its
            // smoothed movement look-ahead (including the settling motion when they stop).
            desiredPosition.x += (walkingCompanion.position.x - player.position.x) * .5f * walkingFocusWeight;
            float walkingLens = desiredLens * Mathf.Lerp(1f, .9f, walkingFocusWeight);
            if (attachedCamera.orthographic) attachedCamera.orthographicSize = walkingLens;
            else attachedCamera.fieldOfView = walkingLens;
            return;
        }
        float targetLowerOffset = 0f;
        if (pursuitFocus) desiredLens *= pursuitZoomRatio;
        if (dialogueOwner != null && player != null)
        {
            Bounds subjects = dialogueSubjects;
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
            if (mysteryDialogue) targetLowerOffset = halfHeight * 2f * MysteryDialogueLowerViewportFraction;
        }
        // Ease the lower composition and floor allowance together, including the return to gameplay.
        dialogueLowerOffset = Mathf.SmoothDamp(dialogueLowerOffset, targetLowerOffset,
            ref dialogueLowerVelocity, dialogueSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
        desiredPosition.y -= dialogueLowerOffset;
        float lens = UpdateFocusLens(desiredLens, Time.unscaledDeltaTime);
        if (dialogueOwner == null && !pursuitFocus && Mathf.Abs(lens - desiredLens) < .001f && dialogueLowerOffset < .001f)
            RestoreDialogueLens();
    }

    private float UpdateFocusLens(float desiredLens, float deltaTime)
    {
        float currentLens = attachedCamera.orthographic ? attachedCamera.orthographicSize : attachedCamera.fieldOfView;
        // A timed pursuit owns its lens through StepPursuitPan; do not add a second smoothing tail.
        if (pursuitFocus && pursuitPanDuration > 0f) return currentLens;
        float lensSmoothTime = pursuitFocus ? .12f : dialogueSmoothTime;
        float lens = Mathf.SmoothDamp(currentLens, desiredLens, ref lensVelocity,
            lensSmoothTime, Mathf.Infinity, deltaTime);
        if (attachedCamera.orthographic) attachedCamera.orthographicSize = lens;
        else attachedCamera.fieldOfView = lens;
        return lens;
    }

    public void SetStoryBounds(Vector2 bounds)
    {
        storyMinimumCameraX = null;
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

    // Keep the entrance player centered, then allow walking to the visible left edge.
    // Camera-center limits and player/world-edge limits are different coordinate ranges.
    public float CenterStoryEntrance(float playerX)
    {
        storyMinimumCameraX = playerX;
        if (TryFindTarget()) SnapToTarget();
        if (attachedCamera == null) return playerX;
        var plane = new Plane(Vector3.forward, Vector3.zero);
        Ray ray = attachedCamera.ViewportPointToRay(new Vector3(0, .5f, 0));
        return plane.Raycast(ray, out float distance) ? ray.GetPoint(distance).x : playerX;
    }

    private void Awake()
    {
        cameraZ = transform.position.z;
        attachedCamera = GetComponent<Camera>();
        CacheTargetBody();
    }

    private void OnEnable()
    {
        // Pixel Perfect's orthographic grid cannot stabilize multiple perspective depth planes.
        // Keep the perspective follow continuous instead of quantizing its view each render.
        if (attachedCamera != null && !attachedCamera.orthographic)
        {
            var pixelCamera = GetComponent<UnityEngine.Rendering.Universal.PixelPerfectCamera>();
            if (pixelCamera != null && pixelCamera.enabled)
            {
                perspectivePixelSnap = pixelCamera;
                pixelCamera.enabled = false;
                attachedCamera.ResetWorldToCameraMatrix();
            }
        }
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

    public float StoryLookOffset { get; set; }
    public bool CanLookRight => ClampHorizontalPosition(transform.position.x + .7f) > transform.position.x + .1f;

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

        Vector3 desiredPosition = target.position + new Vector3(followOffset.x + lookAhead + StoryLookOffset, followOffset.y, 0f);
        if (!followVertical)
        {
            float verticalSpeed = targetBody != null ? targetBody.linearVelocity.y : 0f;
            float desiredCameraY = verticalSpeed > verticalSpeedThreshold
                ? risingCameraY
                : fallingCameraY;
            desiredPosition.y = desiredCameraY;
        }
        bool dialogueTransition = hasDialogueLens;
        bool focusedOnSubject = dialogueOwner != null || deathTarget != null || walkingCompanion != null || pursuitFocus;
        // Zoom out gently, but resume responsive player tracking immediately.
        if (dialogueTransition && !focusedOnSubject)
            desiredPosition.x = target.position.x + followOffset.x + StoryLookOffset;
        UpdateDialogueFraming(ref desiredPosition);
        // Clamp the cinematic destination before smoothing, so the pan eases into the map edge.
        if (pursuitFocus && pursuitWorldX.HasValue)
            desiredPosition.x = ClampHorizontalPosition(pursuitWorldX.Value);
        desiredPosition.z = cameraZ;
        float trackingSmoothTime = focusedOnSubject ? dialogueSmoothTime : smoothTime;
        if (walkingCompanion != null && dialogueOwner == null && deathTarget == null)
            trackingSmoothTime = Mathf.Lerp(smoothTime, dialogueSmoothTime, walkingFocusWeight);
        Vector3 smoothedPosition = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref followVelocity,
            trackingSmoothTime,
            maxFollowSpeed,
            dialogueTransition ? Time.unscaledDeltaTime : Time.deltaTime);

        if (pursuitFocus && pursuitTimedPan && pursuitWorldX.HasValue)
        {
            smoothedPosition.x = StepPursuitPan(pursuitWorldX.Value, Time.unscaledDeltaTime);
            followVelocity.x = 0f;
        }
        float clampedX = ClampHorizontalPosition(smoothedPosition.x);
        if (!Mathf.Approximately(clampedX, smoothedPosition.x)) followVelocity.x = 0f;
        smoothedPosition.x = clampedX;
        float clampedY = ClampFocusHeight(smoothedPosition.y);
        if (!Mathf.Approximately(clampedY, smoothedPosition.y)) followVelocity.y = 0f;
        smoothedPosition.y = clampedY;
        UpdatePursuitBlur(smoothedPosition.x - transform.position.x, Time.unscaledDeltaTime);
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

    public bool TryGetRightMapEdge(out Vector3 edge)
    {
        edge = new Vector3(horizontalWorldBounds.y, 0f, 0f);
        if (!clampToHorizontalBounds || attachedCamera == null) return false;

        // Use the same depth plane that limits the camera. With perspective parallax,
        // the playable x boundary alone can lie beyond the visible artwork's right edge.
        float minX = float.NegativeInfinity, maxX = float.PositiveInfinity;
        LimitForPlane(horizontalWorldBounds.x, horizontalWorldBounds.y, 0f, ref minX, ref maxX);
        foreach (Bounds art in mapArtwork)
        {
            float artMinX = float.NegativeInfinity, artMaxX = float.PositiveInfinity;
            LimitForPlane(art.min.x + .03f, art.max.x - .03f, art.center.z, ref artMinX, ref artMaxX);
            if (artMaxX >= maxX) continue;
            maxX = artMaxX;
            edge = new Vector3(art.max.x - .03f, 0f, art.center.z);
        }
        return true;
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
        if (storyMinimumCameraX.HasValue) minCameraX = storyMinimumCameraX.Value;
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
        // Render-only boundary correction must not feed back into next frame's SmoothDamp.
        camera.worldToCameraMatrix = view * Matrix4x4.Translate(-correction);
    }

    private void OnDisable()
    {
        RestoreDialogueLens();
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        RenderPipelineManager.beginCameraRendering -= ClampAtRender;
        if (attachedCamera != null) attachedCamera.ResetWorldToCameraMatrix();
        if (perspectivePixelSnap != null)
        {
            perspectivePixelSnap.enabled = true;
            perspectivePixelSnap = null;
        }
    }
}
