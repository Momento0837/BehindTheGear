using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BehindTheGear.HideOut;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Explicit isolated Play review. Editor scenes are restored by exiting Play; no production saves.
[InitializeOnLoad]
public static class ScareCrowTrainingChecks
{
    private const string Output = "Temp/ScareCrowValidation/";
    private const string Armed = "BTG.ScareCrow.Checks";
    private static readonly StringBuilder Report = new();
    private static bool running, oldBackground;
    private static int errors, checks;
    private static double deadline;
    private static GameObject keyboardObject;
    static ScareCrowTrainingChecks()
    {
        const string refreshRequest = "Temp/HideOutValidation/request-ui-checks.flag";
        if (File.Exists(Output + "checks.flag") && File.Exists(refreshRequest)) File.Delete(refreshRequest);
        EditorApplication.update += Tick;
    }
    [MenuItem("Tools/HideOut/Verify ScareCrow training room")]
    public static void Run()
    {
        if (Application.unityVersion != "6000.3.23f1" || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run in edit mode with Unity 6000.3.23f1.");
        Directory.CreateDirectory(Output);
        oldBackground = Application.runInBackground; Application.runInBackground = true;
        SessionState.SetBool(Armed, true); deadline = EditorApplication.timeSinceStartup + 120;
        EditorApplication.isPlaying = true;
    }
    private static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        const string visibilityRequest = "Temp/HideOutVisibilityValidation/request.flag";
        if (File.Exists(visibilityRequest) && Application.isPlaying && !SessionState.GetBool(Armed, false))
        { File.Delete(visibilityRequest); VerifyBasePlayerVisibility(); }
        if (File.Exists(Output + "checks.flag") && !EditorApplication.isPlayingOrWillChangePlaymode)
        { File.Delete(Output + "checks.flag"); Run(); }
        if (!SessionState.GetBool(Armed, false)) return;
        EditorApplication.isPaused = false; Application.runInBackground = true; EditorApplication.QueuePlayerLoopUpdate();
        if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 120;
        if (EditorApplication.timeSinceStartup > deadline) { Finish(new Exception("Play verification timeout")); return; }
        if (!EditorApplication.isPlaying || running) return;
        running = true; checks = errors = 0; Report.Clear(); Application.logMessageReceived += Observe;
        var host = new GameObject("Isolated training check runner"); Object.DontDestroyOnLoad(host);
        host.AddComponent<HideOutPlayerBridge>().StartCoroutine(Guard(Verify()));
    }
    private static void Observe(string message, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception)
        { errors++; Report.AppendLine("RUNTIME " + message); }
    }
    private static IEnumerator Guard(IEnumerator routine)
    {
        while (true)
        {
            bool more;
            try { more = routine.MoveNext(); }
            catch (Exception e) { Finish(e); yield break; }
            if (!more) break;
            yield return routine.Current;
        }
        Finish(null);
    }
    private static void Finish(Exception failure)
    {
        Application.logMessageReceived -= Observe;
        if (failure != null) Report.AppendLine("FAIL " + failure);
        Report.AppendLine($"Unity {Application.unityVersion}; {checks} checks; {errors} runtime errors; {(failure == null && errors == 0 ? "PASSED" : "FAILED")}");
        Directory.CreateDirectory(Output); File.WriteAllText(Output + "play-checks.txt", Report.ToString());
        SessionState.SetBool(Armed, false); running = false;
        if (keyboardObject != null) Object.DestroyImmediate(keyboardObject);
        Application.runInBackground = oldBackground; EditorApplication.isPlaying = false;
    }
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); checks++; Report.AppendLine("PASS " + message); }
    private static void Click(Button button)
    {
        ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
    }
    private static string Progress(HideOutPlayerBridge b)
    {
        var copy = JsonUtility.FromJson<HideOutSave>(JsonUtility.ToJson(b.State)); copy.activeClock = 0;
        return JsonUtility.ToJson(copy);
    }

    [MenuItem("Tools/HideOut/Verify base player visibility during Play")]
    public static void VerifyBasePlayerVisibility()
    {
        const string folder = "Temp/HideOutVisibilityValidation/";
        if (!Application.isPlaying || Application.unityVersion != "6000.3.23f1")
            throw new InvalidOperationException("Requires Play mode in Unity 6000.3.23f1.");
        var liveView = Object.FindObjectsByType<HideOutScene>(FindObjectsSortMode.None)
            .FirstOrDefault(view => view.TrainingPlayer != null && view.gameObject.activeInHierarchy);
        if (liveView == null) throw new InvalidOperationException("Open the hideout base before running visibility checks.");
        Directory.CreateDirectory(folder); Report.Clear(); checks = errors = 0;
        GameObject actor = null, host = null;
        Exception failure = null;
        Application.logMessageReceived += Observe;
        try
        {
            var live = liveView.TrainingPlayer;
            string liveData = Progress(live);
            Vector3 livePosition = live.transform.position;
            liveView.SuspendPlayer();
            Check(live.gameObject.activeInHierarchy && live.isActiveAndEnabled && live.Inventory.isActiveAndEnabled,
                "live save rig and inventory remain active");
            Check(live.GetComponentsInChildren<SpriteRenderer>(true).All(r => r.forceRenderingOff),
                "all live player sprites are excluded from rendering in the current base");

            actor = new GameObject("Isolated visibility check player"); actor.SetActive(false);
            SceneManager.MoveGameObjectToScene(actor, liveView.gameObject.scene);
            actor.transform.position = new Vector3(10000, 10000, 0);
            var body = actor.AddComponent<Rigidbody2D>(); body.gravityScale = 0;
            var sprite = actor.AddComponent<SpriteRenderer>();
            var child = new GameObject("Previously hidden child"); child.transform.SetParent(actor.transform, false);
            var hidden = child.AddComponent<SpriteRenderer>(); hidden.enabled = false; hidden.forceRenderingOff = true;
            actor.AddComponent<PlayerStats>(); actor.AddComponent<PlayerInventory>();
            JsonUtility.FromJsonOverwrite("{\"controlsToDisable\":[]}", actor.AddComponent<PlayerControlLock>());
            actor.AddComponent<PlayerHealth>();
            var input = actor.AddComponent<PlayerInputReader>();
            var bridge = actor.AddComponent<HideOutPlayerBridge>(); actor.SetActive(true);
            string isolatedPath = Path.GetFullPath(folder + "unused-test-save.json");
            bridge.InitializeForValidation(live.Settings, isolatedPath);
            string before = Progress(bridge);
            Vector3 position = actor.transform.position;
            host = new GameObject("Isolated visibility check view"); host.SetActive(false);
            SceneManager.MoveGameObjectToScene(host, liveView.gameObject.scene);
            var view = host.AddComponent<HideOutScene>(); view.enabled = false;
            typeof(HideOutScene).GetField("player", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(view, bridge);
            host.SetActive(true);
            view.SuspendPlayer(); view.SuspendPlayer();
            Check(sprite.forceRenderingOff && hidden.forceRenderingOff && sprite.enabled && !hidden.enabled,
                "suspension hides root and child sprites without changing their enabled states");
            Check(!body.simulated && !input.enabled && Progress(bridge) == before && actor.transform.position == position,
                "repeated suspension preserves player position and data while keeping field controls frozen");
            view.ReleasePlayer();
            Check(!sprite.forceRenderingOff && hidden.forceRenderingOff && body.simulated && input.enabled,
                "release restores original visibility, physics and input including an already hidden child");
            view.SuspendPlayer();
            Check(sprite.forceRenderingOff, "returning to the base hides the same rig again");
            Object.DestroyImmediate(host); host = null;
            Check(!sprite.forceRenderingOff && hidden.forceRenderingOff && body.simulated && input.enabled,
                "destroying or unloading the view restores the previous rendering state");
            Check(Progress(bridge) == before && !File.Exists(isolatedPath),
                "visibility transitions neither reset inventory/progression nor write a save file");
            Check(Progress(live) == liveData && live.transform.position == livePosition
                && live.GetComponentsInChildren<SpriteRenderer>(true).All(r => r.forceRenderingOff),
                "existing live gameplay data and position are unchanged and the base player remains hidden");
        }
        catch (Exception error) { failure = error; }
        finally
        {
            if (host != null) Object.DestroyImmediate(host);
            if (actor != null) Object.DestroyImmediate(actor);
            Application.logMessageReceived -= Observe;
            if (failure != null) Report.AppendLine("FAIL " + failure);
            Report.AppendLine($"Unity {Application.unityVersion}; {checks} checks; {errors} runtime errors; {(failure == null && errors == 0 ? "PASSED" : "FAILED")}");
            File.WriteAllText(folder + "result.txt", Report.ToString());
        }
    }
    private static int VisibleNumbers(ScareCrowTrainingScene training) => training.trainingCanvas.GetComponentsInChildren<TMP_Text>()
        .Count(x => x.name == "Pooled Damage Number" && x.gameObject.activeSelf);

    private static IEnumerator Verify()
    {
        yield return null;
        foreach (var persistent in Object.FindObjectsByType<PersistentPlayer2D>(FindObjectsSortMode.None))
            Object.Destroy(persistent.transform.root.gameObject);
        yield return null;
        yield return SceneManager.LoadSceneAsync("HideOutSystemScene");
        yield return null; yield return null;
        var view = Object.FindFirstObjectByType<HideOutScene>();
        var player = view.TrainingPlayer;
        var body = player.GetComponent<Rigidbody2D>();
        var movement = player.GetComponent<PlayerMovement2D>();
        var input = player.GetComponent<PlayerInputReader>();
        Check(movement != null && body != null && player.Melee != null && player.GetComponent<Animator>() != null, "direct base Play uses the existing complete SavePlayerRig");
        Check(player.gameObject.activeInHierarchy && player.isActiveAndEnabled && player.Inventory.isActiveAndEnabled
            && player.GetComponentsInChildren<SpriteRenderer>(true).All(r => r.forceRenderingOff),
            "base hides every player sprite while retaining the active save bridge and inventory data");
        view.SuspendPlayer(); // Repeated suspension must preserve the original visible/physics state.
        Check(player.EquippedWeapon == null && player.State.coins == 0, "entry does not grant equipment or test resources");
        player.Change(s => { s.equipment[0] = view.settings.testWeaponId; s.coins = 500; s.actionPoints = 100;
            HideOutModel.Add(s.inventory, "hideout_scrap", 50, view.settings); s.mana = 67; return "isolated fixture"; });
        yield return null;
        player.UpgradeDummy(); yield return null;
        player.Health.TakeDamage(12); yield return null;
        int hp = player.Health.CurrentHealth, mana = player.State.mana;
        int playerId = player.GetInstanceID();
        string progress = Progress(player);
        string stats = JsonUtility.ToJson(player.Inventory.Stats);
        Vector3 basePosition = player.transform.position;
        var world = view.GetComponentsInChildren<Button>(true).Single(x => x.name == "허수아비 Facility Icon");
        var shortcut = view.GetComponentsInChildren<Button>(true).Single(x => x.name == "Shortcut Dummy");
        var baseCamera = Object.FindFirstObjectByType<HideOutCameraController>();
        float overviewSize = baseCamera.GetComponent<Camera>().orthographicSize;
        Click(world); Click(world); Click(shortcut);
        Check(baseCamera.IsMoving && !SceneFadeTransition.IsRunning && !ScareCrowTrainingScene.IsActive,
            "world click starts the existing facility camera move before fading or loading training");
        float until = Time.realtimeSinceStartup + 12;
        while (baseCamera.IsMoving && Time.realtimeSinceStartup < until) yield return null;
        Check(baseCamera.GetComponent<Camera>().orthographicSize < overviewSize
            && Mathf.Abs(baseCamera.GetComponent<Camera>().orthographicSize - view.settings.topView.Find(Facility.Dummy).orthographicSize) < .01f,
            "camera reaches the authored dummy facility zoom before the scene transition");
        ScreenCapture.CaptureScreenshot(Path.GetFullPath(Output + "entry-camera-zoom.png"));
        bool sawDarkening = false;
        while (!ScareCrowTrainingScene.IsActive && Time.realtimeSinceStartup < until) yield return null;
        var training = Object.FindFirstObjectByType<ScareCrowTrainingScene>();
        var fade = Object.FindFirstObjectByType<SceneFadeTransition>().GetComponentInChildren<Image>(true);
        Check(SceneFadeTransition.IsRunning && fade.color.a > .99f, "new training camera and player bind while the fade is fully black");
        ScreenCapture.CaptureScreenshot(Path.GetFullPath(Output + "entry-black.png"));
        while (SceneFadeTransition.IsRunning && Time.realtimeSinceStartup < until)
        {
            sawDarkening |= fade.color.a > .01f && fade.color.a < .99f;
            yield return null;
        }
        Check(sawDarkening && !SceneFadeTransition.IsRunning && fade.color.a == 0,
            "training is revealed gradually and the existing fade clears completely");
        Check(training != null && training.Player.GetInstanceID() == playerId, "world icon opens the existing ScareCrowScene with the same live player");
        Check(Enumerable.Range(0, SceneManager.sceneCount).Count(i => SceneManager.GetSceneAt(i).path == ScareCrowTrainingScene.ScenePath) == 1, "rapid world and shortcut clicks load the training scene once");
        Check(Object.FindObjectsByType<PersistentPlayer2D>(FindObjectsSortMode.None).Length == 1, "training has one persistent player");
        Check(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length == 1, "training has one active input context");
        Check(player.Health.CurrentHealth == hp && player.State.mana == mana && Progress(player) == progress && JsonUtility.ToJson(player.Inventory.Stats) == stats, "travel preserves HP MP growth equipment inventory currency and per-weapon upgrade data");
        Check(body.simulated && movement.enabled && input.enabled, "training restores the original movement attack input and physics");
        Check(!player.GetComponent<SpriteRenderer>().forceRenderingOff,
            "training restores player rendering even after repeated base suspension");
        Check(training.GetComponentsInChildren<HideOutDummy>().Length == 1 && training.GetComponentsInChildren<MonsterFollowController>().Length == 0, "training contains one dummy and no reward-bearing monsters");
        Check(training.gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .All(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0), "serialized training scene contains no missing scripts");
        Check(training.GetComponentsInChildren<SpriteRenderer>(true).All(r => r.sprite != null && r.sharedMaterial != null)
            && training.playerStart != null && training.trainingCamera != null && training.settings.font != null,
            "authored map sprites materials player marker camera and Korean font references resolve");
        Check(training.weaponLabel.text.Contains(player.EquippedWeapon.DisplayName), "HUD uses the real equipped weapon name");
        yield return new WaitForSeconds(.5f);
        keyboardObject = new GameObject("Training verification keyboard", typeof(PlayReviewKeyboard));
        Key right = PlayerControlSchemeSettings.Current == PlayerControlScheme.WasdMouseQe ? Key.D : Key.RightArrow;
        float xBefore = player.transform.position.x;
        PlayReviewKeyboard.Held = new[] { right }; yield return new WaitForSeconds(.22f); PlayReviewKeyboard.Held = Array.Empty<Key>();
        Check(player.transform.position.x > xBefore + .2f, "real configured movement input moves the existing player on the authored floor");
        yield return new WaitForSeconds(.1f);
        // Place inside melee range. Invoke the same animation input handler used by PlayerInputReader.
        player.transform.position = new Vector3(.7f, -.91f, 0); body.linearVelocity = Vector2.zero; Physics2D.SyncTransforms();
        var target = training.dummy;
        var feedback = target.GetComponent<ScareCrowHitFeedback>();
        // A second child collider proves per-swing receiver deduplication.
        var extra = new GameObject("Isolated duplicate hitbox"); extra.transform.SetParent(target.transform, false);
        extra.layer = target.gameObject.layer; extra.transform.localPosition = new Vector3(0, 1.4f, 0);
        extra.AddComponent<BoxCollider2D>().size = new Vector2(.8f, 2.2f); Physics2D.SyncTransforms();
        int hits = target.Hits;
        typeof(PlayerMovement2D).GetMethod("HandleAttackPressed", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(movement, null);
        until = Time.realtimeSinceStartup + 3;
        while (target.Hits == hits && Time.realtimeSinceStartup < until) yield return null;
        Check(target.Hits == hits + 1 && target.LastDamage == player.Melee.ResolvedDamage && !target.LastHit.IsCritical, "actual animated melee resolves final equipped damage once across overlapping colliders without fabricating criticals");
        Check(VisibleNumbers(training) == 1, "one final hit produces one pooled damage number");
        Check(training.lastHitLabel.text.Contains(target.LastDamage.ToString()), "last-hit HUD reads final damage");
        yield return new WaitForSeconds(1);
        var skills = player.GetComponent<PlayerSkills>();
        int equippedDamage = player.Melee.ResolvedDamage;
        Check(skills.TryUseStealth(), "Existing training player can prepare a stealth attack");
        hits = target.Hits;
        typeof(PlayerMovement2D).GetMethod("HandleAttackPressed", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(movement, null);
        until = Time.realtimeSinceStartup + 3;
        while (target.Hits == hits && Time.realtimeSinceStartup < until) yield return null;
        Check(target.LastDamage == equippedDamage + 10 && !skills.IsStealthed && !target.LastHit.IsCritical,
            "Real equipped animation delivers the one-swing stealth bonus as final damage without fabricating a critical");
        mana = player.State.mana; progress = Progress(player);
        yield return new WaitForSeconds(1);
        // Every subsequent authored swing can hit again; no per-target lifetime lock.
        hits = target.Hits;
        for (int i = 0; i < 3; i++) { player.Melee.BeginComboSwing(); player.Melee.ResolveComboHit(i + 1); }
        Check(target.Hits == hits + 3, "existing successive swing policy remains one hit per swing");
        Object.Destroy(extra);
        yield return new WaitForSeconds(1.1f);
        int finalDamage = player.Melee.ResolvedDamage;
        feedback.strongDamageThreshold = 1;
        target.ReceiveHit(new CombatHit(finalDamage));
        Check(target.LastDamage == finalDamage && VisibleNumbers(training) == 1, "strong presentation changes no damage and creates one number");
        yield return new WaitForSeconds(1.1f);
        target.ReceiveHit(new CombatHit(123456, true));
        Check(target.LastDamage == 123456 && target.LastHit.IsCritical && VisibleNumbers(training) == 1, "explicit final critical metadata combines with strong style in one unclamped number (integration seam test)");
        Check(training.trainingCanvas.GetComponentsInChildren<TMP_Text>().Single(t => t.name == "Pooled Damage Number" && t.gameObject.activeSelf).text.Contains("CRITICAL"), "critical display follows supplied combat metadata");
        yield return new WaitForSeconds(.35f);
        Check(feedback.visualRoot.localRotation == Quaternion.identity && feedback.visualRoot.localPosition == Vector3.zero, "visual sway returns exactly to rest without moving target or hitbox");
        Check(Progress(player) == progress && JsonUtility.ToJson(player.Inventory.Stats) == stats, "repeated dummy hits create no currency items XP or quest progress");
        ScreenCapture.CaptureScreenshot(Path.GetFullPath(Output + "training-critical.png")); yield return null;
        yield return new WaitForSeconds(1);
        player.GetComponent<PlayerAttackController>().SelectThrowSkill(PlayerAttackController.ThrowSkill.Grenade);
        var grenades = player.GetComponent<PlayerGrenadeSkill>();
        Check(grenades.Remaining == 5 && grenades.BeginAim(), "Training player has five grenades and can preview a throw");
        yield return new WaitForSeconds(.3f);
        ScreenCapture.CaptureScreenshot(Path.GetFullPath(Output + "training-grenade-aim.png")); yield return null;
        Check(grenades.TryThrow() && grenades.Remaining == 4, "Training throw spends one actual grenade");
        var projectile = Object.FindFirstObjectByType<PlayerGrenadeProjectile>();
        yield return new WaitForSeconds(.12f);
        // Hold the real projectile near the authored target to make the fuse/hit integration deterministic.
        projectile.GetComponent<Rigidbody2D>().simulated = false;
        projectile.transform.position = target.GetComponent<Collider2D>().bounds.center;
        player.transform.position = new Vector3(-2.5f, -.91f, 0); body.linearVelocity = Vector2.zero; Physics2D.SyncTransforms();
        hits = target.Hits;
        until = Time.realtimeSinceStartup + 3;
        while (projectile != null && Time.realtimeSinceStartup < until) yield return null;
        Check(target.Hits == hits + 1 && target.LastDamage == 24 && !target.LastHit.IsCritical,
            "Real grenade fuse hits the authored dummy once with 24 final damage");
        Check(VisibleNumbers(training) == 1 && training.lastHitLabel.text.Contains("24"),
            "Grenade uses the existing dummy number pool and last-hit HUD");
        ScreenCapture.CaptureScreenshot(Path.GetFullPath(Output + "training-grenade-blast.png")); yield return null;
        player.transform.position = new Vector3(.7f, -.91f, 0); Physics2D.SyncTransforms();
        yield return new WaitForSeconds(1);
        // Archived shockwave remains callable independently, without being bound to E/V.
        int skillDamage = player.GetComponent<PlayerSkills>().ShockwaveDamage;
        hits = target.Hits;
        Check(player.GetComponent<PlayerSkills>().TryUseShockwave(), "existing shockwave skill remains usable in training");
        Check(target.Hits == hits + 1 && target.LastDamage == skillDamage, "existing area attack delivers its resolved damage once to the same receiver");
        mana = player.State.mana;
        training.OpenUpgrade(); yield return null;
        Check(view.TrainingUpgradeVisible && player.GetComponent<PlayerControlLock>().IsLocked, "facility button opens the original upgrade panel with existing control lock");
        int bonus = player.EquippedWeaponDummyBonus;
        var upgrade = training.trainingCanvas.GetComponentsInChildren<Button>().Single(b => b.name == "Dummy Upgrade Button");
        Click(upgrade); yield return null;
        Check(player.EquippedWeaponDummyBonus == bonus + 1 && player.Melee.ResolvedDamage == finalDamage + 1, "original paid facility upgrade immediately changes the equipped weapon bonus");
        Check(player.State.extensions.Any(e => e.key == grenades.SaveKey && e.json.Contains("\"remaining\":4")),
            "Existing bridge capture includes the consumed grenade count through its save extension");
        PlayReviewKeyboard.Held = new[] { Key.Escape }; yield return new WaitForSeconds(.1f); PlayReviewKeyboard.Held = Array.Empty<Key>();
        Check(!view.TrainingUpgradeVisible && ScareCrowTrainingScene.IsActive && !player.GetComponent<PlayerControlLock>().IsLocked, "real ESC closes the facility panel first without leaving training");
        ScreenCapture.CaptureScreenshot(Path.GetFullPath(Output + "training-room.png")); yield return null;
        progress = Progress(player);
        yield return new WaitForSeconds(.15f);
        PlayReviewKeyboard.Held = new[] { Key.Escape }; yield return new WaitForSeconds(.1f); PlayReviewKeyboard.Held = Array.Empty<Key>();
        until = Time.realtimeSinceStartup + 10;
        while ((!view.gameObject.activeInHierarchy || body.simulated || input.enabled) && Time.realtimeSinceStartup < until) yield return null;
        yield return null;
        Check(view.gameObject.activeInHierarchy && !body.simulated && !input.enabled && player.transform.position == basePosition,
            $"real ESC restores the retained base position and frozen base control context (active={view.gameObject.activeInHierarchy}, simulated={body.simulated}, input={input.enabled}, position={player.transform.position}, expected={basePosition})");
        Check(Progress(player) == progress && player.Health.CurrentHealth == hp && player.State.mana == mana, "return retains upgrades and actual HP MP including skill expenditure");
        Check(player.GetComponentsInChildren<SpriteRenderer>(true).All(r => r.forceRenderingOff),
            "returning to base hides the retained rig again without moving or resetting its data");
        Click(shortcut);
        Check(baseCamera.IsMoving && !ScareCrowTrainingScene.IsActive, "bottom shortcut uses the same camera-first entry sequence");
        until = Time.realtimeSinceStartup + 10;
        while ((!ScareCrowTrainingScene.IsActive || SceneFadeTransition.IsRunning) && Time.realtimeSinceStartup < until) yield return null;
        training = Object.FindFirstObjectByType<ScareCrowTrainingScene>();
        Check(training != null && training.Player == player && body.simulated, "bottom shortcut re-enters with the same functioning player");
        Check(player.GetComponent<PlayerGrenadeSkill>().Remaining == 4, "Base and training scene travel never refills used grenades");
        player.Change(s => { s.equipment[0] = null; return "isolated unequip"; }); yield return null;
        Check(training.weaponLabel.text == "장착된 무기가 없습니다.", "equipment event refreshes HUD to the no-weapon notice without granting a weapon");
        var back = training.trainingCanvas.GetComponentsInChildren<Button>().Single(b => b.name == "기지로 돌아가기");
        Click(back); Click(back);
        until = Time.realtimeSinceStartup + 10;
        while ((!view.gameObject.activeInHierarchy || body.simulated || input.enabled) && Time.realtimeSinceStartup < until) yield return null;
        yield return null;
        Check(view.gameObject.activeInHierarchy && Object.FindObjectsByType<PersistentPlayer2D>(FindObjectsSortMode.None).Length == 1, "return button tolerates duplicate clicks and retains one player");
        // Direct training-scene Play also exposes the same panel implementation.
        yield return SceneManager.LoadSceneAsync(ScareCrowTrainingScene.ScenePath);
        yield return null; yield return null;
        training = Object.FindFirstObjectByType<ScareCrowTrainingScene>();
        Check(training != null && training.Player == player, "direct training-scene load reuses the persistent rig without resetting data");
        Check(!player.GetComponent<SpriteRenderer>().forceRenderingOff,
            "unloading the base restores rendering for a direct training-scene load");
        training.OpenUpgrade(); yield return null;
        Check(training.trainingCanvas.GetComponentsInChildren<Button>().Any(b => b.name == "Dummy Upgrade Button")
            && player.GetComponent<PlayerControlLock>().IsLocked, "direct training-scene Play hosts the original facility panel without a second base scene");
        training.CloseUpgrade();
        training.ReturnToBase();
        until = Time.realtimeSinceStartup + 12;
        while (ScareCrowTrainingScene.IsActive && Time.realtimeSinceStartup < until) yield return null;
        yield return null; yield return null;
        Check(Object.FindFirstObjectByType<HideOutScene>() != null && Object.FindObjectsByType<PersistentPlayer2D>(FindObjectsSortMode.None).Length == 1,
            "direct training return opens the registered original base with one persistent player");
        Check(player.GetComponentsInChildren<SpriteRenderer>(true).All(r => r.forceRenderingOff),
            "direct training return also hides the persistent player inside the base");
    }
}
