using System.Collections;
using System.Linq;
using UnityEngine;

public sealed partial class StorySceneFlow
{
    private string currentGuide;
    private StoryArrival currentChapter;
    private bool garbageCollected;

    public StoryFieldState CaptureFieldState() => new()
    {
        chapter = (int)currentChapter, guide = currentGuide, marketMap = marketMap,
        ready = ready, marketNpcSpoken = marketNpcSpoken,
        greetingCompleted = marketBubble != null && marketBubble.HasPresented,
        garbageCollected = garbageCollected,
        hasSelena = selena != null,
        selenaPosition = selena != null ? selena.position : Vector3.zero,
        selenaScale = selena != null ? selena.localScale : Vector3.one,
        monsters = InScene<MonsterFollowController>().Where(m => m.gameObject.activeInHierarchy && m.CurrentHealth > 0)
            .Select(m => new FieldMonsterState { position = m.transform.position, health = m.CurrentHealth }).ToArray()
    };

    private IEnumerator RestoreFieldState(FieldReturnState saved)
    {
        var state = saved.story;
        if (state == null) yield return FreeRoam();
        else
        {
            currentGuide = state.guide;
            marketNpcSpoken = state.marketNpcSpoken;
            if (state.chapter != 0)
            {
                yield return EnterLaterChapter((StoryArrival)state.chapter, true);
                if (state.hasSelena)
                {
                    if (selena == null)
                    {
                        var npc = new GameObject("Selena", typeof(SpriteRenderer), typeof(Animator));
                        selena = npc.transform;
                        var renderer = npc.GetComponent<SpriteRenderer>();
                        var playerRenderer = player.GetComponent<SpriteRenderer>();
                        renderer.sharedMaterial = playerRenderer.sharedMaterial;
                        renderer.sortingLayerID = playerRenderer.sortingLayerID;
                        renderer.sortingOrder = playerRenderer.sortingOrder;
                        npc.GetComponent<Animator>().runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("Story/Selena");
                        Resources.Load<AnimationClip>("Story/Selena_Idle")?.SampleAnimation(npc, 0f);
                    }
                    selena.position = state.selenaPosition;
                    selena.localScale = state.selenaScale;
                }
            }
            else if (state.marketMap)
            {
                foreach (var monster in monsters) monster.gameObject.SetActive(false);
                dialogue.transform.position = new Vector3(65f, -2.5f, 0f);
                PrepareMarket();
                if (state.greetingCompleted)
                {
                    marketBubble.RestoreCompletedGreeting();
                    if (!state.marketNpcSpoken)
                    {
                        var hint = dialogue.GetComponent<WorldInteractionHint>();
                        if (hint == null) hint = dialogue.gameObject.AddComponent<WorldInteractionHint>();
                        hint.ShowUntilDismissed = true;
                    }
                }
            }
            else if (gameObject.scene.name == "Market")
                StartCoroutine(Garbage(state.garbageCollected, true));
            else
                RestoreForestEncounter(state);
            ready = state.ready;
            marketMap = state.marketMap;
            garbageCollected = state.garbageCollected;
            currentGuide = state.guide;
        }
        saved.RestorePlacement(player.gameObject, gameObject.scene);
        saved.RestoreQuest(this);
        while (SceneFadeTransition.IsRunning) yield return null;
        if (currentChapter == StoryArrival.NeighborhoodFive) locationHud?.SetLocation("거주구역");
        else ShowLocation();
    }

    private void RestoreForestEncounter(StoryFieldState state)
    {
        portal.GetComponent<ScenePortal2D>().SetArea(-26.5f, -20.5f);
        portal.SetActive(state.ready);
        var remaining = state.monsters ?? System.Array.Empty<FieldMonsterState>();
        var template = monsters.FirstOrDefault();
        var restored = new System.Collections.Generic.List<MonsterFollowController>();
        for (int i = 0; template != null && i < remaining.Length; i++)
        {
            var monster = i < monsters.Length ? monsters[i] : Instantiate(template);
            monster.gameObject.SetActive(true);
            monster.transform.position = remaining[i].position;
            monster.GetComponent<Rigidbody2D>().position = remaining[i].position;
            monster.BindPlayer(player.transform);
            monster.ConfigureLoot(sceneLoot);
            monster.RestoreEncounterHealth(remaining[i].health);
            restored.Add(monster);
        }
        foreach (var monster in monsters) if (!restored.Contains(monster)) monster.gameObject.SetActive(false);
        monsters = restored.ToArray();
        if (!state.ready) StartCoroutine(WaitForRestoredCombat());
    }

    private IEnumerator WaitForRestoredCombat()
    {
        while (BehindTheGear.HideOut.HideOutTravel.IsVisiting || monsters.Any(m => m != null && m.CurrentHealth > 0)) yield return null;
        ready = true;
        portal.SetActive(true);
        AdvanceGuide("forest_exit");
    }
}
