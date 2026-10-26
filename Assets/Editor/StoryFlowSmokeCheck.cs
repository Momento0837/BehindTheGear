#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class StoryFlowSmokeCheck
{
    private const string Key = "BTG.StorySmoke";
    private static double deadline;
    private static int waitFrames;
    static StoryFlowSmokeCheck() { EditorApplication.update += Tick; }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main/Forest.unity");
        // Video rendering is excluded from the headless progression check.
        foreach (var sequence in UnityEngine.Object.FindObjectsByType<CutsceneVideoSequence>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            UnityEngine.Object.DestroyImmediate(sequence.gameObject);
        SessionState.SetInt(Key, 1);
        EditorApplication.isPlaying = true;
    }
    private static object Read(object target, string name) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void FinishDialogue(StorySceneFlow flow)
    {
        var dialogue = (ConversationDialogue)Read(flow, "dialogue");
        if (dialogue != null && dialogue.IsPlaying)
        {
            dialogue.StopAllCoroutines();
            typeof(ConversationDialogue).GetMethod("FinishDialogue", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(dialogue, null);
        }
    }
    private static void Tick()
    {
        int stage = SessionState.GetInt(Key, 0);
        if (stage == 0 || !EditorApplication.isPlaying) return;
        if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 120;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Story flow test timeout, stage=" + stage);
            var flow = UnityEngine.Object.FindFirstObjectByType<StorySceneFlow>();
            if (flow == null || Read(flow, "player") == null || Read(flow, "monsters") == null) return;
            var player = (PlayerMovement2D)Read(flow, "player");
            var portal = (GameObject)Read(flow, "portal");
            if (portal == null) return;
            if (++waitFrames < 5) return;
            switch(stage)
            {
                case 1:
                    Require((bool)Read(player.GetComponent<InventoryUI>(), "ready"), "Inventory UI did not initialize");
                    Require(!portal.activeSelf, "Forest portal must start locked");
                    var monsters = (MonsterFollowController[])Read(flow, "monsters");
                    Require(monsters.Length == 3, "Forest must have three enemies");
                    foreach(var monster in monsters) monster.TakeDamage(int.MaxValue);
                    SessionState.SetInt(Key, 2); break;
                case 2:
                    FinishDialogue(flow);
                    if (!(bool)Read(flow, "ready")) return;
                    Require(portal.activeSelf, "Forest exit did not unlock");
                    portal.GetComponent<Interactable2D>().Interact(player.gameObject);
                    SessionState.SetInt(Key, 3); break;
                case 3:
                    if (SceneManager.GetActiveScene().name != "Market") return;
                    FinishDialogue(flow);
                    Require((bool)Read(player.GetComponent<InventoryUI>(), "ready"), "Inventory UI lost after travel");
                    var inventory = player.GetComponent<PlayerInventory>();
                    int slot = Enumerable.Range(0, inventory.Capacity).Where(i => !inventory.GetSlot(i).IsEmpty && inventory.GetSlot(i).Item.IsEquipment).DefaultIfEmpty(-1).First();
                    Require(slot >= 0, "Tutorial gear was not supplied");
                    Require(inventory.EquipFromSlot(slot), "Could not equip tutorial gear");
                    SessionState.SetInt(Key, 4); break;
                case 4:
                    FinishDialogue(flow);
                    if (!(bool)Read(flow, "ready")) return;
                    portal.GetComponent<Interactable2D>().Interact(player.gameObject);
                    Require((bool)Read(flow, "marketMap"), "Market map travel failed");
                    Require(Mathf.Abs(player.transform.position.x - 49) < .1f, "Wrong Market arrival");
                    Require(!portal.activeSelf, "Final portal must require NPC dialogue");
                    SessionState.SetInt(Key, 5); break;
                case 5:
                    var dialogue = (ConversationDialogue)Read(flow, "dialogue");
                    dialogue.GetComponent<Interactable2D>().Interact(player.gameObject);
                    SessionState.SetInt(Key, 6); break;
                case 6:
                    FinishDialogue(flow);
                    if (!(bool)Read(flow, "ready")) return;
                    Require(portal.activeSelf, "NPC did not unlock final portal");
                    portal.GetComponent<Interactable2D>().Interact(player.gameObject);
                    Require(!portal.activeSelf, "Final portal should be consumed");
                    Debug.Log("STORY_SMOKE_PASS: enemy gate, Forest -> Garbage, tutorial equipment, same-scene Market travel, NPC gate and final cutscene trigger.");
                    SessionState.SetInt(Key, 0);
                    EditorApplication.Exit(0); break;
            }
            waitFrames = 0;
        }
        catch(Exception exception)
        {
            Debug.LogException(exception);
            SessionState.SetInt(Key, 0);
            EditorApplication.Exit(1);
        }
    }
}
#endif
