#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using TMPro;

[InitializeOnLoad]
public static class InteractionInfoCheck
{
    private const string Key = "BTG.InteractionInfoCheck";
    private const string Folder = "Temp/InteractionInfoValidation";
    private static double started;
    private static double next;
    private static float originalLight;
    private static float peak;
    private static float previousWidth;
    private static bool sawEntrance;
    private static bool sawExit;
    private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static InteractionInfoCheck() { EditorApplication.update += Tick; }
    [MenuItem("Tools/Checks/Interaction and Info")]
    public static void Run()
    {
        if (Application.unityVersion != "6000.3.23f1" || EditorApplication.isPlayingOrWillChangePlaymode) return;
        started = 0;
        peak = previousWidth = 0f;
        sawEntrance = sawExit = false;
        Directory.CreateDirectory(Folder);
        SessionState.SetInt(Key, 1);
        EditorApplication.isPlaying = true;
    }
    private static object Read(object target, string field) => target.GetType().GetField(field, Flags).GetValue(target);
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Tick()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && File.Exists(Folder + "/run"))
        {
            File.Delete(Folder + "/run");
            Run();
            return;
        }
        int stage = SessionState.GetInt(Key, 0);
        if (stage == 0 || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (started == 0) started = EditorApplication.timeSinceStartup;
        try
        {
            Require(EditorApplication.timeSinceStartup - started < 60, "Timeout stage " + stage);
            if (stage == 1)
            {
                SceneManager.LoadScene("Market");
                SessionState.SetInt(Key, 2);
                return;
            }
            var flow = UnityEngine.Object.FindFirstObjectByType<StorySceneFlow>();
            if (flow == null || Read(flow, "inventory") == null || SceneFadeTransition.IsRunning) return;
            var player = (PlayerMovement2D)Read(flow, "player");
            var inventory = player.GetComponent<PlayerInventory>();
            var hud = UnityEngine.Object.FindFirstObjectByType<LocationHUD>();
            var panel = hud.CanvasRoot.GetComponent<RectTransform>();
            var label = panel.GetComponentInChildren<TMP_Text>();
            var search = UnityEngine.Object.FindObjectsByType<Interactable2D>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(i => i.name == "Interaction");
            var light = search.GetComponentInChildren<Light2D>(true);
            var controller = player.GetComponent<PlayerInteractionController>();
            switch (stage)
            {
                case 2:
                    if (QuestTracker.CurrentQuestName != "쓰레기에서 걸칠만한 것을 찾기") return;
                    Require(panel.name == "Info", "Uses authored Info");
                    Require(label.color == Color.white, "White font");
                    player.enabled = false;
                    var body = player.GetComponent<Rigidbody2D>();
                    body.linearVelocity = Vector2.zero;
                    body.gravityScale = 0;
                    foreach (float side in new[] { -1f, 1f })
                    {
                        body.position = (Vector2)search.transform.position + Vector2.right * side * .75f;
                        player.transform.position = body.position;
                        Physics2D.SyncTransforms();
                        var target = typeof(PlayerInteractionController).GetMethod("FindClosestInteractable", Flags).Invoke(controller, null);
                        Require(ReferenceEquals(target, search), "Interaction detected from side " + side);
                    }
                    originalLight = light.intensity;
                    controller.TryInteract();
                    Require(QuestTracker.CurrentQuestName == "망토 두르기", "F controller collects cloak");
                    Require(search.gameObject.activeSelf && !search.enabled, "Object remains visible and cannot collect twice");
                    int count = Enumerable.Range(0, inventory.Capacity).Count(i => !inventory.GetSlot(i).IsEmpty && inventory.GetSlot(i).Item == Resources.Load<InventoryItemDefinition>("Story/TutorialGear"));
                    controller.TryInteract();
                    Require(count == Enumerable.Range(0, inventory.Capacity).Count(i => !inventory.GetSlot(i).IsEmpty && inventory.GetSlot(i).Item == Resources.Load<InventoryItemDefinition>("Story/TutorialGear")), "No duplicate cloak");
                    next = EditorApplication.timeSinceStartup + .9;
                    SessionState.SetInt(Key, 3);
                    break;
                case 3:
                    peak = Mathf.Max(peak, light.intensity);
                    if (EditorApplication.timeSinceStartup < next) return;
                    Require(peak >= 4.99f, "Light reaches 5; peak=" + peak);
                    Require(Mathf.Approximately(light.intensity, originalLight), "Light restores original intensity");
                    SessionState.SetInt(Key, 4);
                    break;
                case 4:
                    if (label.text != "망토 획득") return;
                    float width = panel.rect.width;
                    float height = panel.rect.height;
                    Require(height >= 0f && height <= 152.01f, "Height stays 0 to 152");
                    if (height < 151.99f) Require(Mathf.Abs(width - 1f) < .01f, "Width stays 1 until height reaches 152");
                    if (width > 1f && (float)Read(hud, "elapsed") < .5f)
                    {
                        Require(Mathf.Abs(height - 152f) < .01f, "Height is fixed before width grows");
                        Require(width >= previousWidth, "Width grows monotonically");
                        previousWidth = width;
                        sawEntrance = true;
                    }
                    var group = panel.GetComponent<CanvasGroup>();
                    if (group.alpha > .99f && (float)Read(hud, "elapsed") >= .5f)
                    {
                        Require(Mathf.Abs(height - 152f) < .01f, "Final height 152");
                        Require(panel.anchorMin == new Vector2(.5f, 1f) && panel.anchoredPosition.y < -80f, "Popup uses old top position");
                        Require(width > label.preferredWidth + 24f, "Text has image padding");
                        ScreenCapture.CaptureScreenshot(Folder + "/cloak-info.png");
                    }
                    if (panel.anchoredPosition.y < ((Vector2)Read(hud, "restingPosition")).y && group.alpha < .99f) sawExit = true;
                    if ((bool)Read(hud, "presenting")) return;
                    Require(sawEntrance && sawExit, "Observed growth entrance and downward fade");
                    var item = AssetDatabase.FindAssets("t:InventoryItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<InventoryItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).First(i => i.IsConsumable);
                    var pickup = new GameObject("Validation Pickup").AddComponent<WorldItemPickup>();
                    pickup.Configure(item, 2);
                    typeof(WorldItemPickup).GetMethod("TryCollect", Flags).Invoke(pickup, new object[] { inventory });
                    Require(label.text == item.DisplayName + " ×2 획득", "Consumable pickup popup");
                    File.WriteAllText(Folder + "/result.txt", "PASS Unity " + Application.unityVersion + "\nDetection on both sides; controller interaction grants cloak once; light peaks at 5 and restores; authored Info white text; height 0 to 152 before width grows from 1; top position; padding; downward fade; consumable pickup notice.\n");
                    SessionState.SetInt(Key, 0);
                    EditorApplication.isPlaying = false;
                    break;
            }
        }
        catch (Exception error)
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder + "/result.txt", "FAIL stage " + stage + "\n" + error);
            SessionState.SetInt(Key, 0);
            Debug.LogException(error);
            EditorApplication.isPlaying = false;
        }
    }
}
#endif
