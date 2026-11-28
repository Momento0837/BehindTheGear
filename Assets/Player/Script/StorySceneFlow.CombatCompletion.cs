using System.Collections;

public sealed partial class StorySceneFlow
{
    private bool forestCombatCompletionRunning;

    private IEnumerator FinishForestCombat()
    {
        if (ready || forestCombatCompletionRunning) yield break;
        forestCombatCompletionRunning = true;
        bool previousBusy = busy;
        busy = true;
        portal.SetActive(false);
        yield return ForestBossEncounter.Run(); // Forest boss: fought after the monsters, before the closing monologue
        var controls = player.GetComponent<PlayerControlLock>();
        controls?.Acquire();
        try
        {
            player.GetComponent<InventoryUI>()?.CloseForStoryDialogue();
            yield return Say("Forest/forest_combat_monologue");
            ready = true;
            portal.SetActive(true);
            AdvanceGuide("forest_exit");
            if (controlTutorial == null)
            {
                // A restored encounter resumes directly; only the portal hint is needed here.
                controlTutorial = gameObject.AddComponent<ControlTutorialUI>();
                controlTutorial.Begin(player, portalOnly: true);
            }
            controlTutorial.ArmPortal(portal);
        }
        finally
        {
            controls?.Release();
            busy = previousBusy;
            forestCombatCompletionRunning = false;
        }
    }
}
