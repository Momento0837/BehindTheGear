using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

/// <summary>Prototype story wiring, limited to the two main story scenes.</summary>
public sealed class StorySceneFlow : MonoBehaviour
{
    private const float GarbagePortalLeft = -8f;

    private ConversationDialogue dialogue;
    private CutsceneVideoSequence cutscene;
    private PlayerMovement2D player;
    private GameObject portal;
    private bool ready;
    private bool busy;
    private bool marketMap;
    private PlayerInventory inventory;
    private MonsterFollowController[] monsters;
    private LocationHUD locationHud;
    private MarketNpcBubble marketBubble;
    private bool marketNpcSpoken;
    public static bool RestartAtCombat { get; set; }
    private PlayerHealth tutorialHealth;
    private InventoryItemDefinition[] sceneLoot;
    private ControlTutorialUI controlTutorial;

    private void UnlockTutorialMonsters()
    {
        foreach (var monster in monsters)
            if (monster != null) monster.SetTutorialProtection(false);
        if (tutorialHealth != null) tutorialHealth.Damaged -= UnlockTutorialMonsters;
        tutorialHealth = null;
    }

    private void OnDestroy()
    {
        if (tutorialHealth != null) tutorialHealth.Damaged -= UnlockTutorialMonsters;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        RestartAtCombat = false;
        SceneManager.sceneLoaded -= Attach;
        SceneManager.sceneLoaded += Attach;
    }

