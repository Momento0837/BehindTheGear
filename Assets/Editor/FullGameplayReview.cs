using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class FullGameplayReview
{
    private const string Active = "BTG.FullGameplayReview";
    private const string Output = "Temp/FullGameplayReview";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int stage;
    private static double started, since, lastLog;
    private static bool initialized, equipped, skipChecked, marketTitleChecked;
    private static string previousScene;
    private static InventoryItemDefinition gear;
    static FullGameplayReview() { EditorApplication.update += Tick; }

    [MenuItem("Tools/Checks/Full Gameplay Review")]
    public static void Run()
    {
        if (Application.unityVersion != "6000.3.23f1" || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new Exception("Use edit mode in Unity 6000.3.23f1");
        if (SceneManager.GetActiveScene().isDirty) throw new Exception("Save the open scene before the play review");
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/result.txt", "RUNNING\n");
        SessionState.SetBool(Active, true);
        EditorApplication.isPlaying = true;
    }
    private static object Get(object value, string name) => value.GetType().GetField(name, Private).GetValue(value);
    private static void Call(object value, string name) => value.GetType().GetMethod(name, Private).Invoke(value, null);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Log(string text) { File.AppendAllText(Output + "/result.txt", text + "\n"); Debug.Log("PLAY_REVIEW " + text); }
    private static void Keys(params Key[] keys) => PlayReviewKeyboard.Held = keys;
    private static bool Pulse => (int)((EditorApplication.timeSinceStartup - since) * 6) % 2 == 0;
    private static void Next(int value, string reason) { Keys(); stage = value; since = EditorApplication.timeSinceStartup; Log("Stage " + value + ": " + reason); }
    private static void Shot(string name) => ScreenCapture.CaptureScreenshot(Path.GetFullPath(Output + "/" + name + ".png"));
    private static void Advance() => Keys(Pulse ? new[] { Key.Enter } : Array.Empty<Key>());
    private static void Walk(PlayerMovement2D player, float x, bool interact = false)
    {
        float distance = x - player.transform.position.x;
        Key direction = distance < 0 ? Key.LeftArrow : Key.RightArrow;
        if (Mathf.Abs(distance) > .35f) Keys(direction);
        else if (interact && Pulse) Keys(direction, Key.F);
        else Keys();
    }
    private static void End(bool success, string reason)
    {
        Log((success ? "PASS: " : "FAIL: ") + reason);
        Keys();
        SessionState.SetBool(Active, false);
        EditorApplication.isPlaying = false;
    }
    private static void Tick()
    {
        if (!SessionState.GetBool(Active, false)) return;
        if (!EditorApplication.isPlaying)
        {
            if (initialized && !EditorApplication.isPlayingOrWillChangePlaymode) SessionState.SetBool(Active, false);
            return;
        }
        try
        {
            double now = EditorApplication.timeSinceStartup;
            if (!initialized)
            {
                initialized = true;
                started = since = now;
                new GameObject("Play Review Keyboard").AddComponent<PlayReviewKeyboard>();
                typeof(PlayerControlSchemeSettings).GetField("current", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, PlayerControlScheme.ArrowKeys);
                typeof(PlayerControlSchemeSettings).GetField("loaded", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, true);
                Log("Virtual keyboard plays normal input. One deliberate lethal damage call tests restart; equipment/choice buttons use their existing UI actions. No scenes saved.");
            }
            Check(now - started < 300 && now - since < 65, "Timeout at stage " + stage);
            var flow = UnityEngine.Object.FindFirstObjectByType<StorySceneFlow>();
            if (flow == null || Get(flow, "player") == null || Get(flow, "monsters") == null) return;
            var player = (PlayerMovement2D)Get(flow, "player");
            var health = player.GetComponent<PlayerHealth>();
            var dialogue = (ConversationDialogue)Get(flow, "dialogue");
            var video = (CutsceneVideoSequence)Get(flow, "cutscene");
            var portal = (GameObject)Get(flow, "portal");
            if (portal == null) return;
            if (dialogue.IsPlaying)
            {
                var portrait = (Image)Get(dialogue, "portraitImage");
                Check(portrait == null || portrait.sprite != null || !portrait.enabled, "Empty portrait rendered a white rectangle");
                var interaction = player.GetComponent<PlayerInteractionController>();
                var prompt = (InteractionPromptUI)Get(interaction, "interactionPrompt");
                Check(interaction.isActiveAndEnabled || prompt == null || !prompt.gameObject.activeSelf, "Interaction prompt remained visible during locked dialogue");
            }
            string scene = SceneManager.GetActiveScene().name;
            if (previousScene != scene) { previousScene = scene; Log("Scene " + scene + ", spawn=" + player.transform.position); }
            if (now - lastLog > 5) { lastLog = now; Log($"State {stage}: x={player.transform.position.x:F2}, y={player.transform.position.y:F2}, HP={health.CurrentHealth}, video={video != null && video.IsPlaying}, dialogue={dialogue.IsPlaying}, quest={QuestTracker.CurrentQuestName}"); }
            double elapsed = now - since;
            if (stage == 0)
            {
                Check(scene == "Forest", "Expected Forest startup");
                if (video != null && video.IsPlaying) { Shot("01-intro"); Next(1, "Skip introductory cutscene"); }
            }
            else if (stage == 1 || stage == 3)
            {
                Keys(Key.Space);
                if (video != null && video.IsPlaying)
                {
                    if (!skipChecked && (float)Get(video, "heldSeconds") > .7f)
                    {
                        var text = (TMPro.TMP_Text)Get(video, "skipText");
                        Check(text.text.StartsWith("스킵중") && !text.text.Contains("%"), "Skip animation text");
                        skipChecked = true; Shot("02-skip-animation");
                    }
                    return;
                }
                if (stage == 1) Next(2, "Deliberate death then restart");
                else { Check(!player.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Dead"), "Revived player still Dead"); Next(4, "Fight all monsters through keyboard"); }
            }
            else if (stage == 2)
            {
                if (elapsed < .5f) return;
                var location = (LocationHUD)Get(flow, "locationHud");
                Check(location.CurrentLocation == "포레스트" && location.GetComponent<CanvasGroup>().alpha > .95f, "Forest arrival notice not visible after intro");
                Shot("02b-forest-location");
                if (!health.IsDead) { health.TakeDamage(10000); Check(health.IsDead, "Lethal damage should kill player"); Next(20, "Waiting for death reload"); }
            }
            else if (stage == 20)
            {
                Keys();
                if (!health.IsDead && elapsed > 1)
                {
                    Check(!player.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Dead"), "Dead animation remained after reload");
                    Next(3, "Skip intro after revive");
                }
            }
            else if (stage == 4)
            {
                Check(!health.IsDead, "Player died during ordinary combat");
                var living = ((MonsterFollowController[])Get(flow, "monsters")).Where(m => m != null && m.CurrentHealth > 0).OrderBy(m => Mathf.Abs(m.transform.position.x - player.transform.position.x)).ToArray();
                if (living.Length == 0) { Shot("03-forest-clear"); Next(5, "Victory dialogue and exit"); }
                else
                {
                    Check(QuestTracker.CurrentQuestName == "모든 몬스터 처치", "Combat guide");
                    float dx = living[0].transform.position.x - player.transform.position.x;
                    if (Mathf.Abs(dx) > 1.6f || dx * player.GetComponent<PlayerSpriteFacing>().FacingDirection < 0) Keys(dx < 0 ? Key.LeftArrow : Key.RightArrow);
                    else Keys(Pulse ? new[] { Key.Z } : Array.Empty<Key>());
                }
            }
            else if (stage == 5)
            {
                if (dialogue.IsPlaying) Advance();
                else if ((bool)Get(flow, "ready"))
                {
                    Check(QuestTracker.CurrentQuestName == "왼쪽 포탈로 이동", "Forest exit guide");
                    Walk(player, portal.transform.position.x, true);
                    if ((bool)Get(flow, "busy")) Next(6, "Forest to Garbage scene transition");
                }
            }
            else if (stage == 6)
            {
                Keys();
                if (scene == "Market") { Check(QuestTracker.CurrentQuestName == "장비 장착하기", "Equipment guide"); Next(7, "Read equipment tutorial"); }
            }
            else if (stage == 7)
            {
                if (dialogue.IsPlaying) Advance();
                else { Next(8, "Open inventory and equip tutorial item"); Keys(Key.I); }
            }
            else if (stage == 8)
            {
                var ui = player.GetComponent<InventoryUI>();
                if (!ui.Window.activeSelf) { Keys(Pulse ? new[] { Key.I } : Array.Empty<Key>()); return; }
                Keys();
                if (elapsed < 1) return;
                if (!equipped)
                {
                    for (int i = 0; i < player.GetComponent<PlayerInventory>().Capacity; i++)
                    {
                        var slot = (PlayerInventory.Slot)typeof(InventoryUI).GetMethod("GetDisplayedSlot", Private).Invoke(ui, new object[] { i });
                        if (slot != null && !slot.IsEmpty && slot.Item.IsEquipment) { gear = slot.Item; ui.DoubleClick(i); equipped = true; break; }
                    }
                    Check(equipped, "No equipment reachable through inventory UI"); Shot("04-equipment");
                }
                if (elapsed > 2) { Call(ui, "CloseInventory"); Next(9, "Garbage portal"); }
            }
            else if (stage == 9)
            {
                if ((bool)Get(flow, "marketMap"))
                {
                    Check(player.GetComponent<PlayerInventory>().GetEquipped(gear.Slot) == gear, "Equipment lost on map move");
                    Check(((LocationHUD)Get(flow, "locationHud")).CurrentLocation == "마켓", "Market location title");
                    Shot("05-market-entry"); Next(10, "Walk to NPC and reject request once");
                }
                else if (dialogue.IsPlaying) Advance();
                else if ((bool)Get(flow, "ready")) { Check(QuestTracker.CurrentQuestName == "마켓으로 이동", "Garbage exit guide"); Walk(player, portal.transform.position.x, true); }
            }
            else if (stage == 10 || stage == 11)
            {
                if (stage == 10 && !marketTitleChecked && elapsed > .7f)
                {
                    var location = (LocationHUD)Get(flow, "locationHud");
                    Check(location.CurrentLocation == "마켓" && location.GetComponent<CanvasGroup>().alpha > .95f, "Market arrival notice not visible");
                    marketTitleChecked = true;
                    Shot("05b-market-location");
                }
                object callback = Get(dialogue, "choiceCallback");
                var choices = (RectTransform)Get(dialogue, "choiceRoot");
                if (callback != null && choices != null && choices.gameObject.activeInHierarchy)
                {
                    var buttons = choices.GetComponentsInChildren<Button>();
                    Check(buttons.Length == 2, "Two NPC choices");
                    buttons[stage == 10 ? 1 : 0].onClick.Invoke();
                    Shot(stage == 10 ? "06-rejection" : "07-acceptance");
                    Next(stage == 10 ? 12 : 13, stage == 10 ? "Finish rejection dialogue" : "Finish accepted dialogue");
                }
                else if (dialogue.IsPlaying) Advance();
                else Walk(player, dialogue.transform.position.x, true);
            }
            else if (stage == 12)
            {
                if (dialogue.IsPlaying) Advance();
                else if (!(bool)Get(flow, "busy"))
                {
                    Check(!portal.activeSelf && QuestTracker.CurrentQuestName == "마켓 NPC와 대화", "Rejection preserves objective and locked exit");
                    Next(11, "Re-offer request and accept");
                }
            }
            else if (stage == 13)
            {
                if (dialogue.IsPlaying) Advance();
                else if ((bool)Get(flow, "ready")) { Check(QuestTracker.CurrentQuestName == "숲에서의 만남", "Accepted quest title"); Next(14, "Final portal"); }
            }
            else if (stage == 14)
            {
                if (video != null && video.IsPlaying) { Shot("08-final-video"); Next(15, "Skip final video"); }
                else Walk(player, portal.transform.position.x, true);
            }
            else if (stage == 15)
            {
                Keys(Key.Space);
                if (video == null || !video.IsPlaying) { Check(player.enabled, "Player controls not restored after video"); Next(16, "Map edge and restored gameplay HUD"); }
            }
            else if (stage == 16)
            {
                Keys(Key.RightArrow);
                if (elapsed > 4)
                {
                    Check(player.GetComponent<BoxCollider2D>().bounds.max.x < 97.30f && player.transform.position.y > -5, "Player escaped Market right edge");
                    Check(UnityEngine.Object.FindObjectsByType<InGameQuestHUD>(FindObjectsSortMode.None).Length == 1, "Missing/duplicate quest HUD");
                    Shot("09-complete");
                    End(true, "Intro/skip animation, death/reload, combat, quest gates, scene travel, inventory equipment, same-scene travel, NPC rejection/acceptance, final video and bounds.");
                }
            }
        }
        catch (Exception error) { End(false, error.ToString()); }
    }
}
