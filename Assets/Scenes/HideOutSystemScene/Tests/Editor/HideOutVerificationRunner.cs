using System;
using System.IO;
using BehindTheGear.HideOut;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Only executes after the menu command or an explicit project-Temp request marker.
[InitializeOnLoad]
public static class HideOutVerificationRunner
{
    private const string Armed = "HideOut.Validation.PlayArmed";
    private static double nextPoll;
    private static double requestSeenAt;
    static HideOutVerificationRunner()
    {
        EditorApplication.update += PollRequest;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Armed, false))
            {
                SessionState.SetBool(Armed, false);
                EditorApplication.delayCall += HideOutPlayChecks.Run;
            }
        };
    }
    private static void PollRequest()
    {
        if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        const string request = "Temp/HideOutValidation/request-ui-checks.flag";
        if (!File.Exists(request)) return;
        // A marker can arrive before Unity notices the accompanying source edits.
        // Refresh first and allow the compilation/domain reload to settle before running.
        if (requestSeenAt == 0) { requestSeenAt = EditorApplication.timeSinceStartup; AssetDatabase.Refresh(); return; }
        if (EditorApplication.timeSinceStartup - requestSeenAt < 8) return;
        requestSeenAt = 0;
        File.Delete(request);
        Run();
    }
    [MenuItem("Tools/HideOut/Verify preview and play")]
    public static void Run()
    {
        if (Application.unityVersion != "6000.3.23f1") throw new InvalidOperationException("Unity 6000.3.23f1 required.");
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            // Preserve the existing editor instance; no second editor or lock manipulation.
            void AfterStop(PlayModeStateChange state)
            {
                if (state != PlayModeStateChange.EnteredEditMode) return;
                EditorApplication.playModeStateChanged -= AfterStop;
                EditorApplication.delayCall += Run;
            }
            EditorApplication.playModeStateChanged += AfterStop;
            EditorApplication.isPlaying = false;
            return;
        }
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject sourceObject = null;
        string result = "";
        try
        {
            var settings = AssetDatabase.LoadAssetAtPath<HideOutSettings>("Assets/Scenes/HideOutSystemScene/Data/HideOutPrototype.asset");
            sourceObject = new GameObject("Isolated Preview Validation Source") { hideFlags = HideFlags.DontSave };
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(sourceObject, scene);
            var source = sourceObject.AddComponent<HideOutScene>(); source.settings = settings;
            HideOutPreviewEditor.Generate(source);
            var first = HideOutPreviewEditor.PreviewRoot;
            HideOutPreviewEditor.Generate(source);
            if (first != null) throw new Exception("Preview refresh did not destroy its prior generation.");
            var root = HideOutPreviewEditor.PreviewRoot;
            if (root.GetComponentsInChildren<HideOutIsometricEnvironment>(true).Length != 1) throw new Exception("Duplicate environment.");
            if (root.GetComponentsInChildren<PlayerInventory>(true).Length != 0 || root.GetComponentsInChildren<PlayerHealth>(true).Length != 0)
                throw new Exception("Preview created actual player data components.");
            foreach (var facility in settings.topView.shortcutOrder) root.GetComponent<HideOutScene>().ShowEditorSample(facility);
            root.GetComponent<HideOutScene>().HideEditorDetails();
            if (root.GetComponentsInChildren<MeshRenderer>().Length == 0) throw new Exception("Hiding details removed the environment.");
            result = "PASS explicit preview generation/refresh has one environment; all eight sample panels open; hiding details keeps geometry; no player inventory or HP components.\n";
        }
        catch (Exception exception) { result = "FAIL preview: " + exception + "\n"; }
        finally
        {
            HideOutPreviewEditor.Clear();
            if (sourceObject != null) UnityEngine.Object.DestroyImmediate(sourceObject);
            EditorSceneManager.ClosePreviewScene(scene);
        }
        Directory.CreateDirectory("Temp/HideOutValidation");
        File.WriteAllText("Temp/HideOutValidation/preview-checks.txt", result);
        SessionState.SetBool(Armed, true);
        EditorApplication.isPlaying = true;
    }
}
