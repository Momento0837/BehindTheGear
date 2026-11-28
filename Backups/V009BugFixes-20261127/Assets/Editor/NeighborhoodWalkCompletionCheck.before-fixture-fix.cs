#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using BehindTheGear.HideOut;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Opt-in, isolated real 2D physics + the production story coroutine. Never saves scenes or game data.
[InitializeOnLoad]
public static class NeighborhoodWalkCompletionCheck
{
    private const string Folder = "Logs/Validation/NeighborhoodWalk";
    private const string Pending = "BTG.NeighborhoodWalkCheck.Pending";
    private const string StartedPlay = "BTG.NeighborhoodWalkCheck.StartedPlay";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private const string SavedScenes = "BTG.NeighborhoodWalkCheck.Scenes";
    [Serializable] private sealed class SavedSetup { public SavedScene[] scenes; }
    [Serializable] private sealed class SavedScene { public string path; public bool loaded, active; }
    static NeighborhoodWalkCompletionCheck()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            string json = SessionState.GetString(SavedScenes, "");
            if (string.IsNullOrEmpty(json)) return;
            SessionState.EraseString(SavedScenes);
            var saved = JsonUtility.FromJson<SavedSetup>(json).scenes;
            var setup = new SceneSetup[saved.Length];
            for (int i = 0; i < saved.Length; i++)
                setup[i] = new SceneSetup { path = saved[i].path, isLoaded = saved[i].loaded, isActive = saved[i].active };
            EditorSceneManager.RestoreSceneManagerSetup(setup);
        };
    }

    private static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (File.Exists(Folder + "/check.request"))
        {
            File.Delete(Folder + "/check.request");
            Run();
        }
        else if (Application.isPlaying && !EditorApplication.isPaused && SessionState.GetBool(Pending, false))
        {
            SessionState.EraseBool(Pending);
            Check();
        }
    }

    [MenuItem("Tools/Checks/Neighborhood Walk Completion")]
    public static void Run()
    {
        if (Application.unityVersion != "6000.3.23f1") throw new InvalidOperationException("Requires Unity 6000.3.23f1.");
        Directory.CreateDirectory(Folder);
        if (!Application.isPlaying)
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save open scenes before starting this check.");
            var saved = new SavedSetup { scenes = new SavedScene[setup.Length] };
            for (int i = 0; i < setup.Length; i++)
            {
                if (string.IsNullOrEmpty(setup[i].path))
                    throw new InvalidOperationException("Save the untitled scene before starting this check.");
                saved.scenes[i] = new SavedScene { path = setup[i].path, loaded = setup[i].isLoaded, active = setup[i].isActive };
            }
            SessionState.SetString(SavedScenes, JsonUtility.ToJson(saved));
            // Never enter Play on the user's story scene: its Start/save hooks must not run.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            File.WriteAllText(Folder + "/result.txt", "WAITING for Play Mode in a disposable empty scene.\n");
            SessionState.SetBool(Pending, true);
            SessionState.SetBool(StartedPlay, true);
            EditorApplication.EnterPlaymode();
            return;
        }
        Check();
    }

    private static void Check()
    {
        var report = new StringBuilder();
        Scene fixture = default;
        var previousScene = SceneManager.GetActiveScene();
        object previousOwner = typeof(QuestTracker).GetField("owner", Static).GetValue(null);
        string previousTitle = QuestTracker.CurrentQuestName, previousObjective = QuestTracker.CurrentObjective;
        bool previousShow = QuestTracker.ShowObjective, previousCompletion = QuestTracker.IsCompletionChange;
        var cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var cameraStates = new bool[cameras.Length];
        IEnumerator routine = null;
        try
        {
            for (int i = 0; i < cameras.Length; i++) { cameraStates[i] = cameras[i].enabled; cameras[i].enabled = false; }
            fixture = SceneManager.CreateScene("Neighborhood walk isolated physics", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            SceneManager.SetActiveScene(fixture);
            var physics = fixture.GetPhysicsScene2D();
            Require(physics.IsValid() && physics != Physics2D.defaultPhysicsScene, "Isolated local physics scene.");
            var floor = New(fixture, "Floor", new Vector2(170, -4.23f)).AddComponent<BoxCollider2D>();
            floor.size = new Vector2(60, 1); floor.gameObject.layer = LayerMask.NameToLayer("Ground");
            var actor = New(fixture, "Player", new Vector2(160, -2.5f)); actor.SetActive(false);
            var player = actor.AddComponent<PlayerMovement2D>();
            var body = actor.GetComponent<Rigidbody2D>();
            body.gravityScale = 3; body.constraints = RigidbodyConstraints2D.FreezeRotation;
            var box = actor.GetComponent<BoxCollider2D>(); box.size = new Vector2(.9f, 2.15f);
            var sprite = actor.AddComponent<SpriteRenderer>();
            var authoredPlayer = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Player/SavePlayerRig.prefab");
            if (authoredPlayer != null) sprite.sprite = authoredPlayer.GetComponentInChildren<SpriteRenderer>(true)?.sprite;
            var controls = actor.AddComponent<PlayerControlLock>();
            Set(controls, "playerBody", body);
            Set(controls, "controlsToDisable", new Behaviour[] { player, actor.GetComponent<PlayerInputReader>() });
            var mapBounds = actor.AddComponent<PlayerMapBounds2D>();
            actor.SetActive(true);
            var entrance = New(fixture, "Hideout entrance", new Vector2(179.76f, -2.5f)).AddComponent<HideOutEntrance>();
            entrance.GetComponent<BoxCollider2D>().isTrigger = true;
            var selena = New(fixture, "Selena", new Vector2(157.6f, -2.5f)).transform;
            var owner = New(fixture, "Disabled story runner", Vector2.zero); owner.SetActive(false);
            var flow = owner.AddComponent<StorySceneFlow>();
            Set(flow, "player", player); Set(flow, "selena", selena);
            float distance = (float)typeof(StorySceneFlow).GetProperty("NeighborhoodStopDistance", Private).GetValue(flow);
            float authoredStop = entrance.transform.position.x - distance;
            mapBounds.SetHorizontalBounds(new Vector2(150, 190));
            Physics2D.SyncTransforms();
            for (int tick = 0; tick < 100; tick++) physics.Simulate(.02f);

            // Reproduce the former 0.001 arrival condition under real default contact friction.
            controls.Acquire();
            bool legacyCompleted = false;
            for (int tick = 0; tick < 1500; tick++)
            {
                float remaining = authoredStop - body.position.x;
                if (remaining <= .001f) { legacyCompleted = true; break; }
                body.linearVelocity = new Vector2(Mathf.Min(player.RunningSpeed, remaining / Mathf.Max(Time.fixedDeltaTime, Time.deltaTime)), body.linearVelocity.y);
                physics.Simulate(Time.fixedDeltaTime);
            }
            report.AppendLine("Legacy condition completed=" + legacyCompleted + ", residual=" + (authoredStop - body.position.x).ToString("F6"));
            controls.Release();

            for (int scenario = 0; scenario < 2; scenario++)
            {
                body.position = new Vector2(160, body.position.y); body.linearVelocity = Vector2.zero;
                mapBounds.SetHorizontalBounds(new Vector2(150, scenario == 0 ? 190 : authoredStop));
                Set(flow, "currentGuide", "selena_take_cover");
                var state = actor.GetComponent<PrototypeTutorialState>(); if (state != null) state.SelenaArrived = false;
                float expected = scenario == 0 ? authoredStop : authoredStop - box.bounds.extents.x;
                routine = (IEnumerator)Call(flow, "WalkToHideout");
                int steps = 0;
                bool completed = false;
                for (; steps < 1500; steps++)
                {
                    if (!routine.MoveNext()) { completed = true; break; }
                    Require(controls.IsLocked && !player.enabled, "Player movement remains locked while walking.");
                    Call(mapBounds, "FixedUpdate");
                    physics.Simulate(Time.fixedDeltaTime);
                    Call(mapBounds, "LateUpdate");
                }
                Require(completed, "Production walk must terminate; x=" + body.position.x + ", target=" + expected);
                (routine as IDisposable)?.Dispose(); routine = null;
                Require(Mathf.Abs(body.position.x - expected) < .002f, "Stops at reachable entrance target.");
                Require(actor.GetComponent<PrototypeTutorialState>()?.SelenaArrived == true, "Sets SelenaArrived.");
                Require((string)Get(flow, "currentGuide") == "neighborhood_hideout", "Advances the actual quest to the crawlspace entrance.");
                Require(!controls.IsLocked && player.enabled && actor.GetComponent<PlayerInputReader>().enabled, "Releases input and movement.");
                Require(!(bool)Get(player, "cinematicWalking") && Mathf.Abs(body.linearVelocity.x) < .001f, "Stops walking animation and horizontal velocity.");
                Require(entrance.GetComponent<WorldInteractionHint>()?.ShowUntilDismissed == true, "Enables the entrance interaction hint.");
                report.AppendLine("PASS scenario=" + scenario + ", physics steps=" + steps + ", stopped x=" + body.position.x.ToString("F5") + ", SelenaArrived=true, quest=neighborhood_hideout, controls restored");
            }
            report.AppendLine("RESULT: PASS");
        }
        catch (Exception error) { report.AppendLine("RESULT: FAIL\n" + error); Debug.LogException(error); }
        finally
        {
            (routine as IDisposable)?.Dispose();
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (fixture.IsValid()) { foreach (var root in fixture.GetRootGameObjects()) Object.DestroyImmediate(root); SceneManager.UnloadSceneAsync(fixture); }
            QuestTracker.Restore(previousOwner, previousTitle, previousObjective, previousShow);
            typeof(QuestTracker).GetField("<IsCompletionChange>k__BackingField", Static)?.SetValue(null, previousCompletion);
            for (int i = 0; i < cameras.Length; i++) if (cameras[i] != null) cameras[i].enabled = cameraStates[i];
            File.WriteAllText(Folder + "/result.txt", report.ToString());
            if (SessionState.GetBool(StartedPlay, false)) { SessionState.EraseBool(StartedPlay); EditorApplication.ExitPlaymode(); }
        }
    }
    private static GameObject New(Scene scene, string name, Vector2 position) { var result = new GameObject(name); SceneManager.MoveGameObjectToScene(result, scene); result.transform.position = position; return result; }
    private static object Get(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    private static object Call(object target, string name) => target.GetType().GetMethod(name, Private).Invoke(target, null);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif