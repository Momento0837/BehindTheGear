using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BehindTheGear.HideOut;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Opt-in integration check: direct Base_Test sandbox only; never writes a player save.
[InitializeOnLoad]
public static class ClinicTutorialChecks
{
    private const string Folder = "Temp/ClinicTutorial/";
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly StringBuilder Report = new();
    private static bool running;
    private static int errors;
    [Serializable] private sealed class Setup { public SceneSetup[] scenes; public string startScene; }
    static ClinicTutorialChecks()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            string json = SessionState.GetString("ClinicTutorialChecks.Setup", "");
            if (string.IsNullOrEmpty(json)) return;
            SessionState.EraseString("ClinicTutorialChecks.Setup");
            var setup = JsonUtility.FromJson<Setup>(json);
            EditorSceneManager.RestoreSceneManagerSetup(setup.scenes);
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(setup.startScene) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(setup.startScene);
        };
    }
    private static void Poll()
    {
        bool entrance = File.Exists(Folder + "entrance.request");
        bool atmosphere = File.Exists(Folder + "atmosphere.request");
        if ((!File.Exists(Folder + "check.request") && !entrance && !atmosphere) || running || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (Application.unityVersion != "6000.3.23f1") return;
        if (!EditorApplication.isPlaying)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty) return;
            var setup = new Setup { scenes = EditorSceneManager.GetSceneManagerSetup(), startScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) };
            SessionState.SetString("ClinicTutorialChecks.Setup", JsonUtility.ToJson(setup));
            EditorSceneManager.playModeStartScene = null;
            EditorSceneManager.OpenScene("Assets/!UI/Base/Base_Test.unity");
            if (entrance)
            {
                Directory.CreateDirectory("Temp/BaseEntrance");
                File.WriteAllText("Temp/BaseEntrance/check.request", "run");
                File.Delete(Folder + "entrance.request");
            }
            if (atmosphere)
            {
                Directory.CreateDirectory("Temp/BaseGroundAtmosphere");
                File.WriteAllText("Temp/BaseGroundAtmosphere/check.request", "run");
                File.Delete(Folder + "atmosphere.request");
            }
            EditorApplication.isPlaying = true; return;
        }
        if (SceneManager.GetActiveScene().path != "Assets/!UI/Base/Base_Test.unity") return;
        if (!Application.isPlaying) return;
        var view = Object.FindFirstObjectByType<HideOutScene>();
        if (view?.TrainingPlayer?.Initialized != true || view.EntryAnimationPlaying) return;
        File.Delete(Folder + "check.request");
        if (!view.TrainingPlayer.Sandbox) throw new InvalidOperationException("Use direct Base_Test sandbox only.");
        running = true; errors = 0; Report.Clear();
        Application.logMessageReceived += Observe;
        view.TrainingPlayer.StartCoroutine(Run(view));
    }
    private static object Read(object obj, string name) => obj.GetType().GetField(name, Fields).GetValue(obj);
    private static void Call(object obj, string name) => obj.GetType().GetMethod(name, Fields).Invoke(obj, null);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Report.AppendLine("PASS " + message);
        File.WriteAllText(Folder + "progress.txt", Report.ToString());
    }
    private static void Observe(string text, string trace, LogType type)
    {
        if (type is LogType.Error or LogType.Exception) { errors++; Report.AppendLine("ERROR " + text); }
    }
    private static IEnumerator Run(HideOutScene view)
    {
        var stack = new Stack<IEnumerator>(); stack.Push(Verify(view)); bool success = true;
        while (stack.Count > 0)
        {
            object next;
            try
            {
                if (!stack.Peek().MoveNext()) { stack.Pop(); continue; }
                next = stack.Peek().Current;
                if (next is IEnumerator child) { stack.Push(child); continue; }
            }
            catch (Exception e) { Report.AppendLine("FAIL " + e); success = false; break; }
            yield return next;
        }
        Application.logMessageReceived -= Observe;
        Report.AppendLine("Runtime errors: " + errors);
        Report.AppendLine(success && errors == 0 ? "PASS clinic tutorial checks" : "FAIL clinic tutorial checks");
        File.WriteAllText(Folder + "check-result.txt", Report.ToString());
        running = false; EditorApplication.isPlaying = false;
    }
    private static IEnumerator Until(Func<bool> ready, string message, float seconds = 25)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
        Check(ready(), message);
    }
    private static IEnumerator AdvanceStory()
    {
        int frame = 0; float deadline = Time.realtimeSinceStartup + 35;
        while (BaseArrivalTutorial.IsShowing && Time.realtimeSinceStartup < deadline)
        {
            var dialogue = Object.FindObjectsByType<ConversationDialogue>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(d => d.IsPlaying);
            if (dialogue != null && frame++ % 4 == 0) Call(dialogue, "RequestAdvance");
            yield return null;
        }
        Check(!BaseArrivalTutorial.IsShowing, "story dialogue completes normally");
    }
    private static IEnumerator Hint(MonoBehaviour owner, Button button, string name)
    {
        yield return Until(() => owner.GetComponent<TutorialButtonHighlight>()?.Target == button, name + " becomes the highlight target");
        yield return new WaitForSecondsRealtime(.65f);
        yield return new WaitForEndOfFrame();
        var hint = owner.GetComponent<TutorialButtonHighlight>();
        var border = (Image)Read(hint, "border");
        var dim = (Image[])Read(hint, "dim");
        var canvas = (Canvas)Read(hint, "canvas");
        Check(hint.Target == button && border.enabled, name + " retains a visible white pulse");
        Check(canvas.GetComponent<GraphicRaycaster>() == null && canvas.GetComponentsInChildren<Graphic>().All(g => !g.raycastTarget), name + " highlight never intercepts clicks");
        Check(dim.All(d => Mathf.Abs(d.color.a - .3f) < .001f), name + " uses the same 30% inventory dimmer");
        var rect = (RectTransform)button.transform;
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        var parentCanvas = button.GetComponentInParent<Canvas>();
        var camera = parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : parentCanvas.worldCamera;
        var points = corners.Select(c => RectTransformUtility.WorldToScreenPoint(camera, c)).ToArray();
        var size = new Vector2(points.Max(p => p.x) - points.Min(p => p.x), points.Max(p => p.y) - points.Min(p => p.y));
        Check(Vector2.Distance(border.rectTransform.sizeDelta, size) < .5f, name + " border fits actual button size " + size);
        Check(button.GetComponent<Canvas>() == null, name + " does not modify the target Canvas hierarchy");
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Folder + name + ".png", tex.EncodeToPNG()); Object.Destroy(tex);
    }
    private static void Click(Button button)
    {
        Canvas.ForceUpdateCanvases();
        var rect = (RectTransform)button.transform;
        var canvas = button.GetComponentInParent<Canvas>();
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left,
            position = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center)) };
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(data, hits);
        Check(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button, button.name + " receives the first UI raycast");
        ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerClickHandler);
    }
    private static IEnumerator Verify(HideOutScene view)
    {
        var bridge = view.TrainingPlayer;
        var canvas = view.GetComponentInChildren<HideOutCanvasView>(true);
        Check(!bridge.State.baseTutorialComplete && !bridge.State.baseFirstTreatmentComplete, "fresh sandbox starts before the clinic tutorial");
        Check(view.GetComponent<TutorialButtonHighlight>() == null, "locked clinic has no tutorial highlight");
        var progress = bridge.GetComponent<PrototypeTutorialState>() ?? bridge.gameObject.AddComponent<PrototypeTutorialState>();
        progress.SelenaArrived = true;
        Object.Destroy(view.GetComponent<BaseArrivalTutorial>()); yield return null;
        view.gameObject.AddComponent<BaseArrivalTutorial>();
        yield return Until(() => BaseArrivalTutorial.IsShowing, "Selena arrival begins the real tutorial dialogue");
        yield return AdvanceStory();
        Check(bridge.State.baseTutorialComplete, "arrival dialogue unlocks the clinic");
        var clinic = canvas.facilities.Single(f => f.facility == Facility.Clinic).button;
        yield return Hint(view, clinic, "01-clinic");
        Click(clinic);
        yield return Until(() => TreatmentRoomScene.IsActive && !SceneFadeTransition.IsRunning && !HideOutTravel.IsTravelling, "real clinic button enters treatment scene");
        var room = Object.FindFirstObjectByType<TreatmentRoomScene>();
        Check(room.Player == bridge, "treatment retains the same sandbox player");
        var advance = (Button)Read(room, "tutorialAdvanceButton");
        yield return Hint(room, advance, "02-introduction");
        float deadline = Time.realtimeSinceStartup + 12;
        while ((bool)Read(room, "typing") || (bool)Read(room, "holding"))
        {
            Check(Time.realtimeSinceStartup < deadline, "treatment introduction can advance");
            Click(advance); yield return new WaitForSecondsRealtime(.15f);
        }
        var heal = (Button)Read(room, "tutorialHealButton");
        yield return Hint(room, heal, "03-heal");
        Click(heal);
        yield return Until(() => BaseArrivalTutorial.IsShowing, "first heal starts post-treatment dialogue");
        yield return null;
        Check(bridge.State.baseFirstTreatmentComplete && room.GetComponent<TutorialButtonHighlight>().Target == null, "completed heal removes its highlight during dialogue");
        yield return AdvanceStory();
        Check(bridge.State.baseTreatmentDialogueComplete, "post-treatment dialogue unlocks the potion booth");
        var back = (Button)Read(room, "tutorialBackButton");
        yield return Hint(room, back, "04-return");
        Click(back);
        yield return Until(() => !TreatmentRoomScene.IsActive && !SceneFadeTransition.IsRunning && !HideOutTravel.IsTravelling, "return button restores the base");
        var potions = canvas.facilities.Single(f => f.facility == Facility.Potions).button;
        yield return Hint(view, potions, "05-potions");
        Click(potions);
        var receive = (Button)Read(view, "useFacilityButton");
        yield return Hint(view, receive, "06-receive");
        Click(receive);
        yield return Until(() => bridge.State.potionsReadyAt > 0, "highlighted receive button grants the actual potion");
        yield return new WaitForSecondsRealtime(.7f);
        Check(view.GetComponent<TutorialButtonHighlight>().Target == null && Read(view.GetComponent<TutorialButtonHighlight>(), "canvas") == null,
            "completed tutorial removes both the border and dimmer");
        Call(view, "ReturnToOverview");
        yield return new WaitForSecondsRealtime(2);
        Check(view.GetComponent<TutorialButtonHighlight>().Target == null, "completed tutorial does not restart on returning to overview");
    }
}
