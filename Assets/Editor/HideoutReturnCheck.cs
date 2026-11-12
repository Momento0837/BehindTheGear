#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BehindTheGear.HideOut;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class HideoutReturnCheck
{
    const string Folder = "Temp/HideoutReturnValidation", Key = "BTG.HideoutReturnCheck";
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static int stage;
    static double next, deadline;
    static Vector3 entryPosition;
    static HideOutSave saved;
    static HideOutSettings settings;
    static string slotDirectory;
    static object Read(object target, string name) => target.GetType().GetField(name, Flags).GetValue(target);
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags).Invoke(target, args);
    static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
        File.AppendAllText(Folder + "/progress.txt", "PASS " + message + "\n");
    }
    static void Next(int value, double delay = .2) { stage = value; next = EditorApplication.timeSinceStartup + delay; }
    static void Finish(string message)
    {
        File.WriteAllText(Folder + "/result.txt", message);
        ManualSaveStore.UseValidationDirectory(null);
        SessionState.SetBool(Key, false); EditorApplication.isPlaying = false;
    }
    static HideoutReturnCheck() => EditorApplication.update += Tick;
    static void Tick()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating && File.Exists(Folder + "/run"))
        {
            File.Delete(Folder + "/run");
            if (Application.unityVersion != "6000.3.23f1") return;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty) EditorSceneManager.SaveScene(scene, Folder + "/Unsaved-" + scene.name + ".unity", true);
            }
            File.WriteAllText(Folder + "/progress.txt", "Start\n");
            SessionState.SetBool(Key, true); stage = 0; EditorApplication.isPlaying = true;
        }
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (stage == 0)
            {
                deadline = EditorApplication.timeSinceStartup + 120;
                slotDirectory = Path.GetFullPath(Folder + "/slot-" + Guid.NewGuid().ToString("N"));
                ManualSaveStore.UseValidationDirectory(slotDirectory);
                settings = AssetDatabase.LoadAssetAtPath<HideOutSettings>(ManualSaveSetup.SettingsPath);
                EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/Main/Market.unity", new LoadSceneParameters(LoadSceneMode.Single));
                Next(1, 1); return;
            }
            RequireTime();
            if (EditorApplication.timeSinceStartup < next || SceneFadeTransition.IsRunning) return;
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement2D>();
            if (player == null && stage != 5) return;
            var bridge = player != null ? player.GetComponent<HideOutPlayerBridge>() : null;
            var flow = UnityEngine.Object.FindFirstObjectByType<StorySceneFlow>();
            switch (stage)
            {
                case 1:
                    if (flow == null || Read(flow, "inventory") == null) return;
                    flow.StopAllCoroutines(); Call(flow, "PrepareMarket");
                    var bubble = (MarketNpcBubble)Read(flow, "marketBubble");
                    bubble.RestoreCompletedGreeting();
                    typeof(StorySceneFlow).GetField("marketNpcSpoken", Flags).SetValue(flow, true);
                    Call(flow, "ShowGuide", "market_retry");
                    player.transform.position = new Vector3(73.25f, -1f, 0f);
                    player.GetComponent<Rigidbody2D>().position = player.transform.position;
                    player.SettleOnGround();
                    Next(2, .2); break;
                case 2:
                    entryPosition = player.transform.position;
                    var entrance = UnityEngine.Object.FindFirstObjectByType<HideOutEntrance>();
                    Require(entrance != null, "Market hideout entrance exists");
                    entrance.Enter(); Next(3, 1); break;
                case 3:
                    var hideout = UnityEngine.Object.FindFirstObjectByType<HideOutScene>();
                    if (hideout == null || bridge == null || bridge.State?.fieldReturn == null) return;
                    Require((bridge.State.fieldReturn.position - entryPosition).sqrMagnitude < .0001f, "Entry captures actual interaction position");
                    Require(bridge.State.fieldReturn.questName == "목숨이 소중하지도 않아?", "Story objective retained inside hideout");
                    Require(bridge.Save(), "Save to isolated Temp slot");
                    saved = ManualSaveStore.ReadSlot();
                    Require(saved.fieldReturn.story.marketNpcSpoken && saved.fieldReturn.story.greetingCompleted, "NPC conversation flags survive disk serialization");
                    Call(hideout, "ExitHideOut"); Next(4, 1); break;
                case 4:
                    Require(SceneManager.GetActiveScene().name == "Market", "Live exit returns to origin scene");
                    Require((player.transform.position - entryPosition).sqrMagnitude < .0025f, "Live exit restores interaction position");
                    Require(QuestTracker.CurrentQuestName == saved.fieldReturn.questName, "Live exit restores current story objective");
                    SaveGameSession.ReturnToMenu(); Next(5, 1); break;
                case 5:
                    SaveGameSession.Continue(saved, settings, AssetDatabase.LoadAssetAtPath<GameObject>(ManualSaveSetup.PlayerPath));
                    Next(6, 1); break;
                case 6:
                    var loadedHideout = UnityEngine.Object.FindFirstObjectByType<HideOutScene>();
                    if (loadedHideout == null || bridge == null || SaveGameSession.IsContinuing) return;
                    Require(bridge.State.fieldReturn != null && !HideOutTravel.CanReturn, "Continued slot uses persisted return data without live origin");
                    Call(loadedHideout, "ExitHideOut"); Next(7, 1); break;
                case 7:
                    if (flow == null) return;
                    Require((player.transform.position - entryPosition).sqrMagnitude < .0025f, "Continue then exit restores exact position");
                    Require(QuestTracker.CurrentQuestName == saved.fieldReturn.questName, "Continue restores story quest rather than replaying tutorial");
                    Require((bool)Read(flow, "marketNpcSpoken") && ((MarketNpcBubble)Read(flow, "marketBubble")).HasPresented, "NPC progression restored");
                    var dialogue = (ConversationDialogue)Read(flow, "dialogue");
                    dialogue.GetComponent<Interactable2D>().Interact(player.gameObject);
                    Next(8, 1); break;
                case 8:
                    var d = (ConversationDialogue)Read(flow, "dialogue");
                    Require(d.IsChoosing, "Restored NPC still offers choices");
                    Call(d, "SelectChoice", true);
                    Require(QuestTracker.CurrentQuestName == "반동분자를 찾으러 가기", "Restored quest owner can advance storyline");
                    Next(9, .2); break;
                case 9:
                    var speech = (ConversationDialogue)Read(flow, "dialogue");
                    if (speech.IsPlaying) { Call(speech, "RequestAdvance"); next = EditorApplication.timeSinceStartup + .2; return; }
                    // Check the same live monster AI without writing any authored monster data.
                    var monster = UnityEngine.Object.FindObjectsByType<MonsterFollowController>(FindObjectsInactive.Include, FindObjectsSortMode.None).First();
                    monster.gameObject.SetActive(true); monster.enabled = true;
                    monster.transform.position = player.transform.position + Vector3.left * 4f;
                    monster.GetComponent<Rigidbody2D>().position = monster.transform.position;
                    monster.BindPlayer(player.transform);
                    monster.RestoreEncounterHealth(100);
                    typeof(MonsterFollowController).GetField("actionUntil", Flags).SetValue(monster, 0f);
                    var skills = player.GetComponent<PlayerSkills>();
                    Require(skills.TryUseStealth(), "Q stealth activates");
                    Next(10, .15); break;
                case 10:
                    var enemy = UnityEngine.Object.FindFirstObjectByType<MonsterFollowController>();
                    Require(Mathf.Abs(enemy.GetComponent<Rigidbody2D>().linearVelocity.x) < .01f && !(bool)Read(enemy, "wasChasing"), "Stealth stops chasing and patrol drift");
                    player.GetComponent<PlayerSkills>().BreakStealth();
                    Next(11, .08); break;
                case 11:
                    var pursuer = UnityEngine.Object.FindFirstObjectByType<MonsterFollowController>();
                    Require((bool)Read(pursuer, "wasChasing") && pursuer.GetComponent<Rigidbody2D>().linearVelocity.x > 0f, "Aggro resumes on next physics ticks after stealth ends");
                    Finish("PASS Unity " + Application.unityVersion + ": live hideout return; isolated save/continue/exit position and quest restoration; NPC storyline advancement; stealth stop and reacquisition."); break;
            }
        }
        catch (Exception error) { Finish("FAIL stage " + stage + "\n" + error); Debug.LogException(error); }
    }
    static void RequireTime() { if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timeout stage " + stage); }
}
#endif
