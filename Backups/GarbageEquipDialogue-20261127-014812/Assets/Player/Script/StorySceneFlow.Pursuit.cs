using System.Collections;
using UnityEngine;

public sealed partial class StorySceneFlow
{
    private bool pursuitPlayed;
    private IEnumerator GarbagePursuit()
    {
        if (marketMap || pursuitPlayed) yield break;
        pursuitPlayed = true;
        busy = true;
        var controls = player.GetComponent<PlayerControlLock>();
        var ui = player.GetComponent<InventoryUI>();
        var bubble = gameObject.AddComponent<AutomaticNpcBubble>(); bubble.ScreenRight = true;
        var camera = Camera.main != null ? Camera.main.GetComponent<PlayerCameraFollow2D>() : null;
        bool peek = camera != null && camera.CanLookRight;
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
            if (peek) camera.StoryLookOffset = .7f;
            var source = Resources.Load<TextAsset>("Dialogue/Garbage/garbage_pursuit_shout");
            bubble.Begin(dialogue.TalkingImage, ConversationDialogue.GetGreetingLines(source, int.MaxValue));
            while (bubble.IsPlaying) yield return null;
            if (camera != null) camera.StoryLookOffset = 0;
        }
        // Hold one continuous lock across inventory close, shout and Ethan's monologue.
        controls?.Acquire();
        try
        {
            ui?.CloseForStoryDialogue();
            dialogue.BeforeLineEvent = BeforeLine;
            yield return Say("Garbage/garbage_pursuit_monologue");
        }
        finally
        {
            dialogue.BeforeLineEvent = previousEvent;
            if (camera != null) camera.StoryLookOffset = 0;
            if (bubble != null) Destroy(bubble);
            controls?.Release();
            busy = false;
        }
    }
}