    private static void Attach(Scene scene, LoadSceneMode mode)
    {
        if (scene.path != "Assets/Scenes/Main/Forest.unity" &&
            scene.path != "Assets/Scenes/Main/Market.unity") return;
        var root = new GameObject("Story Scene Flow");
        SceneManager.MoveGameObjectToScene(root, scene);
        var flow = root.AddComponent<StorySceneFlow>();
        var placedItems = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<WorldItemPickup>(true)).ToArray();
        flow.sceneLoot = placedItems.Select(pickup => pickup.Definition).Where(item => item != null).Distinct().ToArray();
        foreach (var pickup in placedItems) pickup.gameObject.SetActive(false);
    }

    private T[] InScene<T>() where T : Component =>
        FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(c => c.gameObject.scene == gameObject.scene).ToArray();

    private IEnumerator Start()
    {
        // Wait for duplicate persistent players to be destroyed after scene loading.
        yield return null;
        player = FindFirstObjectByType<PlayerMovement2D>();
        if (player == null) yield break;
        if (SaveGameSession.Expedition)
        {
            player.GetComponent<PlayerHealth>()?.ReviveAfterSceneRestart();
            yield return FreeRoam(); yield break;
        }
        locationHud = LocationHUD.Create();
        SceneManager.MoveGameObjectToScene(locationHud.gameObject, gameObject.scene);
        player.GetComponent<PlayerHealth>()?.ReviveAfterSceneRestart();
        var inventoryUi = player.GetComponent<InventoryUI>();
        if (inventoryUi != null && inventoryUi.SceneCanvas != null)
        {
            var keptCanvas = inventoryUi.SceneCanvas;
            foreach (var canvas in InScene<Canvas>())
                if (canvas != keptCanvas && canvas.name == "Inventory Canvas") canvas.gameObject.SetActive(false);
            DontDestroyOnLoad(keptCanvas.transform.root.gameObject);
        }
        player.GetComponent<PlayerHealthHUD>()?.PreserveHud();
        player.GetComponent<PlayerInteractionController>()?.PreservePrompt();
        dialogue = InScene<ConversationDialogue>().FirstOrDefault(d => d.GetComponent<BlackMarkerNpc>() != null)
            ?? InScene<ConversationDialogue>().FirstOrDefault(d => d.name == "BlackMarker" || d.name == "Quest Giver NPC");
        cutscene = InScene<CutsceneVideoSequence>().FirstOrDefault();
        var oldPortal = InScene<ScenePortal2D>().FirstOrDefault();
        if (dialogue == null || oldPortal == null)
        {
            Debug.LogError("[Story] Missing dialogue or portal scene references.", this);
            yield break;
        }
        dialogue.gameObject.SetActive(true);
        dialogue.BindPlayer(player.GetComponent<PlayerControlLock>());
        if (cutscene != null)
        {
            cutscene.gameObject.SetActive(true);
            cutscene.BindPlayer(player.GetComponent<PlayerControlLock>());
            cutscene.IncludeUi(locationHud.CanvasRoot);
            if (inventoryUi != null) cutscene.IncludeUi(inventoryUi.SceneCanvas != null ? inventoryUi.SceneCanvas.gameObject : null);
            var video = cutscene.GetComponent<VideoPlayer>();
            if (video != null) video.targetCamera = Camera.main;
            foreach (var collider in cutscene.GetComponents<Collider2D>()) collider.enabled = false;
        }
        foreach (var npc in InScene<QuestGiver2D>())
        {
            npc.enabled = false;
            foreach (var collider in npc.GetComponents<Collider2D>()) collider.enabled = false;
            foreach (var renderer in npc.GetComponentsInChildren<SpriteRenderer>(true)) renderer.enabled = false;
        }
        dialogue.GetComponent<BlackMarkerNpc>()?.SetMapVisible(false);
        if (gameObject.scene.name == "Forest")
        {
            // Forest dialogue and video are driven by the story, never by NPC interaction.
            foreach (var interactable in InScene<Interactable2D>())
            {
                if (interactable.GetComponent<ScenePortal2D>() != null) continue;
                if (interactable.GetComponent<QuestGiver2D>() != null
                    || interactable.GetComponent<ConversationDialogue>() != null
                    || interactable.GetComponent<CutsceneVideoSequence>() != null)
                    interactable.enabled = false;
            }
        }
        portal = oldPortal.gameObject;
        portal.AddComponent<StoryInteraction2D>().Activated = UsePortal;
        monsters = InScene<MonsterFollowController>();
        foreach (var monster in monsters) monster.BindPlayer(player.transform);
        if (gameObject.scene.name == "Forest") yield return Forest();
        else yield return Garbage();
    }

    private IEnumerator FreeRoam()
    {
        // Expeditions start at a safe fixed entrance; saved field position/combat are never replayed.
        marketMap = gameObject.scene.name == "Market";
        PlacePlayer(marketMap ? new Vector3(49f, -2.5f, 0) : new Vector3(-4.44f, -.36f, 0));
        RoomDropCache.SetCurrent(gameObject.scene.path + (marketMap ? "#market" : ""));
        locationHud = LocationHUD.Create();
        SceneManager.MoveGameObjectToScene(locationHud.gameObject, gameObject.scene);
        dialogue = InScene<ConversationDialogue>().FirstOrDefault(d => d.GetComponent<BlackMarkerNpc>() != null);
        cutscene = InScene<CutsceneVideoSequence>().FirstOrDefault();
        if (cutscene != null) { cutscene.gameObject.SetActive(true); cutscene.BindPlayer(player.GetComponent<PlayerControlLock>()); }
        var questGivers = InScene<QuestGiver2D>();
        foreach (var npc in questGivers)
        {
            npc.enabled = true;
            if (!marketMap)
            {
                npc.enabled = false;
                npc.GetComponent<ConversationDialogue>()?.BindPlayer(player.GetComponent<PlayerControlLock>());
                foreach (var renderer in npc.GetComponentsInChildren<SpriteRenderer>(true)) renderer.enabled = true;
                foreach (var collider in npc.GetComponents<Collider2D>()) collider.enabled = true;
                npc.GetComponent<Interactable2D>().enabled = true;
            }
            if (marketMap)
            {
                npc.transform.position = new Vector3(65f, -2.5f, 0);
                npc.enabled = false;
                foreach (var renderer in npc.GetComponentsInChildren<SpriteRenderer>(true)) renderer.enabled = true;
                foreach (var collider in npc.GetComponents<Collider2D>()) collider.enabled = true;
                npc.GetComponent<Interactable2D>().enabled = true;
                npc.GetComponent<ConversationDialogue>()?.BindPlayer(player.GetComponent<PlayerControlLock>());
                npc.GetComponent<BlackMarkerNpc>()?.SetMapVisible(true);
            }
        }
        var exit = InScene<ScenePortal2D>().FirstOrDefault();
        if (exit != null)
        {
            exit.SetArea(marketMap ? 91f : -26.5f, marketMap ? 97.6f : -20.5f, lightFacesRight: !marketMap);
            var action = exit.GetComponent<StoryInteraction2D>() ?? exit.gameObject.AddComponent<StoryInteraction2D>();
            portal = exit.gameObject;
            action.Activated = UsePortal;
            ready = !marketMap;
            if (marketMap && dialogue != null) PrepareMarket();
            exit.gameObject.SetActive(true);
        }
        monsters = InScene<MonsterFollowController>();
        foreach (var monster in monsters)
        {
            monster.BindPlayer(player.transform); monster.ConfigureLoot(sceneLoot); monster.SetTutorialProtection(false);
        }
        QuestJournal.Publish();
        yield break;
    }

    private IEnumerator Forest()
    {
        PlacePlayer(new Vector3(-4.44f, -.36f, 0));
        ShowGuide("forest_combat");
        portal.GetComponent<ScenePortal2D>().SetArea(-26.5f, -20.5f);
        portal.SetActive(false);
        // Preserve the supplied monster; add two instances to match the three map markers.
        if (monsters.Length == 1)
        {
            var template = monsters[0];
            var second = Instantiate(template, new Vector3(-11f, -2.5f, 0), Quaternion.identity);
            var third = Instantiate(template, new Vector3(12f, -2.5f, 0), Quaternion.identity);
            monsters = new[] { template, second, third };
            foreach (var monster in monsters) monster.BindPlayer(player.transform);
        }
        foreach (var monster in monsters) monster.enabled = false;
        var questHud = FindObjectsByType<InGameQuestHUD>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(hud => hud.CanPresentOpening);
        if (cutscene != null && questHud != null)
            cutscene.EndingTransition = reveal => questHud.PresentOpeningQuest(reveal);
        foreach (var monster in monsters)
        {
            monster.ConfigureLoot(sceneLoot);
            monster.SetTutorialProtection(true);
        }
        tutorialHealth = player.GetComponent<PlayerHealth>();
        if (RestartAtCombat)
        {
            RestartAtCombat = false;
            var controls = player.GetComponent<PlayerControlLock>();
            controls?.Acquire();
            try
            {
                if (questHud != null) yield return questHud.PresentOpeningQuest(DeathFadeTransition.RevealCheckpoint);
                else DeathFadeTransition.RevealCheckpoint();
            }
            finally { controls?.Release(); }
        }
        else yield return PlayVideo();
        controlTutorial = gameObject.AddComponent<ControlTutorialUI>();
        controlTutorial.Begin(player);
        if (tutorialHealth != null) tutorialHealth.Damaged += UnlockTutorialMonsters;
        foreach (var monster in monsters) if (monster != null) monster.enabled = true;
        if (monsters.Length == 0)
        {
            Debug.LogError("[Story] Forest has no encounter monsters; portal remains locked.", this);
            yield break;
        }
        while (monsters.Any(m => m != null && m.CurrentHealth > 0)) yield return null;
        ready = true;
        portal.SetActive(true);
        AdvanceGuide("forest_exit");
        controlTutorial.ArmPortal(portal);
    }

    private IEnumerator Garbage()
    {
        foreach (var monster in monsters) monster.gameObject.SetActive(false);
        PlacePlayer(new Vector3(23f, -2.5f, 0));
        portal.GetComponent<ScenePortal2D>().SetArea(GarbagePortalLeft, -2f);
        portal.SetActive(false);
        dialogue.transform.position = new Vector3(65f, -2.5f, 0);
        inventory = player.GetComponent<PlayerInventory>();
        if (inventory == null)
        {
            Debug.LogError("[Story] Player needs an inventory for the equipment tutorial.", this);
            yield break;
        }
        var tutorialGear = Resources.Load<InventoryItemDefinition>("Story/TutorialGear");
        var searchPoint = InScene<Transform>().FirstOrDefault(t => t.name == "Interaction" && t.position.x < 30f);
        if (tutorialGear == null || searchPoint == null)
        {
            Debug.LogError("[Story] Missing Garbage Interaction or TutorialGear.", this);
            yield break;
        }
        bool collected = false;
        searchPoint.gameObject.layer = LayerMask.NameToLayer("Interactable");
        var trigger = searchPoint.GetComponent<BoxCollider2D>();
        if (trigger == null) trigger = searchPoint.gameObject.AddComponent<BoxCollider2D>();
        trigger.isTrigger = true;
        trigger.size = new Vector2(2f, 3f);
        foreach (var pulse in searchPoint.GetComponentsInChildren<InteractionLightPulse>(true))
            pulse.BindProximity(player.transform, trigger);
        var interactable = searchPoint.GetComponent<Interactable2D>();
        if (interactable == null) interactable = searchPoint.gameObject.AddComponent<Interactable2D>();
        interactable.AllowFromEitherSide = true;
        if (searchPoint.GetComponent<WorldInteractionHint>() == null)
            searchPoint.gameObject.AddComponent<WorldInteractionHint>();
        var action = searchPoint.GetComponent<StoryInteraction2D>();
        if (action == null) action = searchPoint.gameObject.AddComponent<StoryInteraction2D>();
        action.Activated = user =>
        {
            if (collected || user != player.gameObject || SceneFadeTransition.IsRunning) return;
            if (inventory.Add(tutorialGear, 1) != 1) return;
            collected = true;
            interactable.enabled = false;
            trigger.enabled = false;
            searchPoint.gameObject.SetActive(false);
            LocationHUD.ShowAcquired(tutorialGear, 1);
            AdvanceGuide("equipment_tutorial");
            player.GetComponent<InventoryUI>()?.BeginEquipmentHint(tutorialGear);
        };
        // Reveal the objective only after the incoming black screen has cleared.
        while (SceneFadeTransition.IsRunning) yield return null;
        ShowGuide("garbage_search");
        while (!collected) yield return null;
        while (inventory.GetEquipped(tutorialGear.Slot) != tutorialGear)
            yield return null;
        player.GetComponent<InventoryUI>()?.EndEquipmentHint();
        ready = true;
        portal.SetActive(true);
        AdvanceGuide("garbage_exit");
    }

    private void UsePortal(GameObject interactor)
    {
        if (busy || interactor != player.gameObject || dialogue.IsPlaying || (marketBubble != null && marketBubble.IsPresenting)) return;
        if (marketMap && !ready)
        {
            StartCoroutine(RemindMarketConversation());
            return;
        }
        if (!ready) return;
        busy = true;
        if (gameObject.scene.name == "Forest")
        {
            controlTutorial?.CompletePortal();
            portal.GetComponent<WorldInteractionHint>()?.Dismiss();
            // Arrival is owned by the Market flow, so no cross-scene marker is required.
            SceneFadeTransition.LoadScene("Market", .8f, success => { if (!success) busy = false; });
        }
        else if (!marketMap) StartCoroutine(EnterMarket());
        else StartCoroutine(FinalCutscene());
    }

    private IEnumerator EnterMarket()
    {
        ready = false;
        portal.SetActive(false);
        var controlLock = player.GetComponent<PlayerControlLock>();
        controlLock?.Acquire();
        try
        {
            yield return SceneFadeTransition.FadeThrough(PrepareMarket);
            // Show the arrival objective before the player reaches the NPC greeting trigger.
            ShowGuide("market_arrival");
        }
        finally
        {
            controlLock?.Release();
            busy = false;
        }
    }

    private void PrepareMarket()
    {
        var drops = RoomDropCache.Prepare(gameObject.scene.path + "#market");
        marketMap = true;
        RoomDropCache.Commit(drops, gameObject.scene);
        PlacePlayer(new Vector3(49f, -2.5f, 0));
        QuestTracker.Complete(this);
        dialogue.GetComponent<BlackMarkerNpc>()?.SetMapVisible(true);
        marketBubble = dialogue.gameObject.AddComponent<MarketNpcBubble>();
        marketBubble.Configure(dialogue, player.transform, Resources.Load<TextAsset>("Dialogue/quest_offer"));
        marketBubble.Finished += () => { if (!marketNpcSpoken) ShowGuide("market_talk"); };
        portal.GetComponent<ScenePortal2D>().SetArea(91f, 97.6f, lightFacesRight: false);
        portal.SetActive(true);
        var npcCollider = dialogue.GetComponents<Collider2D>();
        foreach (var collider in npcCollider) collider.enabled = true;
        var interaction = dialogue.gameObject.AddComponent<StoryInteraction2D>();
        interaction.Activated = _ => { if (!busy && !ready && !marketBubble.IsPresenting) StartCoroutine(TalkToMarketNpc()); };
    }

    private IEnumerator RemindMarketConversation()
    {
        busy = true;
        try { yield return Say("market_talk_reminder"); }
        finally { busy = false; }
    }

    private IEnumerator TalkToMarketNpc()
    {
        busy = true;
        int startLine = 3;
        if (!marketNpcSpoken) ShowGuide("market_talk");
        marketNpcSpoken = true;
        if (marketBubble != null) marketBubble.Dismiss();
        bool? accepted = null;
        var offer = Resources.Load<TextAsset>("Dialogue/quest_offer");
        if (offer == null)
        {
            Debug.LogError("[Story] Missing dialogue: quest_offer", this);
            busy = false;
            yield break;
        }
        dialogue.PlayWithChoices(offer, "수락", "거절", choice =>
        {
            accepted = choice;
            if (choice) AdvanceGuide("market_quest");
        }, startLine);
        while (!accepted.HasValue) yield return null;
        yield return Say(accepted.Value ? "quest_accepted" : "quest_rejected");
        if (!accepted.Value)
        {
            ShowGuide("market_retry");
            busy = false;
            yield break;
        }
        ready = true;
        portal.SetActive(true);
        busy = false;
    }

    private IEnumerator FinalCutscene()
    {
        ready = false;
        portal.SetActive(false);
        yield return PlayVideo();
        busy = false;
    }

    private IEnumerator PlayVideo()
    {
        if (cutscene == null) yield break;
        cutscene.PlayFromInteraction();
        while (cutscene.IsPlaying) yield return null;
        ShowLocation();
    }

    private IEnumerator Say(string resource)
    {
        var content = Resources.Load<TextAsset>("Dialogue/" + resource);
        if (content == null)
        {
            Debug.LogError("[Story] Missing dialogue: " + resource, this);
            yield break;
        }
        dialogue.Play(content);
        while (dialogue.IsPlaying) yield return null;
    }

    private void PlacePlayer(Vector3 position)
    {
        player.transform.position = position;
        var body = player.GetComponent<Rigidbody2D>();
        if (body != null) { body.position = position; body.linearVelocity = Vector2.zero; }
        // The opening cinematic pauses physics before gravity can settle the spawn.
        if (gameObject.scene.name == "Forest" && !player.SettleOnGround())
            Debug.LogWarning("[Story] No floor below the Forest spawn.", player);
        Vector2 mapBounds = marketMap ? new Vector2(44.92f, 97.28f) : new Vector2(-26.18f, 26.18f);
        if (!marketMap && gameObject.scene.name == "Market") mapBounds.x = GarbagePortalLeft;
        var movementBounds = player.GetComponent<PlayerMapBounds2D>();
        if (movementBounds == null) movementBounds = player.gameObject.AddComponent<PlayerMapBounds2D>();
        movementBounds.SetHorizontalBounds(mapBounds);
        foreach (var camera in InScene<PlayerCameraFollow2D>())
            camera.SetStoryBounds(mapBounds);
        Physics2D.SyncTransforms();
        ShowLocation();
    }

    private void ShowLocation() => locationHud?.SetLocation(
        gameObject.scene.name == "Forest" ? "포레스트" : marketMap ? "마켓" : "가베지");

    // Story/tutorial objectives are assigned automatically; NPC requests still require acceptance.
    private void ShowGuide(string definition) =>
        QuestTracker.Accept(this, Resources.Load<TextAsset>("Quests/" + definition));

    private void AdvanceGuide(string definition) =>
        QuestTracker.Advance(this, Resources.Load<TextAsset>("Quests/" + definition));
}
