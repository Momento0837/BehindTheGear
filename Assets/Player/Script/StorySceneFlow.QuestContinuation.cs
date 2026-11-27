using System.Collections;

public sealed partial class StorySceneFlow
{
    private const string SelenaTalkGuide = "forest_selena_talk";
    private const string SelenaShelterGuide = "selena_take_cover";
    private const string HideoutEntranceGuide = "neighborhood_hideout";
    private const string SelenaShelterCue = "quest:take_cover";

    private IEnumerator SelenaConversation()
    {
        var previousEvent = dialogue.BeforeLineEvent;
        bool shelterPending = false;
        IEnumerator BeforeLine(string cue)
        {
            if (cue == SelenaShelterCue) shelterPending = true;
            else if (previousEvent != null) yield return previousEvent(cue);
        }
        void OnLineStarted()
        {
            if (!shelterPending) return;
            shelterPending = false;
            // This event runs after the portrait has changed, before the first character appears.
            if (currentGuide != SelenaShelterGuide) AdvanceGuide(SelenaShelterGuide);
        }
        dialogue.BeforeLineEvent = BeforeLine;
        dialogue.LineRevealStarted += OnLineStarted;
        try { yield return Say("Forest_4/selena"); }
        finally
        {
            dialogue.BeforeLineEvent = previousEvent;
            dialogue.LineRevealStarted -= OnLineStarted;
        }
    }

    public void CompleteHideoutEntranceQuest(FieldReturnState saved)
    {
        if (currentChapter != StoryArrival.NeighborhoodFive || currentGuide != HideoutEntranceGuide) return;
        currentGuide = string.Empty;
        QuestTracker.Complete(this);
        // Clear the captured field objective too, so leaving/continuing cannot resurrect this step.
        if (saved == null) return;
        saved.questName = string.Empty;
        saved.objective = string.Empty;
        saved.showObjective = false;
        if (saved.story != null) saved.story.guide = string.Empty;
    }
}
