#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BehindTheGear.HideOut;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ManualSaveChecks
{
    private const string Key = "BTG.ManualSaveChecks";
    private static IEnumerator routine;
    private static double deadline;
    private static readonly StringBuilder report = new();
    private static int checks;
    private static Mouse validationMouse;
    static ManualSaveChecks()
    {
        const string refreshRequest = "Temp/HideOutValidation/request-ui-checks.flag";
        if ((File.Exists("Temp/ManualSaveValidation/request.txt") || SessionState.GetBool(Key, false)) && File.Exists(refreshRequest))
            File.Delete(refreshRequest); // The existing watcher is used only to refresh these source edits.
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingPlayMode) ReleaseValidationMouse();
            if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Key + ".restore", false)) return;
            SessionState.SetBool(Key + ".restore", false);
            string original = SessionState.GetString(Key + ".scene", "");
            if (!string.IsNullOrEmpty(original)) EditorSceneManager.OpenScene(original);
        };
    }
    public static void RunRestart()
    {
        SessionState.SetBool(Key + ".restart", true); Run();
    }
    [MenuItem("Tools/Manual Save/Run isolated integration checks")]
    public static void Run()
    {
        if (Application.isPlaying || EditorSceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Run from a saved scene in Edit mode.");
        SessionState.SetInt(Key + ".controlScheme", (int)PlayerControlSchemeSettings.Current);
        SessionState.SetBool(Key + ".hadControlPreference", PlayerPrefs.HasKey("BehindTheGear.PlayerControlScheme"));
        routine = null;
        SessionState.SetString(Key + ".scene", EditorSceneManager.GetActiveScene().path);
        EditorSceneManager.OpenScene("Assets/Scenes/Controls/ControlSchemeSelect.unity");
        SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
    }
    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (!Application.isPlaying) return;
        if (EditorApplication.isPaused) EditorApplication.isPaused = false;
        if (routine == null)
        {
            validationMouse = InputSystem.AddDevice<Mouse>("ManualSaveValidationMouse");
            InputSystem.onBeforeUpdate += FeedValidationMouse;
            routine = SessionState.GetBool(Key + ".restart", false) ? RestartChecks() : Checks();
            deadline = EditorApplication.timeSinceStartup + 180; report.Clear(); checks = 0;
        }
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Integration check timeout");
            if (routine.MoveNext()) return;
            Finish(true, null);
        }
        catch (Exception error) { Finish(false, error); }
    }
    private static void Finish(bool success, Exception error)
    {
        if (error != null) report.AppendLine(error.ToString());
        report.AppendLine($"Unity {Application.unityVersion}: {checks} checks, {(success ? "PASSED" : "FAILED")}");
        File.WriteAllText("Temp/ManualSaveValidation/" + (SessionState.GetBool(Key + ".restart", false) ? "restart-checks.txt" : "integration-checks.txt"), report.ToString());
        SessionState.SetBool(Key, false); SessionState.SetBool(Key + ".restore", true); SessionState.SetBool(Key + ".restart", false);
        ManualSaveStore.UseValidationDirectory(null);
        ReleaseValidationMouse();
        // Menu-driven checks exercise real preferences; restore the user's selection afterward.
        PlayerControlSchemeSettings.Select((PlayerControlScheme)SessionState.GetInt(Key + ".controlScheme", 0));
        if (!SessionState.GetBool(Key + ".hadControlPreference", false))
        {
            PlayerPrefs.DeleteKey("BehindTheGear.PlayerControlScheme");
            PlayerPrefs.Save();
        }
        EditorApplication.isPlaying = false;
    }
    private static void FeedValidationMouse()
    {
        if (validationMouse == null || !validationMouse.added || InputState.currentUpdateType != InputUpdateType.Dynamic) return;
        validationMouse.MakeCurrent();
        InputSystem.QueueStateEvent(validationMouse, new MouseState { position = new Vector2(-1000, -1000) });
    }
    private static void ReleaseValidationMouse()
    {
        InputSystem.onBeforeUpdate -= FeedValidationMouse;
        if (validationMouse != null && validationMouse.added) InputSystem.RemoveDevice(validationMouse);
        validationMouse = null;
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++; report.AppendLine("PASS " + message);
        File.WriteAllText("Temp/ManualSaveValidation/progress.txt", report.ToString());
    }
    private static object Read(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    private static IEnumerator Checks()
    {
        ManualSaveStore.UseValidationDirectory(Path.GetFullPath("Temp/ManualSaveValidation/slot-" + Guid.NewGuid().ToString("N")));
        while (Object.FindFirstObjectByType<ManualSaveMenu>() == null) yield return null;
        var menu = Object.FindFirstObjectByType<ManualSaveMenu>();
        yield return null; yield return null;
        Check(!ManualSaveStore.Exists, "empty isolated slot");
        menu.NewGame(); Object.FindFirstObjectByType<ControlSchemeSelectScene>().SelectArrowKeys();
        while (SceneManager.GetActiveScene().name != "Forest" || SceneFadeTransition.IsRunning) yield return null;
        var inventory = Object.FindFirstObjectByType<PlayerInventory>();
        Check(inventory != null, "new game through production menu and control selection");
        var entrance = Object.FindFirstObjectByType<HideOutEntrance>();
        Check(entrance != null && entrance.gameObject.layer == LayerMask.NameToLayer("Interactable"), "serialized Forest entrance and interaction layer");
        entrance.GetComponent<Interactable2D>().Interact(inventory.gameObject);
        while (Object.FindFirstObjectByType<HideOutScene>() == null) yield return null;
        yield return null; yield return null;
        var bridge = inventory.GetComponent<HideOutPlayerBridge>();
        var settings = bridge.Settings;
        Check(bridge.Initialized && !bridge.Sandbox, "normal entrance opens existing hideout UI with live player");
        Check(!ManualSaveStore.Exists, "entering hideout does not save");
        inventory.Add(settings.Find("health_potion"), 3);
        inventory.Add(settings.Find("tutorial_gear"), 1);
        int gearSlot = Enumerable.Range(0, inventory.Capacity).First(i => inventory.GetSlot(i).Item == settings.Find("tutorial_gear"));
        Check(inventory.EquipFromSlot(gearSlot), "actual equipment operation");
        inventory.Stats.TryUpgrade(PersonalStatType.Attack, out _);
        int expectedCapacity = inventory.Capacity;
        bridge.Change(s => { s.coins = 200; s.actionPoints = 20; HideOutModel.Add(s.inventory, "hideout_scrap", 20, settings); return "test funding"; });
        yield return null;
        bridge.Upgrade(Facility.Storage); yield return null;
        Check(bridge.State.levels[3] == 1 && inventory.Capacity == expectedCapacity + settings.rules.inventorySlotsPerLevel, "facility upgrade updates live slots without disk write");
        QuestJournal.Accept("market_quest"); QuestJournal.Record("kill", "monster", 2);
        Check(!ManualSaveStore.Exists, "inventory, stats, facility and quest changes do not save");
        var view = Object.FindFirstObjectByType<HideOutScene>();
        // Editor updates can run before the loaded scene's Start and UI construction complete.
        while (Read(view, "player") == null || Read(view, "cameraController") == null || Read(view, "upgradeOverlay") == null) yield return null;
        Call(view, "SelectFacility", Facility.Bed);
        while (!((GameObject)Read(view, "upgradeOverlay")).activeSelf) yield return null;
        // Invoke the same bed button callback after its camera focus animation.
        for (int i = 0; i < 100; i++) yield return null;
        Call(view, "UseSelectedFacility"); yield return null;
        Check(ManualSaveStore.Exists && bridge.Message == "저장 완료", "bed interaction writes and acknowledges only successful save");
        byte[] original = File.ReadAllBytes(ManualSaveStore.SlotPath);
        var saved = ManualSaveStore.ReadSlot(); HideOutPlayerBridge.ValidateSnapshot(saved, settings);
        Check(saved.quests.Single().amounts[0] == 2 && saved.facilities.Count == 8, "saved quest 2/5 and stable facility IDs");
        inventory.Add(settings.Find("health_potion"), 4); QuestJournal.Record("kill", "monster", 2);
        bridge.Change(s => { s.coins += 99; return "unsaved change"; }); yield return null;
        Check(File.ReadAllBytes(ManualSaveStore.SlotPath).SequenceEqual(original), "unsaved changes leave last manual save byte-identical");
        SaveGameSession.ReturnToMenu();
        while (SceneManager.GetActiveScene().name != "ControlSchemeSelect" || SceneFadeTransition.IsRunning) yield return null;
        menu = Object.FindFirstObjectByType<ManualSaveMenu>(); yield return null;
        menu.NewGame(); menu.Cancel(); Check(!menu.ChoosingControls, "overwrite cancellation keeps menu and slot");
        menu.NewGame(); menu.Confirm();
        Check(File.ReadAllBytes(ManualSaveStore.SlotPath).SequenceEqual(original), "overwrite approval starts new session without replacing file");
        SaveGameSession.ReturnToMenu();
        while (SceneFadeTransition.IsRunning || Object.FindFirstObjectByType<ManualSaveMenu>() == menu) yield return null;
        menu = Object.FindFirstObjectByType<ManualSaveMenu>(); yield return null; menu.Continue();
        while (Object.FindFirstObjectByType<HideOutScene>() == null || SceneFadeTransition.IsRunning) yield return null;
        yield return null; bridge = Object.FindFirstObjectByType<HideOutPlayerBridge>(); inventory = bridge.Inventory;
        Check(SceneManager.GetActiveScene().path == settings.scenePath && !bridge.Sandbox, "Continue starts directly in hideout UI");
        Check(QuestJournal.Find("market_quest").amounts[0] == 2, "Continue restores 2/5 instead of unsaved 4/5");
        Check(bridge.State.coins == saved.coins && inventory.GetEquipped(settings.Find("tutorial_gear").Slot)?.ItemId == "tutorial_gear"
            && inventory.Capacity == expectedCapacity + settings.rules.inventorySlotsPerLevel, "coins, equipment, permanent stats and facility slots restored");
        ScreenCapture.CaptureScreenshot("Temp/ManualSaveValidation/continue-hideout.png");
        SaveGameSession.StartExpedition();
        while (SceneManager.GetActiveScene().name != "Forest" || SceneFadeTransition.IsRunning) yield return null;
        Check(inventory.GetComponent<PlayerMovement2D>().enabled, "Continue can leave hideout with gameplay controls restored");
        var monster = Object.FindFirstObjectByType<MonsterFollowController>();
        var second = Object.Instantiate(monster); var third = Object.Instantiate(monster);
        foreach (var enemy in new[] { monster, second, third }) { enemy.SetTutorialProtection(false); enemy.TakeDamage(int.MaxValue); }
        Check(QuestJournal.Find("market_quest").stage == 2, "actual monster death events advance 2/5 to ready 5/5");
        var dropped = new GameObject("Cache test crystal"); dropped.transform.position = new Vector3(-14f, 0, 0);
        dropped.AddComponent<WorldItemPickup>().Configure(settings.Find("sky_crystal"), 2);
        var fieldExit = Object.FindFirstObjectByType<ScenePortal2D>();
        fieldExit.GetComponent<Interactable2D>().Interact(inventory.gameObject);
        while (SceneManager.GetActiveScene().name != "Market" || SceneFadeTransition.IsRunning) yield return null;
        Check(RoomDropCache.Previous.drops.Any(d => d.itemId == "sky_crystal" && d.amount == 2), "production portal A to B captures uncollected drops");
        var npc = Object.FindFirstObjectByType<QuestGiver2D>();
        Check(npc != null && npc.enabled && npc.CurrentStage == QuestGiver2D.QuestStage.ReadyToComplete, "existing market NPC presents restored ready quest");
        int beforeReward = bridge.State.coins;
        npc.GetComponent<Interactable2D>().Interact(inventory.gameObject);
        var conversation = npc.GetComponent<ConversationDialogue>(); conversation.StopAllCoroutines(); Call(conversation, "FinishDialogue");
        yield return null;
        Check(QuestJournal.Find("market_quest").rewardClaimed && bridge.State.coins == beforeReward + 50, "NPC completion dialogue reports and rewards once");
        int rewardedCoins = bridge.State.coins;
        Check(!QuestJournal.Report("market_quest", bridge) && bridge.State.coins == rewardedCoins, "repeated quest report cannot double reward");
        Object.FindFirstObjectByType<ScenePortal2D>().GetComponent<Interactable2D>().Interact(inventory.gameObject);
        while (SceneManager.GetActiveScene().name != "Forest" || SceneFadeTransition.IsRunning) yield return null;
        var crystals = Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).Where(p => p.Definition?.ItemId == "sky_crystal").ToArray();
        Check(crystals.Length == 1 && crystals[0].Amount == 2 && Vector3.Distance(crystals[0].transform.position, new Vector3(-14, 0, 0)) < .01f,
            "A to B to A restores one exact drop without initial-loot duplication");
        Call(crystals[0], "TryCollect", inventory); yield return null;
        Check(Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).All(p => p.Definition?.ItemId != "sky_crystal"),
            "actual pickup removes only accepted items");
        Object.FindFirstObjectByType<ScenePortal2D>().GetComponent<Interactable2D>().Interact(inventory.gameObject);
        while (SceneManager.GetActiveScene().name != "Market" || SceneFadeTransition.IsRunning) yield return null;
        Object.FindFirstObjectByType<ScenePortal2D>().GetComponent<Interactable2D>().Interact(inventory.gameObject);
        while (SceneManager.GetActiveScene().name != "Forest" || SceneFadeTransition.IsRunning) yield return null;
        Check(Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).All(p => p.Definition?.ItemId != "sky_crystal"),
            "collected drops do not resurrect on revisit");
        Object.FindFirstObjectByType<HideOutEntrance>().GetComponent<Interactable2D>().Interact(inventory.gameObject);
        while (Object.FindFirstObjectByType<HideOutScene>() == null) yield return null;
        yield return null; yield return null;
        Check(bridge.Save(), "manual save after report"); yield return null;
        bridge.RestoreSnapshot(ManualSaveStore.ReadSlot());
        Check(!QuestJournal.Report("market_quest", bridge) && bridge.State.coins == rewardedCoins, "claimed save reload cannot double reward");
        byte[] lastGood = File.ReadAllBytes(ManualSaveStore.SlotPath);
        string restartRoot = Path.GetFullPath("Temp/ManualSaveValidation/restart-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(restartRoot); File.WriteAllBytes(Path.Combine(restartRoot, "slot-1-v2.json"), lastGood);
        SessionState.SetString(Key + ".restartRoot", restartRoot);
        using (var locked = new FileStream(ManualSaveStore.SlotPath + ".tmp", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            yield return null;
            Check(!bridge.Save(), "locked temporary file reports save failure");
            Check(File.ReadAllBytes(ManualSaveStore.SlotPath).SequenceEqual(lastGood), "failed write preserves last good save");
        }
        var bad = JsonUtility.FromJson<HideOutSave>(JsonUtility.ToJson(saved)); bad.inventory[0].id = "missing-item";
        int coinsBefore = bridge.State.coins; bool rejected = false;
        try { bridge.RestoreSnapshot(bad); } catch { rejected = true; }
        Check(rejected && bridge.State.coins == coinsBefore, "invalid snapshot rejected before partial application");
        File.WriteAllText(ManualSaveStore.SlotPath, "{broken"); rejected = false;
        try { ManualSaveStore.ReadSlot(); } catch { rejected = true; }
        Check(rejected, "corrupt file is rejected without initialization");
        File.WriteAllText(ManualSaveStore.SlotPath, System.Text.Encoding.UTF8.GetString(lastGood).Replace("\"formatVersion\": 2", "\"formatVersion\": 999"));
        rejected = false; try { ManualSaveStore.ReadSlot(); } catch { rejected = true; }
        Check(rejected, "unsupported format version is rejected");
        File.WriteAllText(ManualSaveStore.SlotPath, System.Text.Encoding.UTF8.GetString(lastGood).Replace("sha256", "brokenHash"));
        rejected = false; try { ManualSaveStore.ReadSlot(); } catch { rejected = true; }
        Check(rejected, "checksum corruption is rejected");
        File.WriteAllBytes(ManualSaveStore.SlotPath, lastGood);
        Check(File.Exists(ManualSaveStore.SlotPath + ".bak"), "atomic replace keeps prior backup");
        ManualSaveStore.DeleteSlot();
        Check(!ManualSaveStore.Exists && !File.Exists(ManualSaveStore.SlotPath + ".bak"), "slot deletion also removes backup");
        CheckDrops(settings);
        // A confirmed new game still keeps the old file until the first successful bed save.
        Directory.CreateDirectory(Path.GetDirectoryName(ManualSaveStore.SlotPath));
        File.WriteAllBytes(ManualSaveStore.SlotPath, lastGood);
        SaveGameSession.ReturnToMenu();
        while (SceneManager.GetActiveScene().name != "ControlSchemeSelect" || SceneFadeTransition.IsRunning) yield return null;
        menu = Object.FindFirstObjectByType<ManualSaveMenu>(); yield return null;
        menu.NewGame(); menu.Confirm(); Object.FindFirstObjectByType<ControlSchemeSelectScene>().SelectArrowKeys();
        while (SceneManager.GetActiveScene().name != "Forest" || SceneFadeTransition.IsRunning) yield return null;
        inventory = Object.FindFirstObjectByType<PlayerInventory>();
        Check(File.ReadAllBytes(ManualSaveStore.SlotPath).SequenceEqual(lastGood), "approved overwrite keeps previous save during new field play");
        Object.FindFirstObjectByType<HideOutEntrance>().GetComponent<Interactable2D>().Interact(inventory.gameObject);
        while (Object.FindFirstObjectByType<HideOutScene>() == null) yield return null;
        yield return null; bridge = inventory.GetComponent<HideOutPlayerBridge>();
        Check(bridge.State.coins == 0 && bridge.State.levels.All(n => n == 0) && QuestJournal.Find("market_quest") == null,
            "new game initializes independent permanent state");
        yield return null; Check(bridge.Save(), "first new-game manual save succeeds"); yield return null;
        Check(!File.ReadAllBytes(ManualSaveStore.SlotPath).SequenceEqual(lastGood) && ManualSaveStore.ReadSlot().coins == 0,
            "first successful manual save replaces previous slot");
        SaveGameSession.ReturnToMenu();
        while (SceneManager.GetActiveScene().name != "ControlSchemeSelect" || SceneFadeTransition.IsRunning) yield return null;
        menu = Object.FindFirstObjectByType<ManualSaveMenu>(); yield return null;
        menu.Continue();
        while (Object.FindFirstObjectByType<HideOutScene>() == null || SceneFadeTransition.IsRunning) yield return null;
        Check(Object.FindFirstObjectByType<HideOutPlayerBridge>().State.coins == 0, "Continue uses newly overwritten save");
        SaveGameSession.ReturnToMenu();
        while (SceneManager.GetActiveScene().name != "ControlSchemeSelect" || SceneFadeTransition.IsRunning) yield return null;
        menu = Object.FindFirstObjectByType<ManualSaveMenu>(); yield return null;
        menu.Delete(); menu.Cancel(); Check(ManualSaveStore.Exists, "delete cancellation preserves slot");
        File.WriteAllText(ManualSaveStore.LegacyPath, "{}");
        File.WriteAllText(ManualSaveStore.LegacyPath + ".bak", "{}");
        menu.Delete(); menu.Confirm();
        Check(!ManualSaveStore.Exists && !File.Exists(ManualSaveStore.SlotPath + ".bak") && !File.Exists(ManualSaveStore.LegacyPath + ".bak"),
            "confirmed menu deletion removes current, legacy and backup saves");
    }
    private static IEnumerator RestartChecks()
    {
        ManualSaveStore.UseValidationDirectory(SessionState.GetString(Key + ".restartRoot", ""));
        while (Object.FindFirstObjectByType<ManualSaveMenu>() == null) yield return null;
        yield return null; yield return null;
        Check(QuestJournal.Find("market_quest") == null, "fresh play session starts without old in-memory quests");
        Object.FindFirstObjectByType<ManualSaveMenu>().Continue();
        while (Object.FindFirstObjectByType<HideOutScene>() == null || SceneFadeTransition.IsRunning) yield return null;
        yield return null;
        var bridge = Object.FindFirstObjectByType<HideOutPlayerBridge>();
        Check(bridge.State.levels[3] == 1 && bridge.Inventory.Stats.Attack == 1
            && bridge.Inventory.GetEquipped(InventoryItemDefinition.EquipmentSlot.Accessory)?.ItemId == "tutorial_gear",
            "disk restore across editor play-session restart preserves facilities, stats and equipment");
        Check(QuestJournal.Find("market_quest").rewardClaimed && !QuestJournal.Report("market_quest", bridge),
            "reward claim survives full play-session restart");
        Check(RoomDropCache.Previous == null, "fresh Continue has no field drop cache");
        ScreenCapture.CaptureScreenshot("Temp/ManualSaveValidation/continue-hideout.png");
        for (int i = 0; i < 30; i++) yield return null;
        string legacyDirectory = Path.GetFullPath("Temp/ManualSaveValidation/legacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(legacyDirectory); ManualSaveStore.UseValidationDirectory(legacyDirectory);
        var legacy = JsonUtility.FromJson<HideOutSave>(JsonUtility.ToJson(bridge.State)); legacy.version = 1;
        legacy.quests.Clear(); legacy.facilities.Clear();
        string legacyJson = JsonUtility.ToJson(legacy);
        File.WriteAllText(ManualSaveStore.LegacyPath, legacyJson);
        var readLegacy = ManualSaveStore.ReadSlot(); bridge.RestoreSnapshot(readLegacy);
        Check(readLegacy.version == 1 && bridge.Inventory.GetEquipped(InventoryItemDefinition.EquipmentSlot.Accessory)?.ItemId == "tutorial_gear",
            "original player-v1 format restores equipment and facilities");
        yield return null; Check(bridge.Save() && ManualSaveStore.ReadSlot().version == 2, "next bed save migrates legacy data to versioned slot");
        Check(File.ReadAllText(ManualSaveStore.LegacyPath) == legacyJson, "legacy migration preserves original file until explicit deletion");
        SaveGameSession.StartExpedition();
        while (SceneManager.GetActiveScene().name != "Forest" || SceneFadeTransition.IsRunning) yield return null;
        var npc = Object.FindFirstObjectByType<QuestGiver2D>();
        Check(npc != null && npc.CurrentStage == QuestGiver2D.QuestStage.NotStarted, "authored Forest collection NPC is available in expedition");
        AcceptNpc(npc);
        Check(QuestJournal.Find("collection_quest")?.stage == 1, "NPC choice and acceptance dialogue accept collection quest");
        var questPickup = Object.FindFirstObjectByType<QuestPickup2D>();
        Call(questPickup, "OnTriggerEnter2D", bridge.Inventory.GetComponent<BoxCollider2D>()); yield return null;
        Check(QuestJournal.Find("collection_quest").stage == 2, "actual quest pickup records accepted inventory amount");
        int coins = bridge.State.coins;
        npc.Interact(); var dialogue = npc.GetComponent<ConversationDialogue>(); dialogue.StopAllCoroutines(); Call(dialogue, "FinishDialogue"); yield return null;
        Check(QuestJournal.Find("collection_quest").rewardClaimed && bridge.State.coins == coins + 20, "collection NPC report rewards once");
        Object.FindFirstObjectByType<ScenePortal2D>().GetComponent<Interactable2D>().Interact(bridge.Inventory.gameObject);
        while (SceneManager.GetActiveScene().name != "Market" || SceneFadeTransition.IsRunning) yield return null;
        npc = Object.FindFirstObjectByType<QuestGiver2D>(); AcceptNpc(npc);
        Check(QuestJournal.Find("market_quest")?.stage == 1, "existing Market NPC choice and dialogue accept kill quest");
    }
    private static void AcceptNpc(QuestGiver2D npc)
    {
        npc.Interact();
        var dialogue = npc.GetComponent<ConversationDialogue>(); dialogue.StopAllCoroutines();
        Call(dialogue, "CreateChoiceButtons", "수락", "거절"); Call(dialogue, "SelectChoice", true);
        dialogue.StopAllCoroutines(); Call(dialogue, "FinishDialogue");
    }
    private static void CheckDrops(HideOutSettings settings)
    {
        RoomDropCache.Clear();
        var a = SceneManager.CreateScene("DropCheckA"); var b = SceneManager.CreateScene("DropCheckB"); var c = SceneManager.CreateScene("DropCheckC");
        // Unsaved temporary scenes have empty paths, so explicit IDs are used with a common position origin.
        string aid = a.path, bid = "B", cid = "C";
        var root = new GameObject("Drop A"); SceneManager.MoveGameObjectToScene(root, a); root.transform.position = new Vector3(3, 4, 0);
        root.AddComponent<WorldItemPickup>().Configure(settings.Find("health_potion"), 2);
        RoomDropCache.SetCurrent(aid); var ab = RoomDropCache.Prepare(bid); RoomDropCache.Commit(ab, b);
        Check(RoomDropCache.Previous.drops.Count == 1, "A to B captures uncollected drop");
        var ba = RoomDropCache.Prepare(aid);
        Check(ba.arriving != null && ba.arriving.drops.Single().amount == 2, "arrival A snapshot reserved before departing B replaces cache");
        RoomDropCache.Commit(ba, a);
        Check(RoomDropCache.Previous.roomId == bid && RoomDropCache.Previous.drops.Count == 0, "empty departing room replaces previous cache");
        var ac = RoomDropCache.Prepare(cid); // not committed: simulated load failure
        Check(RoomDropCache.Previous.roomId == bid, "failed transition leaves cache unchanged");
        RoomDropCache.SetCurrent(bid); var bc = RoomDropCache.Prepare(cid); RoomDropCache.Commit(bc, c);
        Check(RoomDropCache.Previous.roomId == bid && RoomDropCache.Previous.drops.Count == 0, "A to B to C replaces cache with empty B snapshot");
        var collector = new GameObject("Partial pickup collector").AddComponent<PlayerInventory>();
        collector.Add(settings.Find(settings.testWeaponId), collector.Capacity - 1);
        collector.Add(settings.Find("health_potion"), 49);
        var partialRoot = new GameObject("Partial pickup"); SceneManager.MoveGameObjectToScene(partialRoot, c);
        var partial = partialRoot.AddComponent<WorldItemPickup>(); partial.Configure(settings.Find("health_potion"), 3);
        Call(partial, "TryCollect", collector);
        Check(partial.Amount == 2, "partial pickup leaves unaccepted quantity on ground");
        Call(partial, "TryCollect", collector);
        Check(partial.Amount == 2, "full inventory cannot delete remaining ground quantity");
        collector.EquipFromSlot(0); Call(partial, "TryCollect", collector);
        Check(partial.Amount == 0, "pickup removes ground item only when all quantity entered inventory");
        Object.Destroy(collector.gameObject);
        RoomDropCache.Clear(); Check(RoomDropCache.Previous == null, "session end clears drop cache");
        SceneManager.UnloadSceneAsync(a); SceneManager.UnloadSceneAsync(b); SceneManager.UnloadSceneAsync(c);
    }
}
#endif
