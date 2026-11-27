using System.Collections;
using UnityEngine;

public sealed partial class StorySceneFlow
{
    private bool pursuitPlayed;

    private IEnumerator ShowPursuitBubble(string resourcePath, bool leftSide = false, float opacity = 1f)
    {
        var source = Resources.Load<TextAsset>(resourcePath);
        if (source == null)
        {
            Debug.LogError("[Pursuit] Missing dialogue JSON: " + resourcePath);
            yield break;
        }
        var bubble = gameObject.AddComponent<AutomaticNpcBubble>();
        bubble.ScreenRight = !leftSide;
        bubble.ScreenLeft = leftSide;
        bubble.Opacity = opacity;
        if (!leftSide)
        {
            var camera = Camera.main != null ? Camera.main.GetComponent<PlayerCameraFollow2D>() : null;
            if (camera != null && camera.TryGetRightMapEdge(out Vector3 edge))
                bubble.RightMapEdge = edge;
            else if (player != null && player.TryGetComponent<PlayerMapBounds2D>(out var bounds) && bounds.HasHorizontalBounds)
                bubble.RightMapEdge = new Vector3(bounds.HorizontalBounds.y, 0f, 0f);
        }
        DialoguePresentation.Set(bubble, true);
        try
        {
            bubble.Begin(dialogue.TalkingImage, ConversationDialogue.GetGreetingLines(source, int.MaxValue));
            while (bubble.IsPlaying) yield return null;
        }
        finally
        {
            DialoguePresentation.Set(bubble, false);
            if (bubble != null) Destroy(bubble);
        }
    }

    private IEnumerator PlayPursuitShout(string resourcePath)
    {
        var camera = Camera.main != null ? Camera.main.GetComponent<PlayerCameraFollow2D>() : null;
        bool peek = camera != null && camera.CanLookRight;
        float previousOffset = camera != null ? camera.StoryLookOffset : 0f;
        try
        {
            // Garbage retains its short rightward glance and map-edge exception.
            if (peek)
            {
                camera.BeginPursuitFocus();
                camera.StoryLookOffset = previousOffset + .7f;
            }
            yield return ShowPursuitBubble(resourcePath);
        }
        finally
        {
            if (peek && camera != null)
            {
                camera.StoryLookOffset = previousOffset;
                camera.EndPursuitFocus();
            }
        }
    }

    private static IEnumerator WaitForPursuitView(PlayerCameraFollow2D camera)
    {
        if (camera == null || !camera.isActiveAndEnabled) yield break;
        // Give LateUpdate a frame to apply the new target before judging arrival.
        yield return null;
        float deadline = Time.unscaledTime + 5f;
        while (camera != null && camera.isActiveAndEnabled && !camera.PursuitViewSettled
            && Time.unscaledTime < deadline) yield return null;
    }

    private IEnumerator MarketPursuit()
    {
        // EnterMarket holds player/inventory input through both pans, both shouts, and the monologue.
        player.GetComponent<InventoryUI>()?.CloseForStoryDialogue();
        var camera = Camera.main != null ? Camera.main.GetComponent<PlayerCameraFollow2D>() : null;
        float previousOffset = camera != null ? camera.StoryLookOffset : 0f;
        DialoguePresentation.Set(this, true);
        try
        {
            if (camera != null)
            {
                camera.StoryLookOffset = 0f;
                camera.BeginPursuitFocus();
                camera.AimPursuitView(true);
            }
            yield return WaitForPursuitView(camera);
            yield return ShowPursuitBubble("Dialogue/Market/market_pursuit_shout", opacity: .5f);
            if (camera != null) camera.AimPursuitView(false, duration: .5f, motionBlur: true);
            yield return WaitForPursuitView(camera);
            yield return ShowPursuitBubble("Dialogue/Market/market_pursuit_shout_left", leftSide: true);
            if (camera != null)
            {
                camera.StoryLookOffset = previousOffset;
                camera.EndPursuitFocus();
            }
            yield return Say("Market/market_pursuit_monologue");
        }
        finally
        {
            if (camera != null)
            {
                camera.StoryLookOffset = previousOffset;
                camera.EndPursuitFocus();
            }
            DialoguePresentation.Set(this, false);
        }
    }

    private IEnumerator GarbagePursuit()
    {
        if (marketMap || pursuitPlayed) yield break;
        pursuitPlayed = true;
        busy = true;
        var controls = player.GetComponent<PlayerControlLock>();
        var ui = player.GetComponent<InventoryUI>();
        var previousEvent = dialogue.BeforeLineEvent;
        bool shoutPlayed = false;
        IEnumerator BeforeLine(string cue)
        {
            if (cue != "추격 말풍선")
            {
                if (previousEvent != null) yield return previousEvent(cue);
                yield break;
            }
            if (shoutPlayed) yield break;
            shoutPlayed = true;
            yield return PlayPursuitShout("Dialogue/Garbage/garbage_pursuit_shout");
        }
        controls?.Acquire();
        try
        {
            ui?.CloseForStoryDialogue();
            yield return GarbageEquipmentReaction();
            dialogue.BeforeLineEvent = BeforeLine;
            yield return Say("Garbage/garbage_pursuit_monologue");
        }
        finally
        {
            dialogue.BeforeLineEvent = previousEvent;
            controls?.Release();
            busy = false;
        }
    }
}
