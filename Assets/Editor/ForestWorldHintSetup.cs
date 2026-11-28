using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using Object = UnityEngine.Object;

// Explicit request files/menu only; never edits an unsaved scene or a player save.
[InitializeOnLoad]
public static class ForestWorldHintSetup
{
    private const string ScenePath = "Assets/Scenes/Main/Forest.unity";
    private const string Folder = "Temp/ForestWorldHints/";
    private const string StateKey = "BTG.ForestWorldHintChecks.State";
    private static readonly string[] Names = { "W", "WASDSpace" };
    private static bool running;
    private static int stage, errors;
    private static double next, deadline;
    private static PlayerMovement2D player;
    private static WorldTutorialSprite[] hints;
    private static Sprite[] firstFrames;
    private static float[] anchorHeights;
    private static readonly List<string> Report = new();

    [Serializable] private sealed class SavedState
    {
        public SceneSetup[] scenes;
        public string startScene;
        public Vector3[] expectedSizes;
    }

    static ForestWorldHintSetup()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += OnPlayStateChanged;
    }

    private static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (File.Exists(Folder + "setup.request"))
        {
            File.Delete(Folder + "setup.request");
            try { Setup(); }
            catch (Exception e) { Result("setup-result.txt", "FAIL\n" + e); Debug.LogException(e); }
        }
        if (File.Exists(Folder + "check.request"))
        {
            File.Delete(Folder + "check.request");
            try { BeginCheck(); }
            catch (Exception e) { Result("check-result.txt", "FAIL\n" + e); Debug.LogException(e); }
        }
        if (!running || !EditorApplication.isPlaying || EditorApplication.isPaused) return;
        try { TickCheck(); }
        catch (Exception e) { FinishCheck("FAIL\n" + e); }
    }

    [MenuItem("Tools/Behind The Gear/Connect Forest World Tutorial GIFs")]
    public static void Setup()
    {
        Require(Application.unityVersion == "6000.3.23f1", "Use Unity 6000.3.23f1.");
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play mode before connecting scene hints.");
        var scene = SceneManager.GetSceneByPath(ScenePath);
        if (scene.IsValid() && scene.isLoaded && scene.isDirty)
        {
            Result("setup-result.txt", "BLOCKED: Forest has unsaved changes. Save the scene, then request setup again.");
            return;
        }
        bool opened = !scene.IsValid() || !scene.isLoaded;
        var active = SceneManager.GetActiveScene();
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            var visuals = FindVisuals(scene);
            string backup = "Backups/ForestWorldHints-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(backup);
            File.Copy(ScenePath, backup + "/Forest.unity");
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Connect Forest tutorial GIFs");
            var lines = new List<string> { "PASS Unity " + Application.unityVersion, "Backup: " + backup };
            Physics2D.SyncTransforms();
            foreach (var visual in visuals)
            {
                var originalPosition = visual.transform.position;
                var originalScale = visual.transform.localScale;
                var originalParent = visual.transform.parent;
                var hint = visual.GetComponent<WorldTutorialSprite>();
                if (hint == null) hint = Undo.AddComponent<WorldTutorialSprite>(visual.gameObject);
                Undo.RecordObject(hint, "Set tutorial animation");
                Undo.RecordObject(visual.transform, "Align tutorial height");
                hint.Configure(visual.name);
                Require(hint.AlignToInteractionHeight(), visual.name + ": no valid player/floor height");
                Require(Mathf.Approximately(visual.transform.position.x, originalPosition.x)
                    && Mathf.Approximately(visual.transform.position.z, originalPosition.z)
                    && visual.transform.localScale == originalScale && visual.transform.parent == originalParent,
                    visual.name + ": preserve authored X/Z, scale and parent");
                EditorUtility.SetDirty(hint);
                lines.Add(visual.name + ": " + hint.AnimationResource + ", world Y " + visual.transform.position.y.ToString("F3")
                    + ", original parent and scale preserved");
            }
            Undo.CollapseUndoOperations(undo);
            EditorSceneManager.MarkSceneDirty(scene);
            Require(EditorSceneManager.SaveScene(scene), "Forest scene saved");
            Result("setup-result.txt", string.Join("\n", lines));
        }
        finally
        {
            if (opened && scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        }
    }

    private static SpriteRenderer[] FindVisuals(Scene scene)
    {
        var sprites = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<SpriteRenderer>(true)).ToArray();
        return Names.Select(name =>
        {
            var matches = sprites.Where(sprite => sprite.name == name).ToArray();
            Require(matches.Length == 1, "Exactly one authored Forest " + name + " SpriteRenderer");
            Require(matches[0].sprite != null, name + " source sprite is connected");
            return matches[0];
        }).ToArray();
    }

    private static void BeginCheck()
    {
        Require(Application.unityVersion == "6000.3.23f1", "Use Unity 6000.3.23f1.");
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play mode before the isolated Forest hint check.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            Require(!SceneManager.GetSceneAt(i).isDirty, "Save open scenes before the isolated Forest hint check.");
        var saved = new SavedState
        {
            scenes = EditorSceneManager.GetSceneManagerSetup(),
            startScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)
        };
        // Persist restoration data before opening the test scene.
        SessionState.SetString(StateKey, JsonUtility.ToJson(saved));
        try
        {
            EditorSceneManager.playModeStartScene = null;
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var visuals = FindVisuals(scene);
            foreach (var visual in visuals)
                Require(visual.GetComponent<WorldTutorialSprite>() != null, visual.name + " world animation component exists");
            saved.expectedSizes = visuals.Select(WorldSize).ToArray();
            SessionState.SetString(StateKey, JsonUtility.ToJson(saved));
            EditorApplication.isPlaying = true;
        }
        catch
        {
            RestoreEditor();
            throw;
        }
    }

    private static void OnPlayStateChanged(PlayModeStateChange state)
    {
        if (string.IsNullOrEmpty(SessionState.GetString(StateKey, ""))) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            running = true; stage = 0; errors = 0; Report.Clear();
            next = EditorApplication.timeSinceStartup + .1;
            deadline = EditorApplication.timeSinceStartup + 8;
            Application.logMessageReceived += Observe;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            Application.logMessageReceived -= Observe;
            running = false;
            RestoreEditor();
        }
    }

    private static void TickCheck()
    {
        double now = EditorApplication.timeSinceStartup;
        Require(now < deadline, "Forest world hint check finished within 8 seconds");
        if (now < next) return;
        var scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) return;
        if (stage == 0)
        {
            // Only suspend story actors in this temporary Play session; no serialized changes.
            foreach (var flow in Object.FindObjectsByType<StorySceneFlow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (flow.gameObject.scene == scene) flow.StopAllCoroutines();
            foreach (var cutscene in Object.FindObjectsByType<CutsceneVideoSequence>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (cutscene.gameObject.scene == scene) cutscene.StopAllCoroutines();
            foreach (var video in Object.FindObjectsByType<VideoPlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (video.gameObject.scene == scene) video.Stop();
            player = Object.FindObjectsByType<PlayerMovement2D>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(actor => actor.gameObject.scene == scene);
            Require(player != null, "Forest player exists");
            player.enabled = false;
            var input = player.GetComponent<PlayerInputReader>();
            if (input != null) input.enabled = false;
            foreach (var monster in Object.FindObjectsByType<MonsterFollowController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (monster.gameObject.scene == scene) monster.enabled = false;
            foreach (var body in Object.FindObjectsByType<Rigidbody2D>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (body.gameObject.scene == scene) body.bodyType = RigidbodyType2D.Kinematic;

            var visuals = FindVisuals(scene);
            hints = visuals.Select(visual => visual.GetComponent<WorldTutorialSprite>()).ToArray();
            foreach (var hint in hints)
            {
                Require(hint != null, "Authored world GIF has its animation component");
                for (var parent = hint.transform; parent != null; parent = parent.parent) parent.gameObject.SetActive(true);
                Require(hint.AlignToInteractionHeight(), hint.name + " aligns to interaction height");
            }
            Physics2D.SyncTransforms();
            stage = 1; next = now + .15; return;
        }
        var saved = JsonUtility.FromJson<SavedState>(SessionState.GetString(StateKey, ""));
        if (stage == 1)
        {
            firstFrames = hints.Select(hint => hint.GetComponent<SpriteRenderer>().sprite).ToArray();
            anchorHeights = hints.Select(hint => hint.transform.position.y).ToArray();
            for (int i = 0; i < hints.Length; i++)
            {
                Require(firstFrames[i] != null, Names[i] + " has a rendered GIF frame");
                var actual = WorldSize(hints[i].GetComponent<SpriteRenderer>());
                Check(Vector2.Distance(actual, saved.expectedSizes[i]) < .001f, Names[i] + " keeps authored sprite dimensions " + actual);
                var hits = Physics2D.RaycastAll(hints[i].transform.position, Vector2.down, 30f, LayerMask.GetMask("Ground"));
                var floor = hits.FirstOrDefault(hit => hit.collider != null && !hit.collider.isTrigger);
                Require(floor.collider != null, Names[i] + " floor is found");
                float expectedY = floor.point.y + player.GetComponent<Collider2D>().bounds.size.y + 1.05f;
                Check(Mathf.Abs(anchorHeights[i] - expectedY) < .01f, Names[i] + " matches F height " + expectedY.ToString("F3"));
            }
            stage = 2; next = now + .4; return;
        }
        if (stage == 2)
        {
            for (int i = 0; i < hints.Length; i++)
                Check(hints[i].GetComponent<SpriteRenderer>().sprite != firstFrames[i], Names[i] + " advances GIF frames");
            player.transform.position += Vector3.up * 3f;
            Physics2D.SyncTransforms();
            foreach (var hint in hints) hint.AlignToInteractionHeight();
            stage = 3; next = now + .2; return;
        }
        for (int i = 0; i < hints.Length; i++)
            Check(Mathf.Abs(hints[i].transform.position.y - anchorHeights[i]) < .01f, Names[i] + " stays at fixed floor height when player jumps");
        Check(errors == 0, "No observed runtime errors");
        FinishCheck("PASS Forest world tutorial GIF checks");
    }

    private static Vector3 WorldSize(SpriteRenderer visual) =>
        Vector3.Scale(visual.sprite.bounds.size, new Vector3(Mathf.Abs(visual.transform.lossyScale.x),
            Mathf.Abs(visual.transform.lossyScale.y), Mathf.Abs(visual.transform.lossyScale.z)));
    private static void Observe(string message, string trace, LogType type)
    {
        if (type is LogType.Error or LogType.Exception) { errors++; Report.Add("ERROR " + message); }
    }
    private static void Check(bool condition, string message)
    {
        Require(condition, message); Report.Add("PASS " + message);
    }
    private static void FinishCheck(string result)
    {
        Result("check-result.txt", string.Join("\n", Report) + "\n" + result);
        Application.logMessageReceived -= Observe;
        running = false;
        EditorApplication.isPlaying = false;
    }
    private static void RestoreEditor()
    {
        string json = SessionState.GetString(StateKey, "");
        SessionState.EraseString(StateKey);
        if (string.IsNullOrEmpty(json)) return;
        var saved = JsonUtility.FromJson<SavedState>(json);
        EditorSceneManager.RestoreSceneManagerSetup(saved.scenes);
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(saved.startScene) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(saved.startScene);
    }
    private static void Result(string file, string text)
    {
        Directory.CreateDirectory(Folder); File.WriteAllText(Folder + file, text);
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
