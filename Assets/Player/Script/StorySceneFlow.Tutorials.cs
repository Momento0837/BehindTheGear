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
    private IEnumerator GarbageArrival()
    {
        var controls = AcquireTutorialControls();
        var animator = player.GetComponent<Animator>();
        bool animatorEnabled = animator != null && animator.enabled;
        try
        {
            var camera = Camera.main;
            Vector3 position = player.transform.position;
            if (camera != null)
                position.y = camera.ViewportToWorldPoint(new Vector3(.5f, 1.2f, Mathf.Abs(position.z - camera.transform.position.z))).y + 2f;
            else position.y += 12f;
            player.transform.position = position;
            var body = player.GetComponent<Rigidbody2D>(); body.position = position; body.linearVelocity = Vector2.zero;
            Physics2D.SyncTransforms();
            while (SceneFadeTransition.IsRunning) yield return null;
            float elapsed = 0;
            do { elapsed += Time.deltaTime; yield return null; } while (!player.IsGroundedForStory && elapsed < 12);
            if (!player.IsGroundedForStory) player.SettleOnGround(40);
            var landing = Resources.Load<AnimationClip>("Story/HardLanding");
            if (landing != null && animator != null)
            {
                animator.enabled = false;
                for (float t = 0; t < landing.length; t += Time.deltaTime) { landing.SampleAnimation(player.gameObject, t); yield return null; }
                animator.enabled = animatorEnabled;
            }
            yield return Say("garbage_monologue");
        }
        finally { if (animator != null) animator.enabled = animatorEnabled; ReleaseTutorialControls(); }
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
        var lines = ConversationDialogue.GetGreetingLines(Resources.Load<TextAsset>("Dialogue/neighborhood_conversation"), 3);
        float ppu = player.GetComponent<SpriteRenderer>().sprite.pixelsPerUnit;
        float stopDistance = 50f / ppu;
        float direction = Mathf.Sign(entrance.transform.position.x - player.transform.position.x);
        bubble.Begin(dialogue.TalkingImage, lines);
        player.SetCinematicWalking(true); npcAnimator?.SetBool("IsMoving", true);
        player.GetComponent<PlayerSpriteFacing>()?.FaceAttack(direction);
        bool moving = true;
        try
        {
            while (direction * (entrance.transform.position.x - body.position.x) > stopDistance)
            {
                float speed = player.RunningSpeed;
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
            player.SetCinematicWalking(false); npcAnimator?.SetBool("IsMoving", false);
            ReleaseTutorialControls(); moving = false;
            // Dialogue timing never changes running speed or delays control at the destination.
            while (bubble != null && bubble.IsPlaying) yield return null;
        }
        finally
        {
            if (moving)
            {
                body.linearVelocity = new Vector2(0, body.linearVelocity.y);
                player.SetCinematicWalking(false); npcAnimator?.SetBool("IsMoving", false);
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
