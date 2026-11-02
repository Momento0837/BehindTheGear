using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

/// <summary>Prototype story wiring, limited to the two main story scenes.</summary>
public sealed class StorySceneFlow : MonoBehaviour
{
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
    [SerializeField] private TextAsset marketQuestDefinition;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= Attach;
        SceneManager.sceneLoaded += Attach;
    }

    private static void Attach(Scene scene, LoadSceneMode mode)
    {
        if (scene.path != "Assets/Scenes/Main/Forest.unity" &&
            scene.path != "Assets/Scenes/Main/Market.unity") return;
        var root = new GameObject("Story Scene Flow");
        SceneManager.MoveGameObjectToScene(root, scene);
        root.AddComponent<StorySceneFlow>();
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
        portal = oldPortal.gameObject;
        portal.AddComponent<StoryInteraction2D>().Activated = UsePortal;
        monsters = InScene<MonsterFollowController>();
        foreach (var monster in monsters) monster.BindPlayer(player.transform);
        if (gameObject.scene.name == "Forest") yield return Forest();
        else yield return Garbage();
    }

    private IEnumerator Forest()
    {
        PlacePlayer(new Vector3(-4.44f, -.36f, 0));
        ShowGuide("forest_combat");
        portal.transform.position = new Vector3(-21.77f, -2.4f, 0);
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
        yield return PlayVideo();
        foreach (var monster in monsters) if (monster != null) monster.enabled = true;
        if (monsters.Length == 0)
        {
            Debug.LogError("[Story] Forest has no encounter monsters; portal remains locked.", this);
            yield break;
        }
        while (monsters.Any(m => m != null && m.CurrentHealth > 0)) yield return null;
        yield return Say("forest_clear");
        ready = true;
        portal.SetActive(true);
        ShowGuide("forest_exit");
    }

    private IEnumerator Garbage()
    {
        foreach (var monster in monsters) monster.gameObject.SetActive(false);
        PlacePlayer(new Vector3(23f, -2.5f, 0));
        ShowGuide("equipment_tutorial");
        portal.transform.position = new Vector3(-1f, -2.4f, 0);
        portal.SetActive(false);
        dialogue.transform.position = new Vector3(65f, -2.5f, 0);
        inventory = player.GetComponent<PlayerInventory>();
        if (inventory == null)
        {
            Debug.LogError("[Story] Player needs an inventory for the equipment tutorial.", this);
            yield break;
        }
        var tutorialGear = Resources.Load<InventoryItemDefinition>("Story/TutorialGear");
        bool ownsGear = Enumerable.Range(0, inventory.Capacity)
            .Any(i => !inventory.GetSlot(i).IsEmpty && inventory.GetSlot(i).Item.IsEquipment);
        if (!ownsGear && tutorialGear != null && inventory.GetEquipped(tutorialGear.Slot) == null)
        {
            // Keep a pickup available even if the inventory is currently full.
            if (inventory.Add(tutorialGear, 1) == 0)
            {
                var pickup = new GameObject("Tutorial Equipment Pickup");
                pickup.transform.position = player.transform.position + Vector3.left;
                pickup.AddComponent<WorldItemPickup>().Configure(tutorialGear, 1);
            }
        }
        yield return Say("equipment_tutorial");
        // Already-equipped gear also satisfies the lesson, including gear carried from Forest.
        while (!System.Enum.GetValues(typeof(InventoryItemDefinition.EquipmentSlot))
                   .Cast<InventoryItemDefinition.EquipmentSlot>().Any(s => inventory.GetEquipped(s) != null))
            yield return null;
        yield return Say("equipment_complete");
        ready = true;
        portal.SetActive(true);
        ShowGuide("garbage_exit");
    }

    private void UsePortal(GameObject interactor)
    {
        if (!ready || busy || interactor != player.gameObject) return;
        busy = true;
        if (gameObject.scene.name == "Forest")
        {
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
        marketMap = true;
        PlacePlayer(new Vector3(49f, -2.5f, 0));
        ShowGuide("market_talk");
        dialogue.GetComponent<BlackMarkerNpc>()?.SetMapVisible(true);
        portal.transform.position = new Vector3(94f, -2.4f, 0);
        var npcCollider = dialogue.GetComponents<Collider2D>();
        foreach (var collider in npcCollider) collider.enabled = true;
        var interaction = dialogue.gameObject.AddComponent<StoryInteraction2D>();
        interaction.Activated = _ => { if (!busy && !ready) StartCoroutine(TalkToMarketNpc()); };
        busy = false;
        yield return null;
    }

    private IEnumerator TalkToMarketNpc()
    {
        busy = true;
        yield return Say("quest_offer");
        bool? accepted = null;
        // Retain the speaker and prompt from the authored JSON instead of adding prototype narration.
        dialogue.ShowChoicesForCurrentLine("수락", "거절", choice =>
        {
            accepted = choice;
            if (choice) QuestTracker.Accept(this, marketQuestDefinition != null ? marketQuestDefinition : Resources.Load<TextAsset>("Quests/market_quest"));
        });
        while (!accepted.HasValue) yield return null;
        yield return Say(accepted.Value ? "quest_accepted" : "quest_rejected");
        if (!accepted.Value)
        {
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
        Vector2 mapBounds = marketMap ? new Vector2(44.92f, 97.28f) : new Vector2(-26.18f, 26.18f);
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
}
