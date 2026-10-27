using System;
using System.Collections;
using System.IO;
using System.Text;
using BehindTheGear.HideOut;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
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
        b.GrantTestResources();
        Check(b.State.coins == 1000 && b.Health.CurrentHealth == 5 && File.Exists(save), "isolated transactional save and test grant"); yield return null;
        b.Heal();
        Check(b.Health.CurrentHealth == 10 && b.State.clinicReadyAt > b.Now, "clinic heals and persists deadline"); yield return null;
        double deadline = b.State.clinicReadyAt;
        b.Heal(); Check(b.State.clinicReadyAt == deadline, "full HP/cooldown failure preserves deadline"); yield return null;
        b.Upgrade(Facility.Bed);
        Check(b.Health.MaxHealth == 20 && b.Health.CurrentHealth == 10, "bed increases max HP without healing"); yield return null;
        int damage = b.Melee.ResolvedDamage;
        b.Upgrade(Facility.Dummy);
        Check(b.Melee.ResolvedDamage == damage + 1, "equipped weapon damage modifier increases exactly once"); yield return null;
        var dummy = b.gameObject.AddComponent<HideOutDummy>();
        Check(b.Melee.TryAttackTarget(dummy) && dummy.LastDamage == damage + 1, "real melee target receives calculated damage");
        Check(!b.Melee.TryAttackTarget(dummy) && dummy.Hits == 1, "melee cooldown prevents immediate second hit");
        b.Inventory.SetCapacityBonus("ValidationOtherGrowth", 3);
        b.Health.SetMaxHealthBonus("ValidationOtherGrowth", 7);
        yield return null;
        b.Upgrade(Facility.Storage);
        Check(b.Inventory.Capacity == 20 && b.State.storage.Count == 15, "storage +2/+5 preserves other growth"); yield return null;
        b.Upgrade(Facility.Gym); Check(b.MaxMana == 101 && b.Health.MaxHealth == 27, "gym changes MP, not HP"); yield return null;
        b.Transfer(true, 0);
        int total = HideOutModel.Count(b.State.inventory,"hideout_scrap") + HideOutModel.Count(b.State.storage,"hideout_scrap");
        Check(HideOutModel.Count(b.State.storage,"hideout_scrap") > 0, "real bag-to-storage transfer"); yield return null;
        b.Transfer(false, 0);
        Check(HideOutModel.Count(b.State.inventory,"hideout_scrap") + HideOutModel.Count(b.State.storage,"hideout_scrap") == total, "real storage-to-bag conserves quantity"); yield return null;
        b.ReceivePotions(); Check(b.State.potionsReadyAt == 0, "Selena starts locked"); yield return null;
        b.TestRecruit(); yield return null;
        b.ReceivePotions(); Check(HideOutModel.Count(b.State.inventory,"health_potion") == 2 && b.State.potionsReadyAt > b.Now, "recruitment unlocks persisted potion delivery"); yield return null;
        b.Save(); yield return null;
        for (int i=0;i<3;i++) { b.Load(); yield return null; }
        Check(b.Inventory.Capacity == 20 && b.Health.MaxHealth == 27 && b.Melee.ResolvedDamage == damage + 1 && b.MaxMana == 101, "repeated load does not compound bonuses or discard other growth");
        Check(b.State.clinicReadyAt == deadline && b.State.potionsReadyAt > b.Now, "load preserves both cooldowns");
        Check(File.Exists(save + ".bak"), "atomic save retains previous backup");
        var snapshot = JsonUtility.FromJson<HideOutSave>(File.ReadAllText(save));
        Check(snapshot.equipment[0] == settings.testWeaponId && snapshot.questInventory.Count == b.Inventory.QuestCapacity, "save includes equipment and quest inventory");
        int coins = b.State.coins, level = b.State.levels[1];
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
