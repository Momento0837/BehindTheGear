using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;

/// <summary>Targeted regression checks; never saves a scene or asset.</summary>
[InitializeOnLoad]
public static class RequestedPolishCheck
{
    private const string Key = "BTG.RequestedPolishCheck";
    private const string Output = "Temp/RequestedPolish/results.txt";
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static double started, stageStarted;
    private static int stage;
    private static StorySceneFlow flow;
    private static PlayerMovement2D player;
    private static Transform slot;
    private static Vector3 scale, position;
    private static MonsterFollowController monster;
    private static ConversationDialogue dialogue;
    private static float normalCameraY;
    static RequestedPolishCheck() => EditorApplication.update += Tick;
    private static object Get(object obj, string field) => obj.GetType().GetField(field, Flags).GetValue(obj);
    private static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Flags).SetValue(obj, value);
    private static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Flags).Invoke(obj, args);
    private static void Check(bool ok, string text)
    {
        if (!ok) throw new Exception(text);
        File.AppendAllText(Output, "PASS: " + text + "\n");
    }
    [MenuItem("Tools/Checks/Requested Polish Check %#F8")]
    public static void Run()
    {
        if (Application.unityVersion != "6000.3.23f1") throw new Exception("Wrong Unity version");
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Run from edit mode");
        started = 0;
        stage = 0;
        Directory.CreateDirectory("Temp/RequestedPolish");
        // Preserve unsaved teammate work in place and as a separate copy before testing.
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene.isDirty) EditorSceneManager.SaveScene(scene, "Temp/RequestedPolish/Unsaved-" + scene.name + ".unity", true);
        }
        File.WriteAllText(Output, "Unity " + Application.unityVersion + "\n");
        var preview = EditorSceneManager.OpenPreviewScene("Assets/Scenes/Controls/ControlSchemeSelect.unity");
        try
        {
            foreach (var button in preview.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Button>(true)))
                Check(button.GetComponent<Image>().color == Color.white, "Control option uses unmultiplied button color: " + button.name);
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/!UI/Other/Interaction.prefab");
        Check(prefab.GetComponentInChildren<InteractionLightPulse>(true) != null, "Interaction prefab has light pulse");
        var shader = Resources.Load<Shader>("Effects/MonsterHitFlash");
        Check(shader != null && !ShaderUtil.ShaderHasError(shader), "White-flash shader imports without errors");
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }
    private static void Next() { stage++; stageStarted = EditorApplication.timeSinceStartup; }
    private static IEnumerator Screenshot(string name)
    {
        yield return null;
        yield return null;
        var texture = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes("Temp/RequestedPolish/" + name + ".png", texture.EncodeToPNG());
        UnityEngine.Object.Destroy(texture);
    }
    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
        try
        {
            if (started == 0) started = stageStarted = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - started > 100) throw new Exception("Timeout stage " + stage);
            double elapsed = EditorApplication.timeSinceStartup - stageStarted;
            if (stage == 0)
            {
                SceneManager.LoadScene("Market");
                Next();
                return;
            }
            if (stage == 1)
            {
                flow = UnityEngine.Object.FindFirstObjectByType<StorySceneFlow>();
                if (flow == null || Get(flow, "inventory") == null || elapsed < 2 || SceneFadeTransition.IsRunning) return;
                player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement2D>();
                var portal = ((GameObject)Get(flow, "portal")).GetComponent<ScenePortal2D>();
                portal.gameObject.SetActive(true);
                Physics2D.SyncTransforms();
                Bounds bounds = portal.GetComponent<BoxCollider2D>().bounds;
                Check(Mathf.Abs(bounds.center.x + 5f) < .01f && Mathf.Abs(bounds.size.x - 6f) < .01f, "Garbage portal is centered at X -5 with width 6");
                Check(Vector2.Distance(bounds.size, portal.GetComponent<SpriteRenderer>().bounds.size) < .01f,
                    "Blue rectangle matches portal collider: " + bounds.size + " / " + portal.GetComponent<SpriteRenderer>().bounds.size);
                player.transform.position = new Vector3(-7.3f, -2.5f, 0f);
                Physics2D.SyncTransforms();
                var controller = player.GetComponent<PlayerInteractionController>();
                Check((Interactable2D)Call(controller, "FindClosestInteractable") == portal.GetComponent<Interactable2D>(), "Portal detected at outer edge while facing away");
                portal.gameObject.SetActive(false);
                var search = UnityEngine.Object.FindObjectsByType<StoryInteraction2D>(FindObjectsSortMode.None).First(x => x.name == "Interaction");
                var pulse = search.GetComponentInChildren<InteractionLightPulse>();
                Set(pulse, "elapsed", 2f - Time.deltaTime);
                Call(pulse, "Update");
                Check(Mathf.Abs(pulse.GetComponent<Light2D>().intensity - 4f) < .001f, "Light reaches intensity 4 after two seconds");
                Set(pulse, "elapsed", 4f - Time.deltaTime);
                Call(pulse, "Update");
                Check(Mathf.Abs(pulse.GetComponent<Light2D>().intensity - 1f) < .001f, "Light returns to intensity 1 after four seconds");
                search.Interact(player.gameObject);
                Check(!search.gameObject.activeSelf, "Cloak pickup hides Interaction object");
                var ui = player.GetComponent<InventoryUI>();
                slot = (Transform)Get(ui, "hintedSlot");
                Check(slot != null, "Equipment tutorial starts slot hint");
                scale = slot.localScale;
                position = slot.localPosition;
                Next();
                return;
            }
            if (stage == 2)
            {
                if (elapsed < 1) return;
                Check(slot.localScale.x > scale.x && slot.localScale.x <= scale.x * 1.051f, "Slot pulses within 100 to 105 percent");
                Check(slot.localPosition == position, "Slot position unchanged during pulse");
                var inventory = player.GetComponent<PlayerInventory>();
                int index = Enumerable.Range(0, inventory.Capacity).First(i => !inventory.GetSlot(i).IsEmpty && inventory.GetSlot(i).Item == Resources.Load<InventoryItemDefinition>("Story/TutorialGear"));
                Check(inventory.EquipFromSlot(index), "Cloak equips through inventory");
                Next();
                return;
            }
            if (stage == 3)
            {
                if (elapsed < .6) return;
                Check(slot.localScale == scale && slot.localPosition == position, "Slot smoothly restores authored size and position");
                Check((bool)Get(flow, "ready"), "Equipping cloak completes quest gate");
                Call(flow, "PrepareMarket");
                Call(flow, "PlacePlayer", new Vector3(63f, -2.5f, 0f));
                dialogue = (ConversationDialogue)Get(flow, "dialogue");
                var playerSprite = player.GetComponent<SpriteRenderer>();
                var npcSprite = dialogue.GetComponent<SpriteRenderer>();
                Check(playerSprite.sortingLayerID == npcSprite.sortingLayerID && playerSprite.sortingOrder > npcSprite.sortingOrder, "Player draws above NPC");
                var gear = Resources.Load<InventoryItemDefinition>("Story/TutorialGear");
                Check(player.GetComponent<PlayerInventory>().GetEquipped(gear.Slot) == gear, "Equipment survives map travel");
                dialogue.Play(new TextAsset("{\"lines\":[{\"speaker\":\"Ethan\",\"text\":\"A          B\"}]}"));
                var portrait = (Image)Get(dialogue, "portraitImage");
                Check(portrait.sprite != null && portrait.enabled, "Ethan portrait resolves original image asset");
                player.StartCoroutine(Screenshot("ethan-portrait"));
                Next();
                return;
            }
            if (stage == 4)
            {
                if (elapsed < .3) return;
                Check(dialogue.BodyText.text == "A          B", "Long spaces retain layout without typing delay");
                Call(dialogue, "FinishDialogue");
                dialogue.Play(new TextAsset("{\"lines\":[{\"speaker\":\"???\",\"text\":\"Portrait check\"}]}"));
                var portrait = (Image)Get(dialogue, "animatedPortrait");
                Call(dialogue, "UpdatePortraitFrame");
                Check(portrait.gameObject.activeInHierarchy && portrait.sprite == dialogue.GetComponent<SpriteRenderer>().sprite, "Mystery portrait follows live animation sprite");
                Check(portrait.GetComponentInParent<RectMask2D>() != null, "Mystery portrait clips to placeholder");
                player.StartCoroutine(Screenshot("mystery-portrait"));
                Next();
                return;
            }
            if (stage == 5)
            {
                if (elapsed < 1.5) return;
                var camera = Camera.main.GetComponent<PlayerCameraFollow2D>();
                normalCameraY = Camera.main.transform.position.y;
                Call(dialogue, "FinishDialogue");
                camera.BeginDeathFocus(player.transform);
                monster = UnityEngine.Object.FindObjectsByType<MonsterFollowController>(FindObjectsInactive.Include, FindObjectsSortMode.None).First();
                monster.gameObject.SetActive(true);
                monster.enabled = false;
                monster.BindPlayer(player.transform);
                monster.SetTutorialProtection(true);
                // Market contains an unused prototype enemy without an Animator.
                // Exercise the same authored controller used by Forest combat.
                var animator = monster.GetComponent<Animator>();
                if (animator == null) animator = monster.gameObject.AddComponent<Animator>();
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/!Animation/Monster/GangB.controller");
                animator.Update(0f);
                Set(monster, "animator", animator);
                Call(monster, "Attack");
                float actionUntil = (float)Get(monster, "actionUntil");
                Check(Mathf.Abs((float)Get(monster, "nextAttackTime") - actionUntil - .7f) < .001f, "Monster recovery starts after attack and lasts 0.7 seconds");
                monster.TakeMeleeDamage(1, 1f);
                Check((string)Get(monster, "animationState") == "Attack" && (float)Get(monster, "actionUntil") == actionUntil, "Nonlethal melee hit does not interrupt attack animation");
                Check(monster.GetComponent<Animator>().speed > 0f, "Attack animation is not frozen by hit stop");
                Check(((SpriteRenderer)Get(monster, "flashOverlay")).enabled, "Hit uses white overlay while retaining original lighting");
                Next();
                return;
            }
            if (stage == 6 && elapsed > 1.5)
            {
                Check(monster.GetComponent<SpriteRenderer>().sharedMaterial == (Material)Get(monster, "originalMaterial"), "Hit fade restores original material");
                Check(!((SpriteRenderer)Get(monster, "flashOverlay")).enabled, "White overlay fades away completely");
                Check(Camera.main.transform.position.y > normalCameraY - .1f, "Death focus preserves gameplay camera height");
                player.StartCoroutine(Screenshot("death-camera"));
                Next();
                return;
            }
            if (stage == 7 && elapsed > .5)
            {
                File.AppendAllText(Output, "ALL TARGETED CHECKS PASSED\n");
                SessionState.SetBool(Key, false);
                if (Application.isBatchMode) EditorApplication.Exit(0);
                else EditorApplication.isPlaying = false;
            }
        }
        catch (Exception error)
        {
            File.AppendAllText(Output, "FAIL: " + error + "\n");
            Debug.LogException(error);
            SessionState.SetBool(Key, false);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else EditorApplication.isPlaying = false;
        }
    }
}
