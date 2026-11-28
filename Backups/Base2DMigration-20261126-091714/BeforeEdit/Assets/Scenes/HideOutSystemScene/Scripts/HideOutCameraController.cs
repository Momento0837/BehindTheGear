using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace BehindTheGear.HideOut
{
    public enum HideOutCameraState { Overview, FreeExplore, FacilityFocus }

    [DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class HideOutCameraController : MonoBehaviour
    {
        private Camera controlledCamera;
        private Vector3 overviewPosition;
        private Quaternion baseRotation;
        private float overviewSize, moveDuration;
        private Bounds explorationBounds;
        private bool configured;

        private float dragThresholdPixels, dragSensitivity, movementSmoothTime, maximumMoveSpeed;
        private bool useInertia;
        private float inertiaDamping, zoomStep, zoomSmoothTime, minimumSize, maximumSize;
        private float boundsPadding;
        private float leftDragMultiplier, overviewPanAllowance;
        private bool leftDrag;

        private Vector3 targetPosition, smoothVelocity, inertiaVelocity;
        private float targetSize, zoomVelocity;
        private Vector2 pressScreenPosition;
        private bool pointerPressed, pointerStartedOverUI, dragging, rotating;
        private int suppressClickUntilFrame = -1;

        public bool IsMoving { get; private set; }
        public bool IsOverview => State == HideOutCameraState.Overview && !IsMoving;
        public bool ShouldSuppressFacilityClick => dragging || Time.frameCount <= suppressClickUntilFrame
            || pointerPressed && Mouse.current != null
                && Vector2.Distance(Mouse.current.position.ReadValue(), pressScreenPosition) >= dragThresholdPixels;
        public HideOutCameraState State { get; private set; } = HideOutCameraState.Overview;
        public event Action MoveCompleted;

        public void Configure(Vector3 overview, Quaternion rotation, float size, float duration, Bounds bounds,
            HideOutTopViewPresentation presentation)
        {
            controlledCamera = GetComponent<Camera>();
            controlledCamera.orthographic = true;
            baseRotation = rotation;
            overviewPosition = overview;
            overviewSize = Mathf.Max(.1f, size);
            moveDuration = Mathf.Max(.05f, duration);
            explorationBounds = bounds;
            dragThresholdPixels = Mathf.Max(1f, presentation.dragThresholdPixels);
            dragSensitivity = Mathf.Max(.01f, presentation.dragSensitivity);
            leftDragMultiplier = Mathf.Clamp(presentation.leftDragMultiplier, .1f, 1f);
            overviewPanAllowance = Mathf.Max(0f, presentation.overviewPanAllowance);
            movementSmoothTime = Mathf.Max(.01f, presentation.movementSmoothTime);
            maximumMoveSpeed = Mathf.Max(.1f, presentation.maximumMoveSpeed);
            useInertia = presentation.useInertia;
            inertiaDamping = Mathf.Max(.1f, presentation.inertiaDamping);
            zoomStep = Mathf.Max(.1f, presentation.zoomStep);
            zoomSmoothTime = Mathf.Max(.01f, presentation.zoomSmoothTime);
            minimumSize = Mathf.Max(.1f, presentation.minimumOrthographicSize);
            maximumSize = Mathf.Max(minimumSize, presentation.maximumOrthographicSize);
            boundsPadding = Mathf.Max(0f, presentation.boundsPadding);
            configured = true;
            SnapToOverview();
        }

        private void Update()
        {
            if (!configured || controlledCamera == null || IsMoving) return;
            float deltaTime = Mathf.Max(.0001f, Time.unscaledDeltaTime);
            HandlePointer(deltaTime);
            if (State == HideOutCameraState.FreeExplore) UpdateFreeExplore(deltaTime);
        }

        public bool TryFocus(Transform anchor, float orthographicSize, Action completed)
        {
            if (!configured || IsMoving || anchor == null) return false;
            CancelManualMotion();
            State = HideOutCameraState.FacilityFocus;
            StartCoroutine(MoveRoutine(anchor.position, Mathf.Clamp(orthographicSize, minimumSize, maximumSize),
                HideOutCameraState.FacilityFocus, completed));
            return true;
        }

        public bool TryReturnToOverview(Action completed)
        {
            if (!configured || IsMoving) return false;
            CancelManualMotion();
            StartCoroutine(MoveRoutine(overviewPosition, overviewSize, HideOutCameraState.Overview, completed));
            return true;
        }

        public void SnapToOverview()
        {
            if (!configured) return;
            StopAllCoroutines();
            transform.SetPositionAndRotation(overviewPosition, baseRotation);
            controlledCamera.orthographicSize = overviewSize;
            overviewPosition = ClampPosition(overviewPosition, overviewSize);
            transform.position = overviewPosition;
            targetPosition = overviewPosition;
            targetSize = overviewSize;
            State = HideOutCameraState.Overview;
            IsMoving = false;
            CancelManualMotion();
        }

        private void HandlePointer(float deltaTime)
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 pointer = mouse.position.ReadValue();
            if (mouse.leftButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame)
            {
                pointerPressed = true;
                leftDrag = !mouse.middleButton.isPressed;
                dragging = rotating = false;
                pressScreenPosition = pointer;
                pointerStartedOverUI = IsPointerOverUI();
            }

            if (pointerPressed && (mouse.leftButton.isPressed || mouse.middleButton.isPressed))
            {
                if (!dragging && Vector2.Distance(pointer, pressScreenPosition) >= dragThresholdPixels)
                {
                    dragging = true;
                    rotating = false;
                    suppressClickUntilFrame = Time.frameCount + 1;
                    inertiaVelocity = Vector3.zero;
                }
                if (dragging && !pointerStartedOverUI && State != HideOutCameraState.FacilityFocus)
                {
                    Vector2 screenDelta = mouse.delta.ReadValue();
                    ApplyPan(screenDelta * (leftDrag ? leftDragMultiplier : 1f), deltaTime);
                }
            }

            if (pointerPressed && !mouse.leftButton.isPressed && !mouse.middleButton.isPressed)
            {
                if (dragging) suppressClickUntilFrame = Time.frameCount + 1;
                if (!useInertia || rotating || pointerStartedOverUI) inertiaVelocity = Vector3.zero;
                else if (leftDrag) inertiaVelocity *= .25f;
                pointerPressed = dragging = rotating = pointerStartedOverUI = false;
            }

            if (State != HideOutCameraState.FacilityFocus && !IsPointerOverUI())
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > .01f)
                {
                    targetSize = Mathf.Clamp(targetSize - Mathf.Sign(scroll) * zoomStep, minimumSize, maximumSize);
                    State = HideOutCameraState.FreeExplore;
                    inertiaVelocity = Vector3.zero;
                }
            }
        }

        private void ApplyPan(Vector2 screenDelta, float deltaTime)
        {
            float worldPerPixel = 2f * controlledCamera.orthographicSize / Mathf.Max(1f, Screen.height);
            Vector3 right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
            Vector3 up = Vector3.ProjectOnPlane(transform.up, Vector3.up).normalized;
            Vector3 movement = (-screenDelta.x * right - screenDelta.y * up) * worldPerPixel * dragSensitivity;
            movement.y = 0f;
            movement = Vector3.ClampMagnitude(movement, maximumMoveSpeed * deltaTime);
            Vector3 before = targetPosition;
            targetPosition = ClampPosition(targetPosition + movement, controlledCamera.orthographicSize);
            inertiaVelocity = Vector3.ClampMagnitude((targetPosition - before) / deltaTime, maximumMoveSpeed);
            transform.position = targetPosition;
            smoothVelocity = Vector3.zero;
            State = HideOutCameraState.FreeExplore;
        }

        private void UpdateFreeExplore(float deltaTime)
        {
            if (!pointerPressed && useInertia && inertiaVelocity.sqrMagnitude > .0001f)
            {
                targetPosition += inertiaVelocity * deltaTime;
                inertiaVelocity *= Mathf.Exp(-inertiaDamping * deltaTime);
                if (inertiaVelocity.sqrMagnitude < .0004f) inertiaVelocity = Vector3.zero;
            }
            controlledCamera.orthographicSize = Mathf.SmoothDamp(controlledCamera.orthographicSize, targetSize,
                ref zoomVelocity, zoomSmoothTime, Mathf.Infinity, deltaTime);
            Vector3 unclamped = targetPosition;
            targetPosition = ClampPosition(targetPosition, controlledCamera.orthographicSize);
            if (Mathf.Abs(unclamped.x - targetPosition.x) > .00001f) inertiaVelocity.x = smoothVelocity.x = 0f;
            if (Mathf.Abs(unclamped.z - targetPosition.z) > .00001f) inertiaVelocity.z = smoothVelocity.z = 0f;
            transform.position = targetPosition;
            transform.rotation = baseRotation;
        }

        private Vector3 ClampPosition(Vector3 candidate, float requestedSize)
        {
            candidate.y = overviewPosition.y;
            Vector2 minOffset = new(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 maxOffset = new(float.NegativeInfinity, float.NegativeInfinity);
            Vector3[] corners = { new(0f, 0f), new(1f, 0f), new(0f, 1f), new(1f, 1f) };
            float scale = requestedSize / Mathf.Max(.01f, controlledCamera.orthographicSize);
            Ray centerRay = controlledCamera.ViewportPointToRay(new Vector3(.5f, .5f));
            Vector3 centerHit = centerRay.origin + centerRay.direction * (-centerRay.origin.y / centerRay.direction.y);
            foreach (Vector3 corner in corners)
            {
                Ray ray = controlledCamera.ViewportPointToRay(corner);
                if (Mathf.Abs(ray.direction.y) < .0001f) continue;
                Vector3 hit = ray.origin + ray.direction * (-ray.origin.y / ray.direction.y);
                Vector3 scaledHit = centerHit + (hit - centerHit) * scale;
                Vector2 offset = new(scaledHit.x - transform.position.x, scaledHit.z - transform.position.z);
                minOffset = Vector2.Min(minOffset, offset);
                maxOffset = Vector2.Max(maxOffset, offset);
            }
            if (float.IsInfinity(minOffset.x)) return candidate;
            candidate.x = ClampAxis(candidate.x, explorationBounds.min.x + boundsPadding - minOffset.x,
                explorationBounds.max.x - boundsPadding - maxOffset.x,
                explorationBounds.center.x - (minOffset.x + maxOffset.x) * .5f);
            candidate.z = ClampAxis(candidate.z, explorationBounds.min.z + boundsPadding - minOffset.y,
                explorationBounds.max.z - boundsPadding - maxOffset.y,
                explorationBounds.center.z - (minOffset.y + maxOffset.y) * .5f);
            return candidate;
        }

        private float ClampAxis(float value, float minimum, float maximum, float fallback)
            => minimum <= maximum ? Mathf.Clamp(value, minimum, maximum)
                : Mathf.Clamp(value, fallback - overviewPanAllowance, fallback + overviewPanAllowance);

        private IEnumerator MoveRoutine(Vector3 target, float size, HideOutCameraState finalState, Action completed)
        {
            IsMoving = true;
            Vector3 startPosition = transform.position;
            Quaternion startRotation = transform.rotation;
            float startSize = controlledCamera.orthographicSize;
            float elapsed = 0f;
            while (elapsed < moveDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / moveDuration);
                float eased = t * t * (3f - 2f * t);
                transform.position = Vector3.LerpUnclamped(startPosition, target, eased);
                transform.rotation = Quaternion.SlerpUnclamped(startRotation, baseRotation, eased);
                controlledCamera.orthographicSize = Mathf.LerpUnclamped(startSize, size, eased);
                yield return null;
            }
            transform.SetPositionAndRotation(target, baseRotation);
            controlledCamera.orthographicSize = size;
            targetPosition = target;
            targetSize = size;
            State = finalState;
            IsMoving = false;
            completed?.Invoke();
            MoveCompleted?.Invoke();
        }

        private void CancelManualMotion()
        {
            smoothVelocity = inertiaVelocity = Vector3.zero;
            zoomVelocity = 0f;
            pointerPressed = dragging = rotating = pointerStartedOverUI = false;
        }

        private static bool IsPointerOverUI()
        {
            if (EventSystem.current == null || Mouse.current == null) return false;
            var pointer = new PointerEventData(EventSystem.current) { position = Mouse.current.position.ReadValue() };
            var hits = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            return hits.Count != 0;
        }

    }
}
