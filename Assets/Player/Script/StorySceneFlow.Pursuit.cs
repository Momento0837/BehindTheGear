using System.Collections;
using UnityEngine;

public sealed partial class StorySceneFlow
{
    private const float GarbagePursuitPanSeconds = .5f;
    private const float MarketPursuitPanSeconds = .8f;
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

    private IEnumerator PanToRightPursuitShout(string resourcePath, PlayerCameraFollow2D camera, float opacity = 1f, float panDuration = MarketPursuitPanSeconds)
    {
        if (camera != null)
        {
            camera.StoryLookOffset = 0f;
            camera.BeginPursuitFocus();
            camera.AimPursuitView(true, duration: panDuration);
        }
        // Both maps must reach the source of the shout before showing its world-anchored bubble.
        yield return WaitForPursuitView(camera);
        yield return ShowPursuitBubble(resourcePath, opacity: opacity);
    }

    private IEnumerator PlayPursuitShout(string resourcePath)
    {
        var camera = Camera.main != null ? Camera.main.GetComponent<PlayerCameraFollow2D>() : null;
        float previousOffset = camera != null ? camera.StoryLookOffset : 0f;
        try
        {
            yield return PanToRightPursuitShout(resourcePath, camera, panDuration: GarbagePursuitPanSeconds);
            if (camera != null) camera.AimPursuitView(false, duration: GarbagePursuitPanSeconds, returnToNormalZoom: true);
            yield return WaitForPursuitView(camera);
        }
        finally
        {
            if (camera != null)
            {
                camera.StoryLookOffset = previousOffset;
                camera.EndPursuitFocus();
            }
        }
    }
    private static IEnumerator WaitForPursuitView(PlayerCameraFollow2D camera, bool waitForZoom = true)
    {
        if (camera == null || !camera.isActiveAndEnabled) yield break;
        // Give LateUpdate a frame to apply the new target before judging arrival.
        yield return null;
        float deadline = Time.unscaledTime + 5f;
        while (camera != null && camera.isActiveAndEnabled && !(waitForZoom ? camera.PursuitViewSettled : camera.PursuitPositionSettled)
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
            yield return PanToRightPursuitShout("Dialogue/Market/market_pursuit_shout", camera, opacity: .5f);
            if (camera != null) camera.AimPursuitView(false, duration: MarketPursuitPanSeconds, motionBlur: true);
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
