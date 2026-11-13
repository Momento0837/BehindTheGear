using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

public sealed partial class StorySceneFlow
{
    private enum StoryArrival { None, ForestFour, NeighborhoodFive }
    private static StoryArrival pendingArrival;
    private static Transform travellingSelena;
    private Transform selena;
    private bool selenaUsed;

    private void TravelToChapter(string scene, StoryArrival arrival)
    {
        pendingArrival = arrival;
        SceneFadeTransition.LoadScene(scene, .8f, success =>
        {
            if (!success) { pendingArrival = StoryArrival.None; if (this != null) busy = false; }
        });
    }

    private IEnumerator EnterLaterChapter(StoryArrival arrival, bool restoring = false)
    {
        currentChapter = arrival;
        foreach (var monster in monsters) monster.gameObject.SetActive(false);
        portal.SetActive(false);
        ready = false;
        bool forest = arrival == StoryArrival.ForestFour;
        string mapName = forest ? "#4_Forest" : "#5_Neighborhood";
        var map = InScene<Transform>().FirstOrDefault(t => t.name == mapName)
            ?? (!forest ? InScene<Transform>().FirstOrDefault(t => t.name == "#4_Neighborhood") : null);
        if (map == null)
        {
            Debug.LogError("[Story] Missing arrival map: " + mapName, this);
            yield break;
        }
        // The saved scene still uses the former chapter number on this prefab instance.
        if (!forest) map.name = mapName;
        map.gameObject.SetActive(true);
        var artwork = map.GetComponentsInChildren<SpriteRenderer>(true)
            .Where(r => r.sprite != null && r.bounds.size.x > 10f).ToArray();
        if (artwork.Length == 0) { Debug.LogError("[Story] Arrival map has no artwork bounds.", map); yield break; }
        // Intersect the large parallax layers so neither the camera nor player exposes their edges.
        Vector2 bounds = new(artwork.Max(r => r.bounds.min.x), artwork.Min(r => r.bounds.max.x));
        EnsureChapterFloor(map, bounds);
        var movementBounds = player.GetComponent<PlayerMapBounds2D>();
        if (movementBounds == null) movementBounds = player.gameObject.AddComponent<PlayerMapBounds2D>();
        movementBounds.ClearHorizontalBounds();
        Vector3 spawn = new(bounds.x + 2f, map.position.y + 2f, 0f);
        player.transform.position = spawn;
        var body = player.GetComponent<Rigidbody2D>();
        body.position = spawn; body.linearVelocity = Vector2.zero;
        player.SetCinematicWalking(false);
        player.SettleOnGround(20f);
        movementBounds.SetHorizontalBounds(bounds);
        foreach (var camera in InScene<PlayerCameraFollow2D>()) camera.SetStoryBounds(bounds);
        RoomDropCache.SetCurrent(gameObject.scene.path + "#" + mapName);
        QuestTracker.Complete(this);
        if (forest) ConfigureSelena();
        else if (travellingSelena != null)
        {
            selena = travellingSelena;
            travellingSelena = null;
            SceneManager.MoveGameObjectToScene(selena.gameObject, gameObject.scene);
            selena.position = new Vector3(player.transform.position.x - 1.2f, selena.position.y, 0f);
            selena.GetComponent<Animator>().SetBool("IsMoving", false);
            selena.GetComponent<Interactable2D>().enabled = false;
            selena.GetComponent<StoryInteraction2D>().Activated = null;
        }
        while (!restoring && SceneFadeTransition.IsRunning) yield return null;
        locationHud?.SetLocation(forest ? "포레스트" : "거주구역");
    }

    private void EnsureChapterFloor(Transform map, Vector2 bounds)
    {
        // The new map prefabs contain artwork only. Keep authored collision if present.
        var floor = InScene<Collider2D>().FirstOrDefault(c => c.enabled && !c.isTrigger
            && c.gameObject.layer == LayerMask.NameToLayer("Ground")
            && c.bounds.min.x <= bounds.x + 1f && c.bounds.max.x >= bounds.y - 1f);
        if (floor != null) return;
        var ground = new GameObject("Story Ground", typeof(BoxCollider2D));
        ground.layer = LayerMask.NameToLayer("Ground");
        // Keep the floor at the scene root so Inspector coordinates are world coordinates.
        SceneManager.MoveGameObjectToScene(ground, map.gameObject.scene);
        // Match the original Forest floor in world space; artwork Y is not a floor offset.
        ground.transform.position = new Vector3((bounds.x + bounds.y) * .5f, -4.23f, 0f);
        ground.GetComponent<BoxCollider2D>().size = new Vector2(bounds.y - bounds.x, 1f);
        Physics2D.SyncTransforms();
    }

