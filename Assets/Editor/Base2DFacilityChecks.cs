using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BehindTheGear.HideOut;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class Base2DFacilityChecks
{
    private const string Result = "Temp/Base2DMigration/check-result.txt";
    private static readonly StringBuilder Report = new();
    private static bool running;
    private static int errors;
    [MenuItem("Tools/HideOut/Run 2D base integration checks")]
    public static void RunChecks()
    {
        if (Application.unityVersion != "6000.3.23f1") throw new InvalidOperationException("Use Unity 6000.3.23f1.");
        if (running || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        var view = Object.FindFirstObjectByType<HideOutScene>();
        if (view == null || view.spriteEnvironment == null || view.TrainingPlayer == null || !view.TrainingPlayer.Initialized) return;
        if (!view.TrainingPlayer.Sandbox) throw new InvalidOperationException("Run Base_Test directly; checks must not use a live player's state.");
        Directory.CreateDirectory(Path.GetDirectoryName(Result));
        running = true;
        Report.Clear(); errors = 0;
        Application.logMessageReceived += Observe;
        view.TrainingPlayer.StartCoroutine(Run(view));
    }

    private static void Observe(string message, string trace, LogType type)
    {
        if (type != LogType.Exception && type != LogType.Error) return;
        errors++;
        Report.AppendLine("ERROR " + message);
    }

    private static IEnumerator Run(HideOutScene view)
    {
        bool success = true;
        var iterator = Checks(view);
        while (true)
        {
            object current;
            try { if (!iterator.MoveNext()) break; current = iterator.Current; }
            catch (Exception error) { Report.AppendLine("FAIL " + error); success = false; break; }
            yield return current;
        }
        success &= errors == 0;
        Report.AppendLine("Runtime errors: " + errors);
        Report.AppendLine(success ? "PASS 2D base integration" : "FAIL 2D base integration");
        Application.logMessageReceived -= Observe;
        File.WriteAllText(Result, Report.ToString());
        running = false;
        EditorApplication.isPlaying = false;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Report.AppendLine("PASS " + message);
    }

    private static Button Button(HideOutScene view, string name)
        => view.GetComponentsInChildren<Button>(true).Single(b => b.name == name);
    private static void Invoke(HideOutScene view, string method)
        => typeof(HideOutScene).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
    private static void Select(HideOutScene view, Facility facility)
    {
        string name = view.settings.FindPresentation(facility).displayName + " Facility Icon";
        var button = Button(view, name);
        Check(button.interactable, facility + " icon is connected and enabled");
        button.onClick.Invoke();
    }

    private static IEnumerator Checks(HideOutScene view)
    {
        yield return null;
        var bridge = view.TrainingPlayer;
        var environment = view.spriteEnvironment;
        Check(bridge.Sandbox, "direct scene play uses isolated sandbox state");
        Check(Object.FindFirstObjectByType<HideOutIsometricEnvironment>() == null, "no generated 3D base");
        Check(environment.areas.Length == 8 && environment.upgradeProps.Length == 19, "eight facilities and nineteen authored hidden props");
        Check(environment.upgradeProps.All(p => !p.target.activeSelf), "level zero preserves authored disabled props");
        Check(environment.areas.Count(a => a.icon != null) == 4, "four supplied icons connected");
        Check(view.GetComponentsInChildren<Canvas>(true).Any(), "existing facility UI created");
        if (view.canvasPrefab != null)
        {
            var canvas = view.GetComponentsInChildren<HideOutCanvasView>(true).Single();
            Check(canvas.upgradePanel.anchoredPosition == view.canvasPrefab.upgradePanel.anchoredPosition
                && canvas.upgradePanel.localScale == view.canvasPrefab.upgradePanel.localScale,
                "Canvas prefab placement and scale preserved");
            Check(canvas.upgradeTitle.font == view.canvasPrefab.upgradeTitle.font
                && canvas.upgradeTitle.fontSize == view.canvasPrefab.upgradeTitle.fontSize,
                "Canvas prefab typography preserved");
            Check(canvas.GetComponentsInChildren<Image>(true).All(i => i.sprite == null || AssetDatabase.Contains(i.sprite)),
                "all Canvas sprites use persistent assets");
            Check(canvas.actions.Length == 25 && canvas.inventorySlots.Length == 10 && canvas.storageSlots.Length == 10,
                "all Canvas button roles and both storage columns connected");
            canvas.nextFacilities.onClick.Invoke();
            Check(canvas.shortcutScroll.horizontalNormalizedPosition > 0f, "prefab shortcut next button scrolls");
            canvas.previousFacilities.onClick.Invoke();
        }
        Check(Object.FindObjectsByType<ConversationDialogue>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0,
            "existing dialogue presentation retained");
        Button(view, "Grant Test Resources").onClick.Invoke();
        yield return null;
        Check(bridge.State.coins >= 1000, "sandbox resources granted: " + bridge.Message);
        Button(view, "Unlock All Test Facilities").onClick.Invoke();
        yield return null;

        Select(view, Facility.Bed);
        yield return new WaitForSecondsRealtime(1f);
        Check(Button(view, "Upgrade Button").gameObject.activeInHierarchy, "bed opens existing upgrade panel");
        var bedding = environment.upgradeProps.Single(p => p.facility == Facility.Bed && p.requiredLevel == 1).target;
        Vector3 rest = bedding.transform.localPosition;
        int hp = bridge.Health.MaxHealth;
        int coins = bridge.State.coins;
        Button(view, "Upgrade Button").onClick.Invoke();
        Report.AppendLine($"Bed result: Lv {bridge.State.levels[1]}, coins {coins}->{bridge.State.coins}, HP {hp}->{bridge.Health.MaxHealth}, message: {bridge.Message}");
        Check(bridge.State.levels[(int)Facility.Bed] == 1 && bridge.State.coins < coins && bridge.Health.MaxHealth > hp,
            "bed upgrade spends resources and increases maximum HP");
        Check(bedding.activeSelf && bedding.transform.localPosition.y > rest.y, "new prop starts above its authored position");
        yield return new WaitForSecondsRealtime(.6f);
        Check(Vector3.Distance(rest, bedding.transform.localPosition) < .0001f, "drop animation lands exactly at authored position");
        Check(environment.upgradeProps.Where(p => p.facility == Facility.Bed && p.requiredLevel > 1).All(p => !p.target.activeSelf),
            "later bed props remain hidden");

        var pathField = typeof(HideOutPlayerBridge).GetField("validationSavePath", BindingFlags.NonPublic | BindingFlags.Instance);
        string previousPath = (string)pathField.GetValue(bridge);
        string save = Path.GetFullPath("Temp/Base2DMigration/isolated-save.json");
        try
        {
            pathField.SetValue(bridge, save);
            yield return null;
            Check(bridge.Save() && File.Exists(save), "save writes only isolated Temp slot");
            yield return null;
            bridge.Upgrade(Facility.Bed);
            yield return new WaitForSecondsRealtime(.6f);
            Check(bridge.State.levels[(int)Facility.Bed] == 2, "second upgrade unlocks next prop");
            bridge.Load();
            Check(bridge.State.levels[(int)Facility.Bed] == 1 && !environment.upgradeProps.Single(p => p.facility == Facility.Bed && p.requiredLevel == 2).target.activeSelf,
                "load restores saved levels and prop visibility without replaying upgrades");
        }
        finally { pathField.SetValue(bridge, previousPath); }

        Invoke(view, "ReturnToOverview"); yield return new WaitForSecondsRealtime(1f);
        Select(view, Facility.Storage); yield return new WaitForSecondsRealtime(1f);
        Check(Button(view, "Upgrade Storage").gameObject.activeInHierarchy, "storage opens existing inventory/storage UI");
        Button(view, "Upgrade Storage").onClick.Invoke(); yield return new WaitForSecondsRealtime(.6f);
        Check(bridge.State.levels[(int)Facility.Storage] == 1 && environment.upgradeProps.Single(p => p.facility == Facility.Storage && p.requiredLevel == 1).target.activeSelf,
            "storage upgrade unlocks authored box");
        Invoke(view, "ReturnToOverview"); yield return new WaitForSecondsRealtime(1f);
        Select(view, Facility.Potions); yield return new WaitForSecondsRealtime(1f);
        Check(Button(view, "Use Facility").gameObject.activeInHierarchy, "potion area opens existing claim panel");
        Button(view, "Use Facility").onClick.Invoke(); yield return null;
        Check(bridge.State.potionsReadyAt > bridge.Now, "potion claim uses existing cooldown and inventory transaction");
        Invoke(view, "ReturnToOverview"); yield return new WaitForSecondsRealtime(1f);
        Select(view, Facility.Gym); yield return new WaitForSecondsRealtime(1f);
        int mana = bridge.MaxMana;
        Button(view, "Upgrade Button").onClick.Invoke(); yield return new WaitForSecondsRealtime(.6f);
        Check(bridge.MaxMana > mana, "gym retains MP upgrade logic");
        Invoke(view, "ReturnToOverview"); yield return new WaitForSecondsRealtime(1f);
        Select(view, Facility.Clinic); yield return new WaitForSecondsRealtime(2.2f);
        var clinic = Object.FindFirstObjectByType<TreatmentRoomScene>();
        Check(clinic != null && clinic.Player == bridge, "clinic enters existing treatment room with same player");
        var shelf = environment.upgradeProps.Single(p => p.facility == Facility.Clinic && p.requiredLevel == 1).target;
        bridge.Upgrade(Facility.Clinic); yield return null;
        Check(bridge.State.levels[(int)Facility.Clinic] == 1 && !shelf.activeSelf, "offscreen clinic reveal waits for base return");
        yield return HideOutTravel.ExitTreatment(clinic);
        yield return new WaitForSecondsRealtime(.8f);
        Check(view.gameObject.activeInHierarchy && shelf.activeInHierarchy, "clinic return reveals upgraded prop on 2D base");
        Invoke(view, "ReturnToOverview"); yield return new WaitForSecondsRealtime(1f);
        Select(view, Facility.Dummy); yield return new WaitForSecondsRealtime(2.2f);
        var training = Object.FindFirstObjectByType<ScareCrowTrainingScene>();
        Check(training != null && training.Player == bridge, "dummy enters existing training room with same player");
        yield return HideOutTravel.ExitTraining(training);
        yield return new WaitForSecondsRealtime(.6f);
        Check(view.gameObject.activeInHierarchy && Object.FindFirstObjectByType<HideOutIsometricEnvironment>() == null,
            "training returns to authored 2D base");
        var camera = view.gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<HideOutCameraController>(true)).Single();
        camera.SnapToOverview();
        Check(camera.transform.rotation == Quaternion.identity && camera.GetComponent<Camera>().orthographic,
            "camera retains XY orthographic view");
        Check(errors == 0, "no runtime exceptions across facility round trips");
    }
}
