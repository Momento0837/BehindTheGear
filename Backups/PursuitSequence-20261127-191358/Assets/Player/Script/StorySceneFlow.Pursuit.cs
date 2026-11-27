using System.Collections;
using UnityEngine;

public sealed partial class StorySceneFlow
{
    private bool pursuitPlayed;

    private IEnumerator PlayPursuitShout(string resourcePath)
    {
        var source = Resources.Load<TextAsset>(resourcePath);
        if (source == null)
        {
            Debug.LogError("[Pursuit] Missing dialogue JSON: " + resourcePath);
            yield break;
        }
        var bubble = gameObject.AddComponent<AutomaticNpcBubble>();
        bubble.ScreenRight = true;
        DialoguePresentation.Set(bubble, true);
        var camera = Camera.main != null ? Camera.main.GetComponent<PlayerCameraFollow2D>() : null;
        bool peek = camera != null && camera.CanLookRight;
        float previousOffset = camera != null ? camera.StoryLookOffset : 0f;
        try
        {
            // Keep the camera still when there is no room to look farther right.
            if (peek)
            {
                camera.BeginPursuitFocus();
                camera.StoryLookOffset = previousOffset + .7f;
            }
            bubble.Begin(dialogue.TalkingImage, ConversationDialogue.GetGreetingLines(source, int.MaxValue));
            while (bubble.IsPlaying) yield return null;
        }
        finally
        {
            if (peek && camera != null)
            {
                camera.StoryLookOffset = previousOffset;
                camera.EndPursuitFocus();
            }
            DialoguePresentation.Set(bubble, false);
            if (bubble != null) Destroy(bubble);
        }
    }

    private IEnumerator MarketPursuit()
    {
        // EnterMarket holds the control lock until the shout and Ethan's response both finish.
        player.GetComponent<InventoryUI>()?.CloseForStoryDialogue();
        yield return PlayPursuitShout("Dialogue/Market/market_pursuit_shout");
        yield return Say("Market/market_pursuit_monologue");
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
        // Hold one continuous lock across inventory close, Ethan's reaction, and the monologue/shout.
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
