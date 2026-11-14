#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class ChapterFourCheck
{
    const string Folder = "Temp/ChapterFourValidation", Key = "BTG.ChapterFourCheck";
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    static int stage;
    static double next, deadline;
    static StorySceneFlow flow;
    static PlayerMovement2D player;
    static ConversationDialogue dialogue;
    static MarketNpcBubble bubble;
    static float walkStart;
    static object Read(object target, string field) => target.GetType().GetField(field, Flags).GetValue(target);
    static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Flags).Invoke(target, args);
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static void Next(int value, double delay = .2) { stage = value; next = EditorApplication.timeSinceStartup + delay; File.AppendAllText(Folder + "/progress.txt", "Stage " + stage + "\n"); }
    static void Place(float x)
    {
        player.transform.position = new Vector3(x, -1f, 0);
        player.GetComponent<Rigidbody2D>().position = player.transform.position;
        player.SettleOnGround();
        Physics2D.SyncTransforms();
    }
    static void Finish(string result)
    {
        File.WriteAllText(Folder + "/result.txt", result);
        SessionState.SetBool(Key, false);
        EditorApplication.isPlaying = false;
    }
    static ChapterFourCheck() => EditorApplication.update += Tick;
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
            SessionState.SetBool(Key, true); stage = 0;
            File.WriteAllText(Folder + "/progress.txt", "Start\n");
            EditorApplication.isPlaying = true;
        }
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (stage == 0)
            {
                deadline = EditorApplication.timeSinceStartup + 100;
                StorySceneFlow.RestartAtCombat = true;
                EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/Main/Forest.unity", new LoadSceneParameters(LoadSceneMode.Single));
                Next(1, 4); return;
            }
            Require(EditorApplication.timeSinceStartup < deadline, "Timeout at " + stage);
            if (EditorApplication.timeSinceStartup < next) return;
            flow = UnityEngine.Object.FindFirstObjectByType<StorySceneFlow>();
            player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement2D>();
            if (flow == null || player == null) return;
            dialogue = (ConversationDialogue)Read(flow, "dialogue");
            if (dialogue == null) return;
            switch (stage)
            {
                case 1:
                    int damage = player.GetComponent<PlayerMeleeAttack>().ResolvedDamage;
                    var enemies = UnityEngine.Object.FindObjectsByType<MonsterFollowController>(FindObjectsSortMode.None);
                    Require(enemies.Length >= 3 && enemies.All(m => m.CurrentHealth == damage * 2), "Opening monsters have two-hit health");
                    foreach (var enemy in enemies) enemy.enabled = false;
                    enemies[0].TakeDamage(damage); Require(enemies[0].CurrentHealth == damage, "First hit hurts without player damage");
                    enemies[0].TakeDamage(damage); Require(enemies[0].CurrentHealth == 0, "Second hit kills without player damage");
                    SceneFadeTransition.LoadScene("Market", .1f); Next(2, 1); break;
                case 2:
                    if (SceneFadeTransition.IsRunning || Read(flow, "inventory") == null) return;
                    flow.StopAllCoroutines();
                    Call(flow, "PrepareMarket");
                    Place(64.4f); player.GetComponent<PlayerSpriteFacing>().FaceAttack(-1);
                    player.GetComponent<PlayerInteractionController>().TryInteract();
                    Require(dialogue.IsPlaying, "NPC interaction works while facing away on left");
                    Next(3, .8); break;
                case 3:
                    Require(!dialogue.IsChoosing && dialogue.BodyText.text == "······.", "Before greeting, ellipsis only with no choices");
                    Require(DialoguePresentation.IsActive, "Dialogue requests hidden cursor");
                    var triangle = (RectTransform)Read(dialogue, "nextTriangle");
                    Require(triangle.gameObject.activeInHierarchy && triangle.GetComponent<Image>().sprite != null && triangle.GetComponent<Button>() == null, "Triangle replaces next button");
                    Require(triangle.GetComponent<MenuImageSoftEffect>() != null, "Triangle glow configured");
                    ScreenCapture.CaptureScreenshot(Folder + "/triangle.png");
                    Next(30); break;
                case 30:
                    Call(dialogue, "RequestAdvance"); Next(4); break;
                case 4:
                    Require(!dialogue.IsPlaying && !DialoguePresentation.IsActive, "Early dialogue closes cleanly");
                    Place(70.1f); Next(5, .3); break;
                case 5:
                    bubble = (MarketNpcBubble)Read(flow, "marketBubble");
                    Require(bubble != null && bubble.HasPresented && bubble.IsPresenting && DialoguePresentation.IsActive, "Automatic greeting locks and hides cursor");
                    Next(6, .4); break;
                case 6:
                    if (bubble.IsPresenting) { Call(bubble, "Advance"); next = EditorApplication.timeSinceStartup + .5; return; }
                    Require(dialogue.GetComponent<WorldInteractionHint>() != null, "Greeting arms world F GIF");
                    Require(dialogue.GetComponent<WorldInteractionHint>().ShowUntilDismissed, "Greeting hint does not require returning closer first");
                    Place(65.6f); player.GetComponent<PlayerSpriteFacing>().FaceAttack(1);
                    Next(7, .5); break;
                case 7:
                    var hint = dialogue.GetComponent<WorldInteractionHint>();
                    Require(((SpriteRenderer)Read(hint, "visual")).color.a > .5f, "NPC F GIF visible nearby");
                    player.GetComponent<PlayerInteractionController>().TryInteract();
                    Require(dialogue.IsPlaying, "NPC interaction works while facing away on right");
                    Next(8, .9); break;
                case 8:
                    Require(dialogue.IsChoosing, "Greeting enables accept/reject choices");
                    Require(((SpriteRenderer)Read(dialogue.GetComponent<WorldInteractionHint>(), "visual")).color.a < .01f, "F hint fades away after interaction");
                    Call(dialogue, "SelectChoice", true); Next(9, .2); break;
                case 9:
                    if (dialogue.IsPlaying) { Call(dialogue, "RequestAdvance"); next = EditorApplication.timeSinceStartup + .2; return; }
                    if (!(bool)Read(flow, "ready")) return;
                    var portal = (GameObject)Read(flow, "portal");
                    portal.GetComponent<Interactable2D>().Interact(player.gameObject);
                    Next(10, .4); break;
                case 10:
                    var video = (CutsceneVideoSequence)Read(flow, "cutscene");
                    Require(video != null && video.IsPlaying, "Market exit starts cutscene");
                    Call(video, "OnVideoFinished", new object[] { null }); Next(11, 2.5); break;
                case 11:
                    if (SceneFadeTransition.IsRunning) return;
                    Require(SceneManager.GetActiveScene().name == "Forest", "Cutscene skip reaches Forest");
                    var bounds = player.GetComponent<PlayerMapBounds2D>().HorizontalBounds;
                    Require(bounds.x > 30 && player.transform.position.x > bounds.x && player.transform.position.x < bounds.x + 4, "Chapter 4 spawns inside left map boundary");
                    Require(Mathf.Abs(player.GetComponent<Rigidbody2D>().linearVelocity.y) < .1f, "Chapter 4 has a grounded entrance");
                    var originalFloor = GameObject.Find("Ground").GetComponent<Collider2D>();
                    var copiedFloor = GameObject.Find("Story Ground").GetComponent<Collider2D>();
                    Require(Mathf.Abs(originalFloor.bounds.max.y - copiedFloor.bounds.max.y) < .001f,
                        "Copied Forest uses the original Forest floor height");
                    Require((Vector2)Read(Camera.main.GetComponent<PlayerCameraFollow2D>(), "horizontalWorldBounds") == bounds, "Chapter 4 camera matches movement boundaries");
                    var npc = GameObject.Find("Selena");
                    Require(npc != null && npc.GetComponent<Animator>().runtimeAnimatorController != null, "Selena has Idle/Walk controller");
                    Place(npc.transform.position.x - .6f);
                    npc.GetComponent<Interactable2D>().Interact(player.gameObject);
                    Require(dialogue.IsPlaying && dialogue.CurrentSpeaker == "Selena", "Selena JSON plays");
                    Next(12, .2); break;
                case 12:
                    if (dialogue.IsPlaying) { Call(dialogue, "RequestAdvance"); next = EditorApplication.timeSinceStartup + .2; return; }
                    walkStart = player.transform.position.x; Next(13, .4); break;
                case 13:
                    Require(player.transform.position.x > walkStart && !player.enabled && player.GetComponent<PlayerControlLock>().IsLocked, "Scripted departure moves while gameplay is locked");
                    Require(player.GetComponent<Animator>().GetBool("IsMoving"), "Player locomotion animator runs during departure");
                    Require(player.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).IsName("Running"), "Player actually enters Running animation");
                    Require(!GameObject.Find("Selena").GetComponent<Animator>().GetBool("IsMoving"), "Ethan leaves before Selena");
                    Next(31, .6); break;
                case 31:
                    Require(GameObject.Find("Selena").GetComponent<Animator>().GetBool("IsMoving"), "Selena follows after Ethan has a lead");
                    Require(player.transform.position.x > GameObject.Find("Selena").transform.position.x + .8f, "Selena keeps behind Ethan");
                    ScreenCapture.CaptureScreenshot(Folder + "/selena-walking.png");
                    Next(14, 3); break;
                case 14:
                    if (SceneFadeTransition.IsRunning) return;
                    Require(SceneManager.GetActiveScene().name == "Market", "Selena departure reaches Market");
                    Require(GameObject.Find("#5_Neighborhood") != null, "Neighborhood map selected");
                    var companion = GameObject.Find("Selena");
                    Require(companion != null && companion.scene == SceneManager.GetActiveScene() && companion.transform.position.x < player.transform.position.x,
                        "Selena arrives in Neighborhood behind Ethan");
                    var area = player.GetComponent<PlayerMapBounds2D>().HorizontalBounds;
                    Require(area.x > 100 && player.transform.position.x >= area.x && player.transform.position.x < area.x + 4, "Neighborhood left entrance and movement bounds");
                    Require((Vector2)Read(Camera.main.GetComponent<PlayerCameraFollow2D>(), "horizontalWorldBounds") == area, "Neighborhood camera matches movement boundaries");
                    Require(!player.GetComponent<PlayerControlLock>().IsLocked && player.enabled && player.GetComponent<InventoryUI>().enabled, "Controls and inventory restored after travel");
                    Require(!player.GetComponent<Animator>().GetBool("IsMoving"), "Cinematic walk ends after arrival");
                    File.WriteAllText(Folder + "/arrival.txt", "Player=" + player.transform.position + " Bounds=" + area + " Camera=" + Camera.main.transform.position);
                    ScreenCapture.CaptureScreenshot(Folder + "/neighborhood.png");
                    Next(15, .3); break;
                case 15:
                    Finish("PASS Unity " + Application.unityVersion + ": two-hit opening enemies; early ellipsis; both-side NPC interaction; greeting cursor suppression and F fade; choices; triangle/glow; cutscene completion callback to chapter 4; Selena JSON, actual Running state, neighborhood arrival, camera/movement bounds and restored controls."); break;
            }
        }
        catch (Exception error) { Finish("FAIL stage " + stage + "\n" + error); Debug.LogException(error); }
    }
}
#endif
