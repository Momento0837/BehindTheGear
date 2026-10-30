using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Linq;
using BehindTheGear.HideOut;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Explicitly invoked; no auto-run, saved scenes or production saves are modified.
public static class HideOutPlayChecks
{
    private static readonly StringBuilder Report = new();
    private static string output;
    private static int checks;
    private static bool running;
    private static int runtimeErrors, eventSystemWarnings;
    private static void ObserveLog(string text, string trace, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error) { runtimeErrors++; Report.AppendLine("RUNTIME " + text); }
        if (type == LogType.Warning && text.IndexOf("EventSystem", StringComparison.OrdinalIgnoreCase) >= 0) eventSystemWarnings++;
    }

    [MenuItem("Tools/HideOut/Run isolated play checks %#F9")]
    public static void Run()
    {
        if (!Application.isPlaying || running) { Debug.LogWarning("Enter Play mode before running HideOut checks."); return; }
        if (Application.unityVersion != "6000.3.23f1") throw new InvalidOperationException("Required Unity: 6000.3.23f1");
        running = true; checks = 0; Report.Clear();
        runtimeErrors = eventSystemWarnings = 0; Application.logMessageReceived += ObserveLog;
        output = Path.GetFullPath("Temp/HideOutValidation/play-checks.txt"); Directory.CreateDirectory(Path.GetDirectoryName(output));
        var actor = new GameObject("HideOut Validation Player"); actor.SetActive(false);
        actor.AddComponent<PlayerStats>(); actor.AddComponent<PlayerInventory>();
        JsonUtility.FromJsonOverwrite("{\"controlsToDisable\":[]}", actor.AddComponent<PlayerControlLock>());
        actor.AddComponent<PlayerHealth>(); actor.AddComponent<PlayerMeleeAttack>();
        var bridge = actor.AddComponent<HideOutPlayerBridge>(); actor.SetActive(true);
        actor.GetComponent<PlayerInputReader>().enabled = false;
        bridge.StartCoroutine(Guard(bridge, Checks(bridge)));
    }
    private static IEnumerator Guard(HideOutPlayerBridge bridge, IEnumerator routine)
    {
        bool failed = false;
        while (true)
        {
            bool more;
            try { more = routine.MoveNext(); }
            catch (Exception e) { Report.AppendLine("FAIL " + e); failed = true; break; }
            if (!more) break;
            yield return routine.Current;
        }
        Application.logMessageReceived -= ObserveLog;
        if (runtimeErrors != 0 || eventSystemWarnings != 0) { failed = true; Report.AppendLine($"Unexpected runtime errors: {runtimeErrors}; EventSystem warnings: {eventSystemWarnings}"); }
        Report.AppendLine($"Unity {Application.unityVersion}; {checks} checks; {(failed ? "FAILED" : "PASSED")}");
        File.WriteAllText(output, Report.ToString());
        if (failed) Debug.LogError(Report.ToString()); else Debug.Log(Report.ToString());
        Object.Destroy(bridge.gameObject); running = false;
        EditorApplication.isPlaying = false;
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        checks++; Report.AppendLine("PASS " + message);
    }
    private static IEnumerator Checks(HideOutPlayerBridge b)
    {
        var settings = AssetDatabase.LoadAssetAtPath<HideOutSettings>("Assets/Scenes/HideOutSystemScene/Data/HideOutPrototype.asset");
        string save = Path.GetFullPath("Temp/HideOutValidation/check-" + Guid.NewGuid().ToString("N") + ".json");
        b.InitializeForValidation(settings, save);
        Check(b.Health.CurrentHealth == 10 && b.Inventory.Capacity == 15, "fresh real Unity components");
        b.Upgrade(Facility.Storage);
        Check(b.State.levels[3] == 0 && b.State.coins == 0 && !File.Exists(save), "insufficient resources do not save or mutate"); yield return null;
        b.UpgradeDummy();
        Check(b.State.levels[2] == 0 && b.State.dummyWeaponBonuses.Count == 0 && !File.Exists(save), "missing weapon blocks dummy upgrade without saving or spending"); yield return null;
        b.GrantTestResources();
        Check(b.State.coins == 1000 && b.Health.CurrentHealth == 5 && File.Exists(save), "isolated transactional save and test grant"); yield return null;
        b.Heal();
        Check(b.Health.CurrentHealth == 10 && b.State.clinicReadyAt > b.Now, "clinic heals and persists deadline"); yield return null;
        double deadline = b.State.clinicReadyAt;
        b.Heal(); Check(b.State.clinicReadyAt == deadline, "full HP/cooldown failure preserves deadline"); yield return null;
        b.Upgrade(Facility.Bed);
        Check(b.Health.MaxHealth == 20 && b.Health.CurrentHealth == 10, "bed increases max HP without healing"); yield return null;
        int damage = b.Melee.ResolvedDamage;
        int dummyCoins = b.State.coins;
        b.Upgrade(Facility.Dummy);
        b.UpgradeDummy();
        Check(b.Melee.ResolvedDamage == damage + 1 && b.EquippedWeaponDummyBonus == 1
            && HideOutModel.DummyBonus(b.State, settings.testWeaponId) == 1 && b.State.levels[2] == 1
            && b.State.coins == dummyCoins - 10,
            "rapid duplicate input spends once and gives the equipped weapon one persistent bonus"); yield return null;
        var dummy = b.gameObject.AddComponent<HideOutDummy>();
        Check(b.Melee.TryAttackTarget(dummy) && dummy.LastDamage == damage + 1, "real melee target receives calculated damage");
        Check(!b.Melee.TryAttackTarget(dummy) && dummy.Hits == 1, "melee cooldown prevents immediate second hit");
        b.Inventory.SetCapacityBonus("ValidationOtherGrowth", 3);
        b.Health.SetMaxHealthBonus("ValidationOtherGrowth", 7);
        yield return null;
        int storageCoins = b.State.coins;
        b.Upgrade(Facility.Storage); b.UpgradeStorage();
        Check(b.Inventory.Capacity == 20 && b.State.storage.Count == 15
            && b.State.levels[(int)Facility.Storage] == 1 && b.State.coins == storageCoins - 10,
            "rapid storage upgrade input applies one +2/+5 level and spends once"); yield return null;
        b.Upgrade(Facility.Gym); Check(b.MaxMana == 101 && b.Health.MaxHealth == 27, "gym changes MP, not HP"); yield return null;
        b.Transfer(true, 0);
        int total = HideOutModel.Count(b.State.inventory,"hideout_scrap") + HideOutModel.Count(b.State.storage,"hideout_scrap");
        Check(HideOutModel.Count(b.State.storage,"hideout_scrap") > 0, "real bag-to-storage transfer"); yield return null;
        b.Transfer(false, 0);
        Check(HideOutModel.Count(b.State.inventory,"hideout_scrap") + HideOutModel.Count(b.State.storage,"hideout_scrap") == total, "real storage-to-bag conserves quantity"); yield return null;
        b.ReceivePotions(); Check(b.State.potionsReadyAt == 0, "Selena starts locked"); yield return null;
        b.TestRecruit(); yield return null;
        b.ReceivePotions(); Check(HideOutModel.Count(b.State.inventory,"health_potion") == 2 && b.State.potionsReadyAt > b.Now, "recruitment unlocks persisted potion delivery"); yield return null;
        b.Transfer(true, 0); yield return null;
        int storedBeforeSave = HideOutModel.Count(b.State.storage, "hideout_scrap");
        Check(storedBeforeSave > 0, "storage contains an item before save/load verification");
        b.Save(); yield return null;
        for (int i=0;i<3;i++) { b.Load(); yield return null; }
        Check(b.Inventory.Capacity == 20 && b.Health.MaxHealth == 27 && b.Melee.ResolvedDamage == damage + 1 && b.MaxMana == 101, "repeated load does not compound bonuses or discard other growth");
        Check(HideOutModel.Count(b.State.storage, "hideout_scrap") == storedBeforeSave,
            "save/load preserves storage item IDs, quantities and slot-backed capacity");
        Check(b.State.clinicReadyAt == deadline && b.State.potionsReadyAt > b.Now, "load preserves both cooldowns");
        Check(File.Exists(save + ".bak"), "atomic save retains previous backup");
        var snapshot = JsonUtility.FromJson<HideOutSave>(File.ReadAllText(save));
        Check(snapshot.equipment[0] == settings.testWeaponId && snapshot.questInventory.Count == b.Inventory.QuestCapacity
            && HideOutModel.DummyBonus(snapshot, settings.testWeaponId) == 1,
            "save includes equipment, quest inventory and weapon-specific dummy bonus");
        int coins = b.State.coins, level = b.State.levels[1];
        // Replenish the bag from the storage round-trip before testing a paid upgrade.
        b.Transfer(false, 0); yield return null;
        b.Upgrade(Facility.Bed); b.Upgrade(Facility.Bed);
        Check(b.State.levels[1] == level+1 && b.State.coins == coins-20, "same-frame double command applies once"); yield return null;
        // Force an IO error on a unique temporary save, not on the player's files.
        var field = typeof(HideOutPlayerBridge).GetField("validationSavePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        string badPath = Path.GetFullPath("Temp/HideOutValidation/occupied-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(badPath);
        field.SetValue(b, badPath);
        coins = b.State.coins; level = b.State.levels[1]; int hp = b.Health.CurrentHealth;
        b.Upgrade(Facility.Bed);
        Check(b.State.coins == coins && b.State.levels[1] == level && b.Health.CurrentHealth == hp, "save IO failure leaves live costs/levels/HP unchanged");
        field.SetValue(b, save);
        var appearance = b.gameObject.AddComponent<HideOutAppearance>();
        var renderer = b.gameObject.AddComponent<SpriteRenderer>();
        int animations = 0; appearance.onUpgrade.AddListener(() => animations++);
        appearance.Refresh(0,renderer,settings.appearanceSprites,false);
        appearance.Refresh(2,renderer,settings.appearanceSprites,true);
        Check(renderer.sprite==settings.appearanceSprites[1] && animations==1, "level 2 appearance and explicit upgrade effect");
        appearance.Refresh(4,renderer,settings.appearanceSprites,false);
        Check(renderer.sprite==settings.appearanceSprites[2], "level 4 appearance restored");
        appearance.Refresh(6,renderer,settings.appearanceSprites,false);
        appearance.Refresh(10,renderer,settings.appearanceSprites,false);
        Check(renderer.sprite==settings.appearanceSprites[3] && animations==1, "level 6+ keeps final appearance without replay on load");
        foreach (var component in Object.FindObjectsByType<HideOutScene>(FindObjectsSortMode.None))
            Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(component.gameObject) == 0, "HideOut root has no missing scripts");

        // Unsaved runtime origin: exercises the real prefab and real HideOut asset without editing teammate scenes.
        yield return null;
        var startupPlayer = Object.FindFirstObjectByType<PersistentPlayer2D>();
        if (startupPlayer != null)
        {
            // Normal Play mode starts in Forest and creates its own persistent player.
            // Remove only that runtime copy so the isolated actor can own persistence.
            Object.Destroy(startupPlayer.gameObject);
            yield return null;
        }
        b.gameObject.AddComponent<PersistentPlayer2D>();
        var body = b.gameObject.AddComponent<Rigidbody2D>(); body.gravityScale = 0;
        var input = b.GetComponent<PlayerInputReader>(); input.enabled = true;
        // ProjectStartupScene routes normal Play mode through Forest. Capture and
        // unload whichever runtime scene actually started so this check remains
        // isolated without changing the project's shared startup-scene policy.
        Scene original = SceneManager.GetActiveScene();
        Scene origin = SceneManager.CreateScene("HideOut Validation Origin"); SceneManager.SetActiveScene(origin);
        var sentinel = new GameObject("Origin state retained"); var inactive = new GameObject("Origin inactive retained"); inactive.SetActive(false);
        var sourceEvents = new GameObject("Origin EventSystem"); sourceEvents.SetActive(false);
        sourceEvents.AddComponent<EventSystem>(); sourceEvents.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Scenes/HideOutSystemScene/Prefabs/HideOutEntrance.prefab");
        var entranceObject = Object.Instantiate(prefab); entranceObject.transform.position = new Vector3(13,7,0);
        var entrance = entranceObject.GetComponent<HideOutEntrance>();
        Check(entrance.settings == settings && entrance.GetComponent<BoxCollider2D>().isTrigger, "entrance prefab references and trigger");
        var unload = SceneManager.UnloadSceneAsync(original); while (!unload.isDone) yield return null;
        sourceEvents.SetActive(true);
        // The last save-failure probe and the entrance both use the same
        // same-frame command guard; advance once before beginning travel.
        yield return null;
        int hpBeforeTravel=b.Health.CurrentHealth, bagBeforeTravel=b.Inventory.Capacity;
        string weaponBeforeTravel=b.State.equipment[0];
        entrance.GetComponent<Interactable2D>().Interact(b.gameObject);
        float timeout=Time.realtimeSinceStartup+15;
        while (Object.FindFirstObjectByType<HideOutScene>() == null && Time.realtimeSinceStartup<timeout) yield return null;
        yield return null; yield return null;
        var hideout=SceneManager.GetSceneByPath(settings.scenePath);
        Check(hideout.isLoaded && HideOutTravel.CanReturn && !sentinel.activeSelf && !inactive.activeSelf, "interactable entrance loads actual HideOut and suspends origin");
        Check(b.Health.CurrentHealth==hpBeforeTravel && b.Inventory.Capacity==bagBeforeTravel && b.State.equipment[0]==weaponBeforeTravel, "entry preserves HP inventory and weapon");
        Check(!body.simulated && !input.enabled, "HideOut freezes existing player controls and physics");
        Check(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length==1, "HideOut owns exactly one active EventSystem");
        var cameraController = Object.FindFirstObjectByType<HideOutCameraController>();
        Check(cameraController != null && cameraController.GetComponent<Camera>().orthographic && cameraController.IsOverview
            && cameraController.State == HideOutCameraState.Overview
            && Quaternion.Angle(cameraController.transform.rotation, Quaternion.Euler(settings.topView.cameraEuler)) < .01f,
            "isometric view starts at the configured orthographic overview");
        bool hasScreenHud = false;
        foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            hasScreenHud |= canvas.renderMode == RenderMode.ScreenSpaceOverlay;
        var diorama = Object.FindFirstObjectByType<HideOutIsometricEnvironment>();
        Check(diorama != null && diorama.GetComponentsInChildren<MeshRenderer>(true).Length > 30 && hasScreenHud,
            "3D isometric diorama keeps facility panels in screen space");
        Check(GameObject.Find("Previous Room") == null && GameObject.Find("Next Room") == null,
            "legacy room navigation buttons are absent");
        var pan = typeof(HideOutCameraController).GetMethod("ApplyPan",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Vector3 panStart = cameraController.transform.position;
        Vector2 smallDrag = new Vector2(20f, 0f) * settings.topView.leftDragMultiplier;
        pan.Invoke(cameraController, new object[] { smallDrag, 1f / 60f });
        float panDistance = Vector3.Distance(panStart, cameraController.transform.position);
        Check(panDistance > .001f && panDistance < .5f
            && cameraController.State == HideOutCameraState.FreeExplore,
            "gentle pan moves a small distance from the overview without zooming");
        for (int i = 0; i < 120; i++) pan.Invoke(cameraController, new object[] { smallDrag, 1f / 60f });
        Vector3 boundary = cameraController.transform.position;
        pan.Invoke(cameraController, new object[] { smallDrag, 1f / 60f });
        Check(Vector3.Distance(boundary, cameraController.transform.position) < .001f,
            "repeated pan stops at the boundary without overscroll");
        pan.Invoke(cameraController, new object[] { -smallDrag, 1f / 60f });
        Check(Vector3.Distance(boundary, cameraController.transform.position) > .001f,
            "reverse pan responds immediately at the boundary");
        cameraController.SnapToOverview();
        var warehouseAnchor = GameObject.Find("WarehouseCameraAnchor");
        var storageFocus = settings.topView.Find(Facility.Storage);
        bool focusCompleted = false;
        Check(warehouseAnchor != null && storageFocus != null
            && cameraController.TryFocus(warehouseAnchor.transform, storageFocus.orthographicSize, () => focusCompleted = true),
            "storage focus accepts one camera move");
        timeout = Time.realtimeSinceStartup + 3f;
        while (cameraController.IsMoving && Time.realtimeSinceStartup < timeout) yield return null;
        Check(focusCompleted && !cameraController.IsOverview && cameraController.State == HideOutCameraState.FacilityFocus
            && Mathf.Abs(cameraController.GetComponent<Camera>().orthographicSize - storageFocus.orthographicSize) < .001f,
            "storage focus completes at its configured zoom");
        bool overviewCompleted = false;
        Check(cameraController.TryReturnToOverview(() => overviewCompleted = true), "overview return accepts one camera move");
        timeout = Time.realtimeSinceStartup + 3f;
        while (cameraController.IsMoving && Time.realtimeSinceStartup < timeout) yield return null;
        Check(overviewCompleted && cameraController.IsOverview && cameraController.State == HideOutCameraState.Overview
            && Mathf.Abs(cameraController.GetComponent<Camera>().orthographicSize - settings.topView.overviewOrthographicSize) < .001f,
            "camera returns to the configured overview zoom");
        var view = Object.FindFirstObjectByType<HideOutScene>();
        string beforeClicks = JsonUtility.ToJson(b.State);
        foreach (Facility facility in settings.topView.shortcutOrder)
        {
            var label = settings.FindPresentation(facility).displayName;
            foreach (string buttonName in new[] { label + " Facility Icon", "Shortcut " + facility })
            {
                var button = view.GetComponentsInChildren<Button>(true).First(x => x.name == buttonName);
                Check(button.gameObject.activeInHierarchy && button.interactable,
                    buttonName + " accepts pointer input (active=" + button.gameObject.activeInHierarchy + ", interactable=" + button.interactable + ", camera=" + cameraController.State + ", moving=" + cameraController.IsMoving + ")");
                ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
                timeout = Time.realtimeSinceStartup + 3f;
                while (cameraController.IsMoving && Time.realtimeSinceStartup < timeout) yield return null;
                yield return null;
                Check(cameraController.State == HideOutCameraState.FacilityFocus && !cameraController.IsMoving, buttonName + " focuses camera");
                string panelName = facility == Facility.Storage ? "Storage Panel" : facility == Facility.Dummy ? "Dummy Bottom Panel"
                    : HideOutModel.IsExhibition(facility) ? "Exhibition Information Panel" : "Upgrade Overlay";
                Check(view.GetComponentsInChildren<RectTransform>(true).First(t => t.name == panelName).gameObject.activeInHierarchy,
                    buttonName + " opens its facility panel after movement");
                Check(button.interactable, buttonName + " remains clickable when focused");
                ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
                timeout = Time.realtimeSinceStartup + 3f;
                while (cameraController.IsMoving && Time.realtimeSinceStartup < timeout) yield return null;
                yield return null;
                Check(cameraController.IsOverview, buttonName + " second click returns to overview");
            }
        }
        Check(JsonUtility.ToJson(b.State) == beforeClicks, "all facility click toggles preserve progress and resources");
        Check(diorama.UpgradeFeedbackCount == 0, "entry and selection never replay upgrade feedback");
        int[] visualLevels = (int[])b.State.levels.Clone();
        foreach (int stageLevel in new[] { 0, 2, 4, 6, 9 })
        {
            foreach (Facility facility in settings.topView.shortcutOrder) if (!HideOutModel.IsExhibition(facility)) visualLevels[(int)facility] = stageLevel;
            diorama.RefreshFacilityVisuals(visualLevels); diorama.ApplyArt(settings, visualLevels);
            Check(diorama.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Visible Stage Frame " + HideOutModel.Tier(stageLevel) && t.gameObject.activeInHierarchy) == 6,
                "all six facilities restore visible stage at level " + stageLevel);
            yield return null;
            ScreenCapture.CaptureScreenshot(Path.GetFullPath("Temp/HideOutValidation/stage-" + stageLevel + ".png"));
            yield return null; yield return null;
        }
        diorama.RefreshFacilityVisuals(b.State.levels); diorama.ApplyArt(settings, b.State.levels);
        yield return null;
        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Temp/HideOutValidation/hud-overview.png"));
        int feedbackBefore = diorama.UpgradeFeedbackCount;
        b.Upgrade(Facility.Clinic); yield return null;
        Check(diorama.UpgradeFeedbackCount == feedbackBefore + 1, "real paid upgrade plays one environment feedback");
        b.Load(); yield return null;
        Check(diorama.UpgradeFeedbackCount == feedbackBefore + 1, "save reload restores art without replaying feedback");
        var exit=HideOutTravel.Exit(b,hideout); while(exit.MoveNext()) yield return exit.Current;
        Check(!hideout.isLoaded && sentinel.activeSelf && !inactive.activeSelf && sourceEvents.activeSelf, "exit restores exact origin active states");
        Check((b.transform.position-new Vector3(13,7,0)).sqrMagnitude<.0001f && body.simulated && input.enabled, "exit restores configured position controls and physics");
        Check(b.Health.CurrentHealth==hpBeforeTravel && b.Inventory.Capacity==bagBeforeTravel && b.State.equipment[0]==weaponBeforeTravel, "exit retains player progression");
        yield return null;
        entrance.GetComponent<Interactable2D>().Interact(b.gameObject);
        timeout=Time.realtimeSinceStartup+15;
        while (Object.FindFirstObjectByType<HideOutScene>()==null && Time.realtimeSinceStartup<timeout) yield return null;
        yield return null; yield return null;
        Check(b.Inventory.Capacity==bagBeforeTravel && b.Health.MaxHealth==37, "second visit does not duplicate permanent bonuses");
        exit=HideOutTravel.Exit(b,SceneManager.GetSceneByPath(settings.scenePath)); while(exit.MoveNext()) yield return exit.Current;
        // Guard exits Play mode, which restores the original editor scene without saving these objects.
    }
}
