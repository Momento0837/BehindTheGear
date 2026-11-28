using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BehindTheGear.HideOut;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// One-time verification; archived outside Assets after the requested check.
[InitializeOnLoad]
public static class DialogueExpressionVerification
{
    private const string Folder = "Temp/DialogueExpressions/";
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static bool running;
    private static int errors;
    private static readonly StringBuilder Report = new();
    [Serializable] private class Line { public string speaker = "Ethan", 표정, text; }
    [Serializable] private class Content { public Line[] lines; }
    static DialogueExpressionVerification() => EditorApplication.update += Poll;
    private static void Poll()
    {
        if (running || !File.Exists(Folder + "check.request") || EditorApplication.isCompiling || EditorApplication.isUpdating
            || Application.unityVersion != "6000.3.23f1" || SceneManager.GetActiveScene().name != "Base_Test") return;
        if (!EditorApplication.isPlaying)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = true;
            return;
        }
        var host = Object.FindFirstObjectByType<HideOutScene>();
        if (host?.TrainingPlayer?.Initialized != true || SceneFadeTransition.IsRunning) return;
        File.Delete(Folder + "check.request"); running = true; errors = 0; Report.Clear();
        Application.logMessageReceived += Observe;
        host.StartCoroutine(Run(host));
    }
    private static void Observe(string message, string trace, LogType type)
    {
        if (type is LogType.Error or LogType.Exception) { errors++; Report.AppendLine("ERROR " + message); }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Report.AppendLine("PASS " + message);
    }
    private static IEnumerator Run(HideOutScene host)
    {
        var steps = Verify(host); bool passed = true;
        while (true)
        {
            object step;
            try { if (!steps.MoveNext()) break; step = steps.Current; }
            catch (Exception e) { Report.AppendLine("FAIL " + e); passed = false; break; }
            yield return step;
        }
        Application.logMessageReceived -= Observe;
        Report.AppendLine("Runtime errors: " + errors);
        Report.AppendLine(passed && errors == 0 ? "PASS dialogue expression rendering" : "FAIL dialogue expression rendering");
        File.WriteAllText(Folder + "check-result.txt", Report.ToString());
        running = false; EditorApplication.isPlaying = false;
    }
    private static IEnumerator Verify(HideOutScene host)
    {
        var library = Resources.Load<DialoguePortraitLibrary>("Dialogue/Portraits");
        Check(library != null, "portrait library loads through the runtime Resources path");
        string[] keys = { "기본 표정", "곤란", "의아", "짜증", "눈 돌림", "눈 반 감음", "눈 감음", "엥" };
        var expected = keys.Select(k => library.Resolve("Ethan", k)).ToArray();
        Check(expected.All(s => s != null) && expected.Distinct().Count() == keys.Length, "all eight Ethan expressions resolve to distinct sprites");
        var dependencies = AssetDatabase.GetDependencies("Assets/Resources/Dialogue/Portraits.asset", true);
        foreach (var sprite in expected)
            Check(dependencies.Contains(AssetDatabase.GetAssetPath(sprite)), sprite.name + " is referenced for player builds");
        Check(library.Resolve("Ethan", "missing-expression") == library.ethan, "missing expression retains the default portrait");
        var template = Object.FindObjectsByType<ConversationDialogue>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .First(d => d.BodyText != null && !d.IsPlaying);
        var dialogue = template.CreateTutorialPresentation(host.transform);
        typeof(ConversationDialogue).GetField("characterInterval", Flags).SetValue(dialogue, .005f);
        var content = new TextAsset(JsonUtility.ToJson(new Content { lines = keys.Select(k => new Line { 표정 = k, text = "표정 확인: " + k }).ToArray() }));
        dialogue.Play(content);
        var portrait = (Image)typeof(ConversationDialogue).GetField("portraitImage", Flags).GetValue(dialogue);
        for (int i = 0; i < keys.Length; i++)
        {
            yield return new WaitForSecondsRealtime(.3f);
            Check(dialogue.IsPlaying && portrait.enabled && portrait.sprite == expected[i], "JSON line renders Ethan / " + keys[i]);
            var animated = (Image)typeof(ConversationDialogue).GetField("animatedPortrait", Flags).GetValue(dialogue);
            Check(!animated.gameObject.activeSelf, "NPC animation does not cover Ethan / " + keys[i]);
            if (keys[i] is "의아" or "곤란" or "엥")
            {
                ScreenCapture.CaptureScreenshot(Path.GetFullPath(Folder + "expression-" + i + ".png"));
                yield return null;
            }
            typeof(ConversationDialogue).GetMethod("RequestAdvance", Flags).Invoke(dialogue, null);
        }
        yield return new WaitForSecondsRealtime(.2f);
        Check(!dialogue.IsPlaying, "eight-line dialogue finishes normally");
        Object.Destroy(dialogue.gameObject); Object.Destroy(content);
    }
}