    private void ConfigureSelena()
    {
        selena = InScene<Transform>().FirstOrDefault(t => t.name == "Selena");
        if (selena == null) { Debug.LogError("[Story] Missing Selena NPC.", this); return; }
        selena.gameObject.layer = LayerMask.NameToLayer("Interactable");
        var trigger = selena.GetComponent<BoxCollider2D>();
        if (trigger == null) trigger = selena.gameObject.AddComponent<BoxCollider2D>();
        trigger.isTrigger = true;
        trigger.size = new Vector2(2f, 3f);
        var interaction = selena.GetComponent<Interactable2D>();
        if (interaction == null) interaction = selena.gameObject.AddComponent<Interactable2D>();
        interaction.AllowFromEitherSide = true;
        var animator = selena.GetComponent<Animator>();
        if (animator == null) animator = selena.gameObject.AddComponent<Animator>();
        if (animator.runtimeAnimatorController == null)
            animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("Story/Selena");
        var action = selena.GetComponent<StoryInteraction2D>();
        if (action == null) action = selena.gameObject.AddComponent<StoryInteraction2D>();
        action.Activated = user =>
        {
            if (!busy && !selenaUsed && user == player.gameObject) StartCoroutine(TalkToSelena());
        };
    }

    private IEnumerator TalkToSelena()
    {
        busy = selenaUsed = true;
        var controls = player.GetComponent<PlayerControlLock>();
        var npcAnimator = selena.GetComponent<Animator>();
        var body = player.GetComponent<Rigidbody2D>();
        var events = EventSystem.current;
        bool eventsEnabled = events != null && events.enabled;
        var inventoryUi = player.GetComponent<InventoryUI>();
        bool inventoryEnabled = inventoryUi != null && inventoryUi.enabled;
        bool loadStarted = false;
        bool restored = false;
        var departingPlayer = player;
        System.Action restore = () =>
        {
            if (restored) return;
            restored = true;
            if (departingPlayer != null) departingPlayer.SetCinematicWalking(false);
            if (npcAnimator != null) npcAnimator.SetBool("IsMoving", false);
            if (body != null) body.linearVelocity = Vector2.zero;
            if (inventoryUi != null) inventoryUi.enabled = inventoryEnabled;
            if (events != null) events.enabled = eventsEnabled;
            if (controls != null) controls.Release();
        };
        controls?.Acquire();
        try
        {
            // Reuse the scene's authored dialogue canvas, fonts and text effects.
            dialogue.transform.position = selena.position;
            yield return Say("selena");
            if (events != null) events.enabled = false;
            if (inventoryUi != null) inventoryUi.enabled = false;
            player.SetCinematicWalking(true);
            npcAnimator.SetBool("IsMoving", false);
            var sprite = selena.GetComponent<SpriteRenderer>();
            if (sprite != null) sprite.flipX = false;
            // Ethan sets off first. Selena follows only once he is visibly ahead.
            float followingTime = 0f;
            bool following = false;
            for (float t = 0f; !following || followingTime < .7f; t += Time.deltaTime)
            {
                body.linearVelocity = new Vector2(2.5f * Mathf.SmoothStep(0f, 1f, t / .25f), body.linearVelocity.y);
                if (!following && t >= .45f && player.transform.position.x >= selena.position.x + .9f)
                {
                    following = true;
                    npcAnimator.SetBool("IsMoving", true);
                }
                if (following)
                {
                    followingTime += Time.deltaTime;
                    selena.position += Vector3.right * (2.5f * Mathf.SmoothStep(0f, 1f, followingTime / .3f) * Time.deltaTime);
                }
                yield return null;
            }
            pendingArrival = StoryArrival.NeighborhoodFive;
            travellingSelena = selena;
            DontDestroyOnLoad(selena.gameObject);
            loadStarted = true;
            SceneFadeTransition.LoadScene("Market", .8f, success =>
            {
                if (!success)
                {
                    pendingArrival = StoryArrival.None;
                    if (travellingSelena != null)
                        SceneManager.MoveGameObjectToScene(travellingSelena.gameObject, SceneManager.GetActiveScene());
                    travellingSelena = null;
                }
                restore();
            }, freezeGameplay: false);
            // Keep the input lock until this scene is replaced (or a load fails).
            while (SceneFadeTransition.IsRunning)
            {
                body.linearVelocity = new Vector2(2.5f, body.linearVelocity.y);
                selena.position += Vector3.right * (2.5f * Time.deltaTime);
                yield return null;
            }
        }
        finally
        {
            if (!loadStarted) restore();
            busy = false;
        }
    }
}
