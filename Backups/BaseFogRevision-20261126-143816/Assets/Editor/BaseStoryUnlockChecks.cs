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

public static class BaseStoryUnlockChecks
{
    private const string Folder = "Temp/BaseStoryUnlocks/";
    private static readonly StringBuilder Report = new();
    private static int errors;
    private static bool running;
    [MenuItem("Tools/HideOut/Check story unlocks and fog")]
    public static void RunChecks()
    {
        if (running || !EditorApplication.isPlaying || Application.unityVersion != "6000.3.23f1") return;
        var view = Object.FindFirstObjectByType<HideOutScene>();
        if (view?.TrainingPlayer == null || !view.TrainingPlayer.Sandbox) throw new InvalidOperationException("Use direct Base_Test sandbox play.");
        running = true; errors = 0; Report.Clear();
        Directory.CreateDirectory(Folder);
        Application.logMessageReceived += Observe;
        view.TrainingPlayer.StartCoroutine(Run(view));
    }
    private static void Observe(string message, string trace, LogType type)
    {
        if (type is LogType.Error or LogType.Exception) { errors++; Report.AppendLine("ERROR " + message); }
    }
    private static IEnumerator Run(HideOutScene view)
    {
        var iterator = Checks(view);
        bool success = true;
        while (true)
        {
            object step;
            try { if (!iterator.MoveNext()) break; step = iterator.Current; }
            catch (Exception error) { Report.AppendLine("FAIL " + error); success = false; break; }
            yield return step;
        }
        Application.logMessageReceived -= Observe;
        Report.AppendLine("Runtime errors: " + errors);
        Report.AppendLine(success && errors == 0 ? "PASS base story progression" : "FAIL base story progression");
        File.WriteAllText(Folder + "check-result.txt", Report.ToString());
        running = false;
        EditorApplication.isPlaying = false;
    }
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
        Report.AppendLine("PASS " + label);
    }
    private static void Refresh(HideOutScene view) => typeof(HideOutScene).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
    private static Transform Fog(HideOutSpriteEnvironment environment, Facility facility) => environment.transform.Find(facility + " Locked Fog");
    private static ConversationDialogue ActiveDialogue() => Object.FindObjectsByType<ConversationDialogue>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(d => d.IsPlaying);
    private static void Advance(ConversationDialogue dialogue) => typeof(ConversationDialogue).GetMethod("RequestAdvance", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dialogue, null);
    private static bool ArtworkVisible(Button button) => button.transform.Cast<Transform>().Any(t => t.name != "Temporary Facility Lock" && t.GetComponent<TMPro.TMP_Text>() == null && t.gameObject.activeSelf);

    private static IEnumerator Checks(HideOutScene view)
    {
        yield return null;
        var bridge = view.TrainingPlayer;
        var environment = view.spriteEnvironment;
        var canvas = view.GetComponentInChildren<HideOutCanvasView>(true);
        Check(environment.bigFogSprites.Length == 3 && environment.smallFogSprites.Length == 4, "all seven authored fog sprites connected");
        Check(HideOutModel.StoryUnlocked(bridge.State, Facility.Bed) && HideOutModel.StoryUnlocked(bridge.State, Facility.Dining), "Bed and Dining open initially");
        Check(!bridge.State.baseTutorialComplete && !bridge.State.baseTreatmentDialogueComplete, "fresh sandbox starts before story dialogue");
        foreach (var facility in canvas.facilities)
        {
            bool unlocked = HideOutModel.StoryUnlocked(bridge.State, facility.facility);
            Check(facility.button.gameObject.activeSelf && facility.ring.gameObject.activeSelf
                && facility.button.interactable == unlocked && ArtworkVisible(facility.button) == unlocked,
                facility.facility + " keeps circle and gates artwork/input");
            var fog = Fog(environment, facility.facility);
            Check(unlocked ? fog == null : fog != null && fog.gameObject.activeSelf && fog.childCount is >= 2 and <= 3,
                facility.facility + " fog exists only when locked, with 1-2 satellites");
        }
        var clouds = environment.GetComponentsInChildren<SpriteRenderer>().Where(r => r.transform.parent.name.EndsWith(" Locked Fog")).ToArray();
        Check(clouds.Select(r => r.transform.position.z).Distinct().Count() > 3, "large and small clouds have varied Z depth");
        var camera = Object.FindFirstObjectByType<HideOutCameraController>();
        Vector3 originalCamera = camera.transform.position;
        Vector3 originalCloud = clouds[0].transform.position;
        camera.transform.position += Vector3.right * .5f;
        yield return null;
        Check((clouds[0].transform.position - originalCloud).sqrMagnitude > .000001f, "orthographic camera movement produces depth parallax");
        camera.transform.position = originalCamera;
        yield return null;
        ScreenCapture.CaptureScreenshot(Path.GetFullPath(Folder + "initial-fog.png"));
        yield return null;
        Check(PlayerTargetRegistry.Current != null && PlayerTargetRegistry.Current.GetComponent<HideOutPlayerBridge>() == bridge,
            "arrival tutorial resolves the current story player");
        var progress = bridge.GetComponent<PrototypeTutorialState>() ?? bridge.gameObject.AddComponent<PrototypeTutorialState>();
        progress.SelenaArrived = true; // Same flag set by Neighborhood auto-walk completion.
        Object.Destroy(view.GetComponent<BaseArrivalTutorial>());
        yield return null;
        view.gameObject.AddComponent<BaseArrivalTutorial>();
        float arrivalDeadline = Time.realtimeSinceStartup + 3;
        while (!BaseArrivalTutorial.IsShowing && Time.realtimeSinceStartup < arrivalDeadline) yield return null;
        Check(BaseArrivalTutorial.IsShowing && !bridge.State.baseTutorialComplete, "Selena arrival flag automatically starts conversation before Clinic unlock");
        float deadline = Time.realtimeSinceStartup + 35;
        int frame = 0;
        while (BaseArrivalTutorial.IsShowing && Time.realtimeSinceStartup < deadline)
        {
            var dialogue = ActiveDialogue();
            if (dialogue != null && frame++ % 4 == 0) Advance(dialogue);
            yield return null;
        }
        Check(!BaseArrivalTutorial.IsShowing && bridge.State.baseTutorialComplete, "finishing arrival dialogue unlocks Clinic");
        Refresh(view);
        Check(!Fog(environment, Facility.Clinic).gameObject.activeSelf && Fog(environment, Facility.Potions).gameObject.activeSelf,
            "Clinic fog disappears while Potions stays covered");
        yield return null;
        canvas.facilities.Single(f => f.facility == Facility.Clinic).button.onClick.Invoke();
        deadline = Time.realtimeSinceStartup + 20;
        while ((!TreatmentRoomScene.IsActive || SceneFadeTransition.IsRunning) && Time.realtimeSinceStartup < deadline) yield return null;
        var room = Object.FindFirstObjectByType<TreatmentRoomScene>();
        Check(room != null && room.Player == bridge, "Clinic opens existing treatment room with same player");
        yield return null;
        room.Heal();
        Check(bridge.State.baseFirstTreatmentComplete && !bridge.State.baseTreatmentDialogueComplete && BaseArrivalTutorial.IsShowing,
            "successful first treatment starts JSON conversation but keeps Potions locked");
        Check(!HideOutModel.StoryUnlocked(bridge.State, Facility.Potions), "Potions is gated until final dialogue advance");
        room.ReturnToBase();
        Check(TreatmentRoomScene.IsActive && !SceneFadeTransition.IsRunning, "room exit is blocked during story conversation");
        yield return null;
        ScreenCapture.CaptureScreenshot(Path.GetFullPath(Folder + "treatment-dialogue.png"));
        deadline = Time.realtimeSinceStartup + 20;
        frame = 0;
        while (BaseArrivalTutorial.IsShowing && Time.realtimeSinceStartup < deadline)
        {
            var dialogue = ActiveDialogue();
            if (dialogue != null && frame++ % 4 == 0) Advance(dialogue);
            yield return null;
        }
        Check(bridge.State.baseTreatmentDialogueComplete && HideOutModel.StoryUnlocked(bridge.State, Facility.Potions), "complete treatment dialogue unlocks Potions");
        Check(!BaseArrivalTutorial.TryShowAfterTreatment(room, bridge, room.transform), "completed conversation does not repeat");
        var copy = JsonUtility.FromJson<HideOutSave>(JsonUtility.ToJson(bridge.State));
        Check(copy.baseFirstTreatmentComplete && copy.baseTreatmentDialogueComplete && HideOutModel.StoryUnlocked(copy, Facility.Potions), "new progression flags survive save JSON round trip");
        room.ReturnToBase();
        deadline = Time.realtimeSinceStartup + 20;
        while ((TreatmentRoomScene.IsActive || SceneFadeTransition.IsRunning) && Time.realtimeSinceStartup < deadline) yield return null;
        Check(view.gameObject.activeInHierarchy, "treatment returns to Base_Test");
        Refresh(view);
        var potions = canvas.facilities.Single(f => f.facility == Facility.Potions);
        Check(potions.button.interactable && ArtworkVisible(potions.button) && !Fog(environment, Facility.Potions).gameObject.activeSelf,
            "unlocked potion artwork appears and whole fog group disappears");
        Check(Fog(environment, Facility.Storage).gameObject.activeSelf && Fog(environment, Facility.Workshop).gameObject.activeSelf,
            "other locked regions retain fog");
        Check(view.GetComponentsInChildren<GraphicRaycaster>(true).All(r => r.enabled), "base UI input restored after conversations");
        yield return null;
        ScreenCapture.CaptureScreenshot(Path.GetFullPath(Folder + "unlocked-fog.png"));
        yield return null;
        // Exercise the production save/load envelope, isolated inside project Temp.
        var pathField = typeof(HideOutPlayerBridge).GetField("validationSavePath", BindingFlags.Instance | BindingFlags.NonPublic);
        object oldPath = pathField.GetValue(bridge);
        pathField.SetValue(bridge, Path.GetFullPath(Folder + "story-save-" + Guid.NewGuid().ToString("N") + ".json"));
        try
        {
            Check(bridge.Save(), "progression saves through the existing validated save system");
            yield return null;
            Check(bridge.Change(s => { s.baseTreatmentDialogueComplete = false; return "validation"; }), "isolated load preparation");
            yield return null;
            bridge.Load();
            Check(bridge.State.baseTutorialComplete && bridge.State.baseFirstTreatmentComplete && bridge.State.baseTreatmentDialogueComplete,
                "actual load restores arrival, first treatment, and dialogue completion flags");
            Refresh(view);
            Check(potions.button.interactable && !Fog(environment, Facility.Potions).gameObject.activeSelf, "loaded progression restores potion artwork and clears its fog");
        }
        finally { pathField.SetValue(bridge, oldPath); }
    }
}
