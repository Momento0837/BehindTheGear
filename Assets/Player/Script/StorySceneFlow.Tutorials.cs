using System.Collections;
using System.Linq;
using UnityEngine;
using BehindTheGear.HideOut;

public sealed partial class StorySceneFlow
{
    private PlayerControlLock tutorialControls;
    private PlayerControlLock AcquireTutorialControls()
    {
        tutorialControls = player.GetComponent<PlayerControlLock>(); tutorialControls?.Acquire(); return tutorialControls;
    }
    private void ReleaseTutorialControls()
    {
        var held = tutorialControls; tutorialControls = null; held?.Release();
    }
    private void OnDisable()
    {
        ReleaseTutorialControls(); PrototypeScreenEffects.OverridePixels = -1;
    }
    public static bool GarbageArrivalOwnsMotion { get; private set; }
    private IEnumerator GarbageArrival()
    {
        GarbageArrivalOwnsMotion = true;
        AcquireTutorialControls();
        var animator = player.GetComponent<Animator>();
        bool animatorEnabled = animator != null && animator.enabled;
        var body = player.GetComponent<Rigidbody2D>();
        float damping = body.linearDamping;
        var mapBounds = player.GetComponent<PlayerMapBounds2D>();
        // The outgoing portal may still have this disabled; arrival now owns its restoration.
        bool boundsEnabled = mapBounds != null && (mapBounds.enabled || mapBounds.HasHorizontalBounds);
        var camera = Camera.main;
        var follow = camera != null ? camera.GetComponent<PlayerCameraFollow2D>() : null;
        bool followEnabled = follow != null && follow.enabled;
        try
        {
            player.SetCinematicWalking(false);
            if (mapBounds != null) mapBounds.enabled = false;
            // Resolve the original landing height before moving outside the camera.
            body.simulated = true;
            player.SettleOnGround(40);
            Vector3 landingPosition = player.transform.position;
            body.simulated = false;
            body.linearVelocity = Vector2.zero;
            // Disabling follow can restore its camera lens/matrix. Compute the spawn AFTER that.
            if (follow != null) follow.enabled = false;
            Vector3 position = landingPosition + new Vector3(8, 12, 0);
            if (camera != null)
            {
                camera.ResetWorldToCameraMatrix();
                var plane = new Plane(Vector3.forward, landingPosition);
                var ray = camera.ViewportPointToRay(new Vector3(1.12f, 1.15f, 0));
                if (plane.Raycast(ray, out float distance)) position = ray.GetPoint(distance);
            }
            position.z = landingPosition.z;
            player.transform.position = position;
            body.position = position;
            Physics2D.SyncTransforms();
            // Scene startup places the complete character offscreen while the fade is still black.
            while (SceneFadeTransition.IsRunning) yield return null;
            // Let the old portal finish releasing its input lock before launching the new motion.
            yield return null;
            float gravity = Mathf.Max(.1f, -Physics2D.gravity.y * body.gravityScale);
            float height = Mathf.Max(.1f, position.y - landingPosition.y);
            float step = Time.fixedDeltaTime;
            float flight = (Mathf.Sqrt(step * step + 8f * height / gravity) - step) * .5f;
            float physicsFlight = Mathf.Ceil(flight / step) * step;
            body.linearDamping = 0;
            body.simulated = true;
            body.linearVelocity = new Vector2((landingPosition.x - position.x) / physicsFlight, 0);
            float elapsed = 0;
            do
            {
                yield return new WaitForFixedUpdate();
                elapsed += Time.fixedDeltaTime;
            } while ((elapsed < flight * .5f || !player.IsGroundedForStory) && elapsed < 12);
            // Horizontal speed is assigned once; Unity gravity drives the entire visible arc.
            body.linearVelocity = new Vector2(0, body.linearVelocity.y);
            body.linearDamping = damping;
            if (mapBounds != null) mapBounds.enabled = boundsEnabled;
            if (follow != null) follow.enabled = followEnabled;
            var landing = Resources.Load<AnimationClip>("Story/HardLanding");
            if (landing != null && animator != null)
            {
                animator.enabled = false;
                for (float t = 0; t < landing.length; t += Time.deltaTime) { landing.SampleAnimation(player.gameObject, t); yield return null; }
                animator.enabled = animatorEnabled;
            }
            yield return Say("Garbage/garbage_monologue");
        }
        finally
        {
            GarbageArrivalOwnsMotion = false;
            if (body != null) { body.linearDamping = damping; body.simulated = true; }
            if (mapBounds != null) mapBounds.enabled = boundsEnabled;
            if (follow != null) follow.enabled = followEnabled;
            if (animator != null) animator.enabled = animatorEnabled;
            ReleaseTutorialControls();
        }
    }

    private IEnumerator GarbageEquipmentReaction()
    {
        var reaction = player.gameObject.AddComponent<AutomaticNpcBubble>();
        reaction.PlayerReaction = true;
        try
        {
            reaction.Begin(dialogue.TalkingImage, new[] { "?" });
            while (reaction.IsPlaying) yield return null;
        }
        finally
        {
            if (reaction != null) Destroy(reaction);
        }
    }

