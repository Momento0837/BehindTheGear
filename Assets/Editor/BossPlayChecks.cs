using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Play Mode run-through of the Forest boss (from the start screen it travels to the Forest by itself), with screenshots, so the fight can be checked without anyone playing it:
/// entrance, each pattern, the phase change, the phase-2 chase and the defeat. Nothing in the project is saved
/// (manual saves are redirected to project Temp, the play-mode start scene is restored).
///
/// Run it: menu Tools/Checks/Boss fight, or create Temp/BossValidation/run.flag while the editor is open.
/// Results: Temp/BossValidation/result.txt (last line "RESULT ...") and numbered screenshots in the same folder.
/// The player is made invincible for the run through the same private field the F7 shortcut uses.
/// </summary>
[InitializeOnLoad]
public static class BossPlayChecks
{
    private const string Folder = "Temp/BossValidation/";
    private const string Key = "BTG.BossPlayChecks";
    private const string ForestScene = "Assets/Scenes/Main/Forest.unity";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly StringBuilder report = new();
    private static IEnumerator routine;
    private static double waitUntil, deadline;
    private static int passed, failed, unityErrors, shot;

    static BossPlayChecks()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    [MenuItem("Tools/Checks/Boss fight")]
    public static void Run()
    {
        if (Application.unityVersion != "6000.3.23f1") throw new InvalidOperationException("Requires Unity 6000.3.23f1.");
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[BossPlayChecks] Stop Play Mode first."); return; }
        Directory.CreateDirectory(Folder);
        foreach (var old in Directory.GetFiles(Folder, "*.png")) File.Delete(old);
        File.WriteAllText(Folder + "result.txt", "RUNNING\n");
        SessionState.SetString(Key + ".startScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ForestScene);
        SessionState.SetBool(Key, true);
        routine = null;
        EditorApplication.isPlaying = true;
    }

    private static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (File.Exists(Folder + "run.flag") && !SessionState.GetBool(Key, false) && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(Folder + "run.flag");
            Run();
            return;
        }
        if (!SessionState.GetBool(Key, false)) return;
        if (!EditorApplication.isPlaying)
        {
            if (routine != null) Finish();
            return;
        }
        if (routine == null)
        {
            report.Clear(); passed = failed = unityErrors = shot = 0;
            report.AppendLine("Unity " + Application.unityVersion + " · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Application.logMessageReceived -= Observe;
            Application.logMessageReceived += Observe;
            Application.runInBackground = true;
            ManualSaveStore.UseValidationDirectory(Path.GetFullPath(Folder + "slot-" + Guid.NewGuid().ToString("N")));
            deadline = EditorApplication.timeSinceStartup + 420;
            waitUntil = 0;
            routine = Flatten(Steps());
        }
        if (EditorApplication.timeSinceStartup > deadline) { Fail("Timed out after 420 s"); EditorApplication.isPlaying = false; return; }
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        try
        {
            if (!routine.MoveNext()) { EditorApplication.isPlaying = false; return; }
            if (routine.Current is float seconds) waitUntil = EditorApplication.timeSinceStartup + seconds;
        }
        catch (Exception exception)
        {
            Fail("Check crashed: " + exception);
            EditorApplication.isPlaying = false;
        }
    }

    private static void Finish()
    {
        Application.logMessageReceived -= Observe;
        string start = SessionState.GetString(Key + ".startScene", "");
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(start) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(start);
        ManualSaveStore.UseValidationDirectory(null);
        try { foreach (var dir in Directory.GetDirectories(Folder, "slot-*")) Directory.Delete(dir, true); } catch (Exception) { }
        // Play Mode stopped before the run-through got anywhere (stopped by hand, or the scene never started): not a pass.
        if (passed == 0 && failed == 0) { failed++; report.AppendLine("FAIL The run ended before any check was made"); }
        if (unityErrors > 0) { failed++; report.AppendLine($"FAIL {unityErrors} Unity error(s)/exception(s) were logged during the run (listed above as UNITY)"); }
        report.AppendLine($"RESULT {(failed == 0 ? "PASS" : "FAIL")} · passed {passed} · failed {failed}");
        File.WriteAllText(Folder + "result.txt", report.ToString());
        Debug.Log("[BossPlayChecks]\n" + report);
        SessionState.EraseBool(Key);
        routine = null;
    }

    private static void Observe(string text, string trace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        unityErrors++;
        string first = trace != null ? trace.Split('\n').FirstOrDefault(l => l.Contains("Boss")) : null;
        report.AppendLine("UNITY " + type + ": " + text.Split('\n')[0] + (first != null ? "  @ " + first.Trim() : ""));
    }

    private static void Check(bool ok, string message)
    {
        if (ok) { passed++; report.AppendLine("PASS " + message); }
        else Fail(message);
        File.WriteAllText(Folder + "result.txt", report + "RUNNING\n");
    }

    private static void Fail(string message) { failed++; report.AppendLine("FAIL " + message); }
    private static void Info(string message) => report.AppendLine("INFO " + message);

    private static void Shot(string name)
    {
        if (Application.isBatchMode) return;
        string file = $"{++shot:00}-{name}.png";
        ScreenCapture.CaptureScreenshot(Path.GetFullPath(Folder + file));
        Info("screenshot " + file + "  " + Describe());
    }

    // The numbers behind a screenshot: where things are, so a picture can be checked against them.
    private static string Describe()
    {
        var boss = BossGoliath.Active;
        var player = PlayerTargetRegistry.Current;
        var view = Camera.main;
        var text = new StringBuilder();
        if (boss != null) text.Append($"boss x {boss.transform.position.x:F2} hp {boss.CurrentHealth}/{boss.MaxHealth} phase {boss.Phase} body {boss.Rig.BodyFrame} pilot {boss.Rig.PilotFrame} ");
        if (player != null) text.Append($"player x {player.position.x:F2} y {player.position.y:F2} ");
        if (view != null) text.Append($"camera x {view.transform.position.x:F2} view half-height {BossCameraDirector.VisibleHalfHeight(view):F2} ");
        text.Append($"timeScale {Time.timeScale:F2}");
        return text.ToString();
    }

    private static void Invincible(bool on)
    {
        var field = typeof(PlayerHealth).GetField("invincibilityEndTime", Any);
        foreach (var health in UnityEngine.Object.FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
            field?.SetValue(health, on ? float.MaxValue : 0f);
    }

    private static IEnumerator Steps()
    {
        // The project always enters Play Mode through the control-selection scene (ProjectStartupScene), so go to the
        // Forest the way the Ctrl+1 development shortcut does. Travel refuses while a fade is running: keep asking.
        double travel = EditorApplication.timeSinceStartup + 60;
        yield return 2f;
        while (SceneManager.GetActiveScene().path != ForestScene && EditorApplication.timeSinceStartup < travel)
        {
            DevelopmentSceneShortcuts.Travel(DevelopmentSceneShortcuts.ForestPath);
            yield return 1f;
        }
        Check(SceneManager.GetActiveScene().path == ForestScene, "Reached the Forest scene (" + SceneManager.GetActiveScene().name + ")");
        if (SceneManager.GetActiveScene().path != ForestScene) yield break;
        // The Forest plays its opening first; the fight is "on" once the story monsters are switched on.
        double ready = EditorApplication.timeSinceStartup + 150;
        while (EditorApplication.timeSinceStartup < ready && (PlayerTargetRegistry.Current == null || !StoryMonsters().Any(m => m.enabled)))
            yield return .5f;
        var player = PlayerTargetRegistry.Current;
        Check(player != null, "A player exists in Forest");
        if (player == null) yield break;
        var health = player.GetComponentInParent<PlayerHealth>() ?? player.GetComponentInChildren<PlayerHealth>();
        var view = Camera.main;
        var follow = view != null ? view.GetComponent<PlayerCameraFollow2D>() : null;
        float normalHalfHeight = BossCameraDirector.VisibleHalfHeight(view);
        Check(StoryMonsters().Any(m => m.enabled), "The Forest fight is running (story monsters are active)");
        yield return 1f;
        Shot("scene");
        yield return .3f;

        // ---- the real trigger: every monster dead -> the story flow brings the boss in
        report.AppendLine("== Entrance");
        Invincible(true);
        int killed = 0;
        foreach (var monster in UnityEngine.Object.FindObjectsByType<MonsterFollowController>(FindObjectsSortMode.None))
            if (monster.CurrentHealth > 0) { monster.TakeDamage(100000); killed++; }
        Info($"killed {killed} monster(s)");
        double appear = EditorApplication.timeSinceStartup + 12;
        while (BossGoliath.Active == null && EditorApplication.timeSinceStartup < appear) yield return .1f;
        bool byStory = BossGoliath.Active != null;
        Check(byStory, "Killing every monster makes the story flow start the boss encounter");
        if (!byStory) ForestBossEncounter.PlayStandalone(ForestBossEncounter.EntranceKind.Full);
        yield return 1.6f; Shot("entrance-footsteps");
        yield return 2.6f; Shot("entrance-walk-a");
        yield return .35f; Shot("entrance-walk-b");
        var boss = BossGoliath.Active;
        Check(boss != null, "The boss was created");
        if (boss == null) yield break;
        Check(boss.Invulnerable && !boss.Fighting, "During the entrance the boss is invulnerable and not attacking");
        double limit = EditorApplication.timeSinceStartup + 20;
        while (Mathf.Abs(boss.transform.position.x - boss.HomeX) > .05f && EditorApplication.timeSinceStartup < limit) yield return .1f;
        Shot("entrance-arrived");
        yield return 1.5f; Shot("entrance-slam");
        yield return 1.5f; Shot("entrance-title");
        while (!boss.Fighting && EditorApplication.timeSinceStartup < limit) yield return .2f;
        Check(boss.Fighting, "The fight starts after the entrance");
        Check(Mathf.Abs(boss.transform.position.x - boss.HomeX) < .1f, $"The boss stands on the arena centre (x {boss.transform.position.x:F2}, centre {boss.HomeX:F2})");
        Check(follow == null || !follow.enabled, "The follow camera is switched off while the boss camera holds the view");
        if (view != null) Check(Mathf.Abs(view.transform.position.x - boss.transform.position.x) < .6f, $"The camera is centred on the boss (camera {view.transform.position.x:F2})");
        var bounds = health != null ? health.GetComponent<PlayerMapBounds2D>() : null;
        Check(bounds != null && bounds.HasHorizontalBounds && Mathf.Abs((bounds.HorizontalBounds.y - bounds.HorizontalBounds.x) - boss.Profile.arenaHalfWidth * 2f) < .2f,
            "The player is fenced into the arena" + (bounds != null ? $" ({bounds.HorizontalBounds.x:F1} .. {bounds.HorizontalBounds.y:F1})" : ""));
        yield return .5f; Shot("fight-start");

        // ---- phase 1 patterns
        report.AppendLine("== Phase 1");
        foreach (var (pattern, waits) in new[]
        {
            (BossGoliath.Pattern.Slam, new[] { 1.0f, .45f, .5f, 1.6f }),
            (BossGoliath.Pattern.Sweep, new[] { .9f, .5f, .6f }),
            (BossGoliath.Pattern.Slag, new[] { 1.4f, .9f, .9f, 1.3f }),
        })
        {
            yield return WaitIdle(boss);
            boss.ForcePattern(pattern);
            yield return WaitBusy(boss);
            foreach (float wait in waits) { yield return wait; Shot("p1-" + pattern.ToString().ToLowerInvariant()); }
            if (pattern == BossGoliath.Pattern.Slag)
            {
                double until = EditorApplication.timeSinceStartup + 6;
                while (!boss.Overheated && EditorApplication.timeSinceStartup < until) yield return .1f;
                Check(boss.Overheated, "After the furnace shot the machine overheats");
                yield return .3f; Shot("p1-overheat");
                var weak = boss.WeakPoint.Bounds;
                float ground = boss.transform.position.y;
                Check(weak.min.y - ground <= 2.15f, $"Overheated: the weak point reaches down to {weak.min.y - ground:F2} units above the ground (a standing attack reaches 2.15)");
                int before = boss.CurrentHealth;
                boss.TakeHit(10);
                Check(boss.CurrentHealth == before - 10, "A hit on the weak point takes exactly its damage");
            }
        }
        yield return WaitIdle(boss);
        {
            var weak = boss.WeakPoint.Bounds;
            Check(weak.min.y - boss.transform.position.y >= 6f, $"Normal stance: the weak point starts {weak.min.y - boss.transform.position.y:F2} units up (a jump attack reaches about 5.97)");
        }

        // ---- phase change
        report.AppendLine("== Phase 2");
        int phaseEvents = 0;
        boss.PhaseChanged += _ => phaseEvents++;
        boss.TakeHit(boss.CurrentHealth - Mathf.FloorToInt(boss.MaxHealth * boss.Profile.phaseTwoAt));
        yield return .25f; Shot("phase-change-a");
        Check(boss.Phase == 2 && boss.Invulnerable, "At half HP phase 2 begins and the boss is invulnerable while it changes");
        int during = boss.CurrentHealth; boss.TakeHit(50);
        Check(boss.CurrentHealth == during, "Hits during the phase change are ignored");
        yield return .9f; Shot("phase-change-b");
        yield return boss.Profile.phaseChangeSeconds;
        Check(phaseEvents == 1 && !boss.Invulnerable, "The phase change happened once and the boss can be hurt again");

        // ---- chase: put the player at the far end of the fence and see whether the boss walks over
        if (health != null && bounds != null)
        {
            // Let whatever it is doing (it may already be walking towards the player) finish, then move the player.
            yield return WaitIdle(boss);
            float startX = boss.transform.position.x;
            var body = health.GetComponent<Rigidbody2D>();
            float edge = bounds.HorizontalBounds.y - 1f;
            if (body != null) body.position = new Vector2(edge, body.position.y); else health.transform.position = new Vector3(edge, health.transform.position.y, 0f);
            boss.ForcePattern(BossGoliath.Pattern.Quake);
            yield return .9f; Shot("p2-chase-walking");
            yield return 1.3f; Shot("p2-quake-telegraph");
            Check(boss.transform.position.x > startX + 1f, $"Phase 2: the tank drove towards the player ({startX:F2} -> {boss.transform.position.x:F2})");
            if (view != null)
            {
                float half = BossCameraDirector.VisibleHalfHeight(view) * view.aspect, cx = view.transform.position.x;
                Check(BossCameraDirector.VisibleHalfHeight(view) > normalHalfHeight * 1.1f, $"Phase 2: the view is wider (half-height {normalHalfHeight:F2} -> {BossCameraDirector.VisibleHalfHeight(view):F2})");
                Check(Mathf.Abs(boss.transform.position.x - cx) < half && Mathf.Abs(health.transform.position.x - cx) < half, "The camera shows both the tank and the player");
                Check(Mathf.Abs((bounds.HorizontalBounds.x + bounds.HorizontalBounds.y) * .5f - cx) < 1f, "The fence is what the camera shows");
            }
            foreach (float wait in new[] { .8f, .5f, .6f }) { yield return wait; Shot("p2-quake"); }
        }
        foreach (var pattern in new[] { BossGoliath.Pattern.Sweep, BossGoliath.Pattern.Slam })
        {
            yield return WaitIdle(boss);
            boss.ForcePattern(pattern);
            yield return WaitBusy(boss);
            foreach (float wait in new[] { 1.6f, 1.0f, 1.0f }) { yield return wait; Shot("p2-" + pattern.ToString().ToLowerInvariant()); }
        }

        // ---- defeat
        report.AppendLine("== Defeat");
        yield return WaitIdle(boss);
        int pickupsBefore = UnityEngine.Object.FindObjectsByType<RewardPickup>(FindObjectsSortMode.None).Length;
        boss.TakeHit(boss.CurrentHealth);
        Check(boss.IsDead, "The boss dies at 0 HP");
        yield return .1f; Shot("defeat-hitstop");
        yield return .6f; Shot("defeat-slowmotion");
        Check(Time.timeScale < 1f, $"Slow motion during the finish (timeScale {Time.timeScale:F2})");
        yield return 1.6f; Shot("defeat-kneel");
        yield return 1.2f; Shot("defeat-fallen");
        double end = EditorApplication.timeSinceStartup + 10;
        while (!boss.DefeatFinished && EditorApplication.timeSinceStartup < end) yield return .2f;
        Check(boss.DefeatFinished, "The defeat sequence finishes");
        Check(Mathf.Approximately(Time.timeScale, 1f), $"Time runs normally again (timeScale {Time.timeScale:F2})");
        int pickups = UnityEngine.Object.FindObjectsByType<RewardPickup>(FindObjectsSortMode.None).Length;
        Check(pickups > pickupsBefore, $"Boss rewards dropped ({pickups - pickupsBefore} pickups)");
        yield return 1.5f; Shot("after-fight");
        Check(follow == null || follow.enabled, "The follow camera is switched back on");
        if (view != null) Check(Mathf.Abs(BossCameraDirector.VisibleHalfHeight(view) - normalHalfHeight) < .05f, $"The view is back to normal (half-height {BossCameraDirector.VisibleHalfHeight(view):F2})");
        if (bounds != null) Check(!bounds.HasHorizontalBounds || bounds.HorizontalBounds.y - bounds.HorizontalBounds.x > boss.Profile.arenaHalfWidth * 2f + 1f,
            "The player's map bounds are restored" + (bounds.HasHorizontalBounds ? $" ({bounds.HorizontalBounds.x:F1} .. {bounds.HorizontalBounds.y:F1})" : " (none)"));
        Invincible(false);
        yield return .3f;
    }

    private static System.Collections.Generic.IEnumerable<MonsterFollowController> StoryMonsters() =>
        UnityEngine.Object.FindObjectsByType<MonsterFollowController>(FindObjectsSortMode.None).Where(m => !m.IsAmbientSpawn && !m.IsDevelopmentSpawn && m.CurrentHealth > 0);

    // The editor loop drives this by hand (it is not a Unity coroutine), so nested enumerators are unrolled here.
    private static IEnumerator Flatten(IEnumerator outer)
    {
        while (outer.MoveNext())
        {
            if (outer.Current is IEnumerator inner)
            {
                var nested = Flatten(inner);
                while (nested.MoveNext()) yield return nested.Current;
            }
            else yield return outer.Current;
        }
    }

    // "Idle" = between patterns: nothing is animating the fists or the body.
    private static bool Busy(BossGoliath boss)
    {
        var fists = (bool[])typeof(BossGoliath).GetField("fistControlled", Any).GetValue(boss);
        bool body = (bool)typeof(BossGoliath).GetField("bodyControlled", Any).GetValue(boss);
        bool pilot = (bool)typeof(BossGoliath).GetField("pilotControlled", Any).GetValue(boss);
        return fists[0] || fists[1] || body || pilot;
    }

    private static IEnumerator WaitIdle(BossGoliath boss)
    {
        double until = EditorApplication.timeSinceStartup + 14;
        while (boss != null && !boss.IsDead && Busy(boss) && EditorApplication.timeSinceStartup < until) yield return .1f;
    }

    private static IEnumerator WaitBusy(BossGoliath boss)
    {
        double until = EditorApplication.timeSinceStartup + 6;
        while (boss != null && !boss.IsDead && !Busy(boss) && EditorApplication.timeSinceStartup < until) yield return .05f;
    }
}
