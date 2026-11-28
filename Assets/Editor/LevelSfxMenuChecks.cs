using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Play Mode checks for the player level / EXP and the save menu (backdrop, rounded plates,
/// account button). Nothing in the project is saved: saves go to project Temp, the play-mode start scene is restored.
///
/// Three ways to run it:
///  - menu Tools/Checks/Level · SFX · Save Menu (editor open)
///  - create the file Temp/LevelSfxMenuValidation/run.flag while the editor is open (it starts within a second)
///  - editor closed: Unity.exe -batchmode -projectPath . -executeMethod LevelSfxMenuChecks.RunBatch -logFile -
/// Results: Temp/LevelSfxMenuValidation/result.txt (last line "RESULT ...") and screenshots in the same folder
/// (screenshots only when the editor has a Game view, not in batch mode).
/// </summary>
[InitializeOnLoad]
public static class LevelSfxMenuChecks
{
    private const string Folder = "Temp/LevelSfxMenuValidation/";
    private const string Key = "BTG.LevelSfxMenuChecks";
    private const string MenuScene = "Assets/Scenes/Controls/ControlSchemeSelect.unity";
    private const string FieldScene = "PlayerMovementDemo";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly StringBuilder report = new();
    private static IEnumerator routine;
    private static double waitUntil, deadline;
    private static int passed, failed, unityErrors;

    static LevelSfxMenuChecks()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    [MenuItem("Tools/Checks/Level · SFX · Save Menu")]
    public static void Run()
    {
        if (Application.unityVersion != "6000.3.23f1") throw new InvalidOperationException("Requires Unity 6000.3.23f1.");
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[LevelSfxMenuChecks] Stop Play Mode first."); return; }
        Directory.CreateDirectory(Folder);
        foreach (var old in Directory.GetFiles(Folder, "*.png")) File.Delete(old);
        File.WriteAllText(Folder + "result.txt", "RUNNING\n");
        SessionState.SetString(Key + ".startScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuScene);
        SessionState.SetBool(Key, true);
        routine = null;
        EditorApplication.isPlaying = true;
    }

    /// <summary>Entry point for -executeMethod (editor closed). Exits Unity with 0 on success, 1 on failure.</summary>
    public static void RunBatch()
    {
        SessionState.SetBool(Key + ".batch", true);
        Run();
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
            if (routine != null) Finish(); // play mode ended (normally, or the user pressed stop)
            return;
        }
        if (routine == null)
        {
            report.Clear(); passed = failed = unityErrors = 0;
            report.AppendLine("Unity " + Application.unityVersion + " · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Application.logMessageReceived -= Observe;
            Application.logMessageReceived += Observe;
            Application.runInBackground = true;
            ManualSaveStore.UseValidationDirectory(Path.GetFullPath(Folder + "slot-" + Guid.NewGuid().ToString("N")));
            deadline = EditorApplication.timeSinceStartup + 120;
            waitUntil = 0;
            routine = Steps();
        }
        if (EditorApplication.timeSinceStartup > deadline) { Fail("Timed out after 120 s"); EditorApplication.isPlaying = false; return; }
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
        if (unityErrors > 0) { failed++; report.AppendLine($"FAIL {unityErrors} Unity error(s)/exception(s) were logged during the run (listed above as UNITY)"); }
        report.AppendLine($"RESULT {(failed == 0 ? "PASS" : "FAIL")} · passed {passed} · failed {failed}");
        File.WriteAllText(Folder + "result.txt", report.ToString());
        Debug.Log("[LevelSfxMenuChecks]\n" + report);
        bool batch = SessionState.GetBool(Key + ".batch", false);
        SessionState.EraseBool(Key); SessionState.EraseBool(Key + ".batch");
        routine = null;
        if (batch || Application.isBatchMode) EditorApplication.Exit(failed == 0 ? 0 : 1);
    }

    private static void Observe(string text, string trace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        unityErrors++;
        report.AppendLine("UNITY " + type + ": " + text.Split('\n')[0]);
    }

    private static void Check(bool ok, string message)
    {
        if (ok) { passed++; report.AppendLine("PASS " + message); }
        else Fail(message);
        File.WriteAllText(Folder + "result.txt", report + "RUNNING\n");
    }

    private static void Fail(string message) { failed++; report.AppendLine("FAIL " + message); }
    private static void Info(string message) => report.AppendLine("INFO " + message);
    private static object Read(object target, string field) => target.GetType().GetField(field, Any)?.GetValue(target);

    private static IEnumerable Capture(string name)
    {
        if (Application.isBatchMode) yield break;
        ScreenCapture.CaptureScreenshot(Path.GetFullPath(Folder + name + ".png"));
        yield return .4f;
        Info("screenshot " + Folder + name + ".png" + (File.Exists(Folder + name + ".png") ? "" : " (not written)"));
    }

    private static IEnumerator Steps()
    {
        yield return 1f;
        foreach (var step in SaveMenu()) yield return step;
        foreach (var step in LevelUnit()) yield return step;
        foreach (var step in FieldPlay()) yield return step;
    }

    // ---------------- Save menu ----------------
    private static IEnumerable SaveMenu()
    {
        report.AppendLine("== Save menu");
        var menu = Object.FindFirstObjectByType<ManualSaveMenu>();
        Check(menu != null, "ManualSaveMenu exists in ControlSchemeSelect");
        if (menu == null) yield break;
        menu.SetVisible(true);
        menu.Refresh();
        yield return 1.2f;

        var panel = (GameObject)Read(menu, "panel");
        Check(panel != null && panel.activeInHierarchy, "Save menu panel is visible");
        if (panel == null) yield break;
        var panelRect = (RectTransform)panel.transform;

        var backdrop = panel.GetComponentInChildren<SaveMenuBackdrop>(true);
        Check(backdrop != null && backdrop.transform.GetSiblingIndex() == 0, "Backdrop exists and is drawn behind everything in the panel");
        foreach (var name in new[] { "SaveMenu_Back", "SaveMenu_Glass", "SaveMenu_Front", "SaveMenu_GearLarge", "SaveMenu_GearMedium", "SaveMenu_GearSmall" })
        {
            var texture = Resources.Load<Texture2D>("UI/SaveMenu/" + name);
            Check(texture != null && texture.filterMode == FilterMode.Point, $"Backdrop art {name} loads ({(texture != null ? texture.width + "x" + texture.height : "missing")}, point filter)");
        }
        if (backdrop != null)
        {
            var gears = (Image[])Read(backdrop, "gearImages");
            var before = gears.Select(g => g != null ? g.sprite : null).ToArray();
            Check(gears.Length == 3 && gears.All(g => g != null && g.sprite != null), "Three gear layers have sprites");
            yield return .8f;
            Check(gears.Where((g, i) => g != null && g.sprite != before[i]).Count() == 3, "All three gears step to new frames (turning)");
            var hands = backdrop.transform.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.name == "Clock Hands");
            Check(hands != null && hands.sprite != null, "Clock hands layer exists");
        }

        var buttons = panel.GetComponentsInChildren<Button>(true);
        var slots = new[] { "새 게임", "이어하기", "삭제" }.Select(n => buttons.FirstOrDefault(b => b.name == n)).ToArray();
        Check(slots.All(b => b != null), "Slot buttons 새 게임 / 이어하기 / 저장 삭제 exist");
        if (slots.All(b => b != null))
        {
            var plates = slots.Select(b => b.GetComponentInChildren<SaveSlotPlateGraphic>()).ToArray();
            Check(plates.All(p => p != null && Mathf.Approximately(p.CornerRadius, 30f)), "Slot plates use rounded corners (radius 30)");
            Check(plates.All(p => p.canvasRenderer.GetMesh() != null && p.canvasRenderer.GetMesh().vertexCount > 0), "Slot plates render a mesh");
            Check(slots.All(b => ((RectTransform)b.transform).sizeDelta == new Vector2(420, 72)), "Slot plates keep 420x72");
        }

        var account = buttons.FirstOrDefault(b => b.name == "Cloud Account Button");
        Check(account != null, "Account button exists");
        if (account != null)
        {
            var rect = (RectTransform)account.transform;
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            var panelCorners = new Vector3[4]; panelRect.GetWorldCorners(panelCorners);
            bool inside = corners.All(c => c.x >= panelCorners[0].x && c.x <= panelCorners[2].x && c.y >= panelCorners[0].y && c.y <= panelCorners[2].y);
            Check(inside, "Account button lies fully inside the menu panel");
            var screen = new Rect(0, 0, Screen.width, Screen.height);
            Check(corners.All(c => screen.Contains(new Vector2(c.x, c.y))), $"Account button lies fully on screen ({Screen.width}x{Screen.height})");
            Rect Bounds(RectTransform r) { var c = new Vector3[4]; r.GetWorldCorners(c); return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y); }
            var accountBounds = Bounds(rect);
            Check(slots.Where(b => b != null).All(b => !Bounds((RectTransform)b.transform).Overlaps(accountBounds)), "Account button does not overlap the slot buttons");
            var style = account.GetComponent<SaveSlotButtonStyle>();
            var plate = account.GetComponentInChildren<SaveSlotPlateGraphic>();
            Check(plate != null && plate.CornerRadius >= rect.rect.height * .5f - 4 && !plate.ShowPin, "Account button is a capsule without the slot rivet");
            var icon = account.transform.Find("Account Icon");
            Check(icon != null && icon.GetComponent<Image>().sprite != null, "Account button shows the cloud icon");
            var label = account.GetComponentInChildren<TMP_Text>();
            Info("account label: " + (label != null ? label.text : "none"));
            var top = (Color)Read(plate, "top");
            Check(top.g > top.r, "Account button uses the verdigris (green-blue) colour, unlike the copper slot buttons");
            var delete = slots[2];
            if (delete != null && delete.interactable) Check(delete.navigation.selectOnDown == account, "Keyboard: 저장 삭제 → 계정 연동하기");
        }
        foreach (var step in Capture("save-menu")) yield return step;
    }

    // ---------------- Level system on a bare object ----------------
    private static IEnumerable LevelUnit()
    {
        report.AppendLine("== Level system (unit)");
        var go = new GameObject("Level Validation Player");
        go.AddComponent<SpriteRenderer>();
        var box = go.AddComponent<BoxCollider2D>(); box.size = new Vector2(.9f, 2.15f);
        go.transform.position = new Vector3(5000, 5000, 0);
        var level = PlayerLevel.EnsureOn(go);
        yield return null;
        Check(go.GetComponent<PlayerLevelBadge>() != null, "PlayerLevel.EnsureOn also adds the badge");
        int need = level.ExperienceToNext;
        Check(level.Level == 1 && need == 20, $"Starts at Lv 1 needing {need} EXP (expected 20)");
        int levelUps = 0; level.LeveledUp += (l, ap) => levelUps++;
        level.AddExperience(need + 5);
        var wallet = go.GetComponent<PlayerFieldWallet>();
        Check(level.Level == 2 && level.Experience == 5, $"25 EXP → Lv {level.Level}, {level.Experience} EXP (expected Lv 2, 5)");
        Check(levelUps == 1 && wallet != null && wallet.PendingActionPoints == 1, "One level-up pays AP +1 through PlayerFieldWallet");
        level.AddExperience(1000);
        Check(level.Level > 3 && wallet.PendingActionPoints == level.Level - 1, $"Big EXP gives several levels at once (Lv {level.Level}, AP {wallet.PendingActionPoints})");
        string json = level.CaptureJson();
        var other = new GameObject("Level Restore Validation").AddComponent<PlayerLevel>();
        other.RestoreJson(json);
        Check(other.Level == level.Level && other.Experience == level.Experience && other.GetComponent<PlayerFieldWallet>() == null,
            $"Save round trip keeps Lv/EXP and pays no AP again ({json})");
        var save = new BehindTheGear.HideOut.HideOutSave();
        Check(PlayerLevel.LevelIn(save) == 1, "Saves made before levels existed read as Lv 1");
        Object.Destroy(other.gameObject);
        Object.Destroy(go);
        yield return null;
    }

    // ---------------- Real player and monster in PlayerMovementDemo ----------------
    private static IEnumerable FieldPlay()
    {
        report.AppendLine("== Field (" + FieldScene + ")");
        SceneManager.LoadScene(FieldScene);
        yield return 2f;
        var inventory = Object.FindFirstObjectByType<PlayerInventory>();
        Check(inventory != null, "Player with PlayerInventory exists in " + FieldScene);
        if (inventory == null) yield break;
        var owner = inventory.gameObject;
        var level = owner.GetComponent<PlayerLevel>();
        var badge = owner.GetComponent<PlayerLevelBadge>();
        Check(level != null && badge != null, "PlayerLevel and PlayerLevelBadge were added to the player automatically");
        if (level == null || badge == null) yield break;

        var badgeTransform = owner.transform.Find("Level Badge");
        var badgeRenderer = badgeTransform != null ? badgeTransform.GetComponent<SpriteRenderer>() : null;
        Check(badgeRenderer != null && badgeRenderer.sprite != null, "Badge renderer exists");
        if (badgeRenderer != null)
        {
            var sprite = badgeRenderer.sprite;
            Check(sprite.rect.width == 9 && sprite.rect.height == 9 && Mathf.Approximately(sprite.pixelsPerUnit, 11f),
                $"Badge is 9x9 px at 11 px/unit ({sprite.rect.width}x{sprite.rect.height}, ppu {sprite.pixelsPerUnit})");
            var local = badgeRenderer.transform.localPosition;
            Check(local.x < -.5f && local.y > 1.075f, $"Badge sits up and to the left of the head (local {local.x:F3}, {local.y:F3})");
            var body = owner.GetComponent<SpriteRenderer>();
            if (body != null) Check(badgeRenderer.sortingLayerID == body.sortingLayerID && badgeRenderer.sortingOrder > body.sortingOrder, "Badge draws above the player on the same sorting layer");
            Info($"badge world diameter {badgeRenderer.bounds.size.x:F3} units, player sprite height {(body != null ? body.bounds.size.y : 0):F3}");
        }
        foreach (var step in Capture("field-badge-lv1")) yield return step;

        // Kill a real monster: EXP shards should drop and be collected when they touch the player.
        var monster = Object.FindObjectsByType<MonsterFollowController>(FindObjectsSortMode.None).FirstOrDefault(m => !m.IsDevelopmentSpawn);
        Check(monster != null, "A monster exists in " + FieldScene);
        if (monster == null) yield break;
        int expBefore = level.Experience + 100000 * level.Level;
        monster.TakeDamage(100000);
        yield return .1f;
        var drops = Object.FindObjectsByType<RewardPickup>(FindObjectsSortMode.None);
        var exp = drops.Where(d => d.name == "EXP Drop").ToArray();
        Check(exp.Length > 0, $"Killing the monster dropped EXP ({exp.Length} shard(s), {drops.Length} reward pickup(s) in total)");
        yield return .6f; // pickup delay
        for (int frame = 0; frame < 20; frame++)
        {
            foreach (var drop in Object.FindObjectsByType<RewardPickup>(FindObjectsSortMode.None))
                drop.transform.position = owner.transform.position;
            yield return null;
        }
        yield return .2f;
        int expAfter = level.Experience + 100000 * level.Level;
        Check(Object.FindObjectsByType<RewardPickup>(FindObjectsSortMode.None).Length == 0, "All reward pickups were collected on touch");
        Check(expAfter > expBefore, $"Collected EXP reached the player (Lv {level.Level}, {level.Experience}/{level.ExperienceToNext})");

        level.AddExperience(level.ExperienceToNext);
        yield return 1.2f;
        Check((int)Read(badge, "shownLevel") == level.Level, $"Badge counted up to Lv {level.Level} after the ring filled");
        foreach (var step in Capture("field-badge-levelup")) yield return step;
    }
}