    private IEnumerator RevealGarbagePoint(Transform point)
    {
        try
        {
            yield return ChromaticRamp(8, 3, .8f);
            PrototypeScreenEffects.OverridePixels = 5;
            point.gameObject.SetActive(true);
            yield return ChromaticRamp(5, 0, 1f);
        }
        finally
        {
            PrototypeScreenEffects.OverridePixels = -1;
        }
    }
    private IEnumerator ChromaticRamp(float from, float to, float duration)
    {
        for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
        { PrototypeScreenEffects.OverridePixels = Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, t / duration)); yield return null; }
        PrototypeScreenEffects.OverridePixels = to;
    }

    private IEnumerator WalkToHideout()
    {
        var entrance = InScene<HideOutEntrance>().FirstOrDefault();
        if (entrance == null || selena == null) yield break;
        var controls = AcquireTutorialControls();
        var body = player.GetComponent<Rigidbody2D>();
        var npcAnimator = selena.GetComponent<Animator>();
        var bubble = selena.gameObject.AddComponent<AutomaticNpcBubble>();
        var lines = ConversationDialogue.GetGreetingLines(Resources.Load<TextAsset>("Dialogue/Neighborhood/neighborhood_conversation"), 3);
        float ppu = player.GetComponent<SpriteRenderer>().sprite.pixelsPerUnit;
        float stopDistance = 50f / ppu;
        // The F hint appears at the automatic stop, so that position must be interactive.
        var entranceTrigger = entrance.GetComponent<BoxCollider2D>();
        if (entranceTrigger != null)
        {
            var size = entranceTrigger.size;
            size.x = Mathf.Max(size.x, 2f * (stopDistance + .25f) / Mathf.Max(.001f, Mathf.Abs(entrance.transform.lossyScale.x)));
            entranceTrigger.size = size;
        }
        entrance.GetComponent<Interactable2D>().AllowFromEitherSide = true;
        float direction = Mathf.Sign(entrance.transform.position.x - player.transform.position.x);
        var walkingCamera = Camera.main != null ? Camera.main.GetComponent<PlayerCameraFollow2D>() : null;
        walkingCamera?.BeginWalkingFocus(selena);
        float focusElapsed = 0f;
        float zoomOutDistance = -1f;
        bubble.Begin(dialogue.TalkingImage, lines);
        player.SetCinematicWalking(true); npcAnimator?.SetBool("IsMoving", true);
        player.GetComponent<PlayerSpriteFacing>()?.FaceAttack(direction);
        bool moving = true;
        try
        {
            while (direction * (entrance.transform.position.x - body.position.x) > stopDistance)
            {
                float remaining = Mathf.Max(0f, direction * (entrance.transform.position.x - body.position.x) - stopDistance);
                focusElapsed += Time.deltaTime;
                if (zoomOutDistance < 0f && bubble.RevealedLineCount >= lines.Length)
                    zoomOutDistance = Mathf.Max(.01f, remaining);
                float focus = zoomOutDistance < 0f ? Mathf.SmoothStep(0, 1, focusElapsed / .8f)
                    : Mathf.SmoothStep(0, 1, Mathf.Clamp01(remaining / zoomOutDistance));
                walkingCamera?.SetWalkingFocusWeight(focus);
                float speed = player.RunningSpeed * .5f;
                var walkAnimator = player.GetComponent<Animator>();
                if (walkAnimator != null) walkAnimator.speed = .3f;
                if (npcAnimator != null) npcAnimator.speed = .3f;
                body.linearVelocity = new Vector2(direction * speed, body.linearVelocity.y);
                var target = new Vector3(player.transform.position.x - direction * SelenaFollowDistance, selena.position.y, selena.position.z);
                selena.position = Vector3.MoveTowards(selena.position, target, speed * Time.deltaTime);
                var sprite = selena.GetComponent<SpriteRenderer>(); if (sprite != null) sprite.flipX = direction < 0;
                yield return null;
            }
            var state = player.GetComponent<PrototypeTutorialState>() ?? player.gameObject.AddComponent<PrototypeTutorialState>();
            state.SelenaArrived = true;
            var hint = entrance.GetComponent<WorldInteractionHint>() ?? entrance.gameObject.AddComponent<WorldInteractionHint>();
            hint.ShowUntilDismissed = true;
            body.linearVelocity = new Vector2(0, body.linearVelocity.y);
            walkingCamera?.EndWalkingFocus();
            player.SetCinematicWalking(false); npcAnimator?.SetBool("IsMoving", false);
            if (npcAnimator != null) npcAnimator.speed = 1f;
            ReleaseTutorialControls(); moving = false;
            // Dialogue timing never changes running speed or delays control at the destination.
            while (bubble != null && bubble.IsPlaying) yield return null;
        }
        finally
        {
            walkingCamera?.EndWalkingFocus();
            if (moving)
            {
                body.linearVelocity = new Vector2(0, body.linearVelocity.y);
                player.SetCinematicWalking(false); npcAnimator?.SetBool("IsMoving", false);
            if (npcAnimator != null) npcAnimator.speed = 1f;
                ReleaseTutorialControls();
            }
            if (bubble != null) Destroy(bubble);
        }
    }
}

public sealed class PrototypeTutorialState : MonoBehaviour
{
    public bool SelenaArrived;
}
