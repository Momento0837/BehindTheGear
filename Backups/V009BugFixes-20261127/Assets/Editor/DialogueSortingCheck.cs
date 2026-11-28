#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Opt-in preview-scene verification; never enters Play mode or saves authored assets.</summary>
[InitializeOnLoad]
public static class DialogueSortingCheck
{
    private const string Folder = "Logs/Validation/DialogueSorting";
    private const string HudPath = "Assets/!UI/Ingame/InGame_Canvas.prefab";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    static DialogueSortingCheck() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode
            || !File.Exists(Folder + "/check.request")) return;
        File.Delete(Folder + "/check.request");
        Run();
    }

    [MenuItem("Tools/Checks/Dialogue Sorting")]
    public static void Run()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode) return;
        Directory.CreateDirectory(Folder);
        var report = new StringBuilder();
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var prefabs = Resources.Load<DialogueCanvasPrefabs>("Dialogue/CanvasPrefabs");
            Require(prefabs != null && prefabs.storyCanvas != null, "Shared story canvas must be assigned");
            var source = prefabs.storyCanvas;
            string sourcePath = AssetDatabase.GetAssetPath(source);
            string sourceHash = Hash(sourcePath);
            string hudHash = Hash(HudPath);
            bool sourceDirty = EditorUtility.IsDirty(source);
            var hud = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
            Require(hud != null && hud.transform.Find("Skill_ZQ") != null
                && hud.transform.Find("Skill_EX") != null, "Actual HUD includes both skill slots");
            var hudCanvases = hud.GetComponentsInChildren<Canvas>(true);
            Require(hudCanvases.Length > 0, "Actual HUD has a canvas");

            var ui = (GameObject)PrefabUtility.InstantiatePrefab(source, preview);
            var view = ui.GetComponent<DialogueCanvasView>();
            Require(view != null && view.body != null && view.speaker != null,
                "Actual shared prefab contains dialogue references");
            var host = new GameObject("Dialogue sorting verification");
            host.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, preview);
            var dialogue = host.AddComponent<ConversationDialogue>();
            Call(dialogue, "BindCanvas", view);
            Call(dialogue, "SetVisible", true);
            VerifyVisible(ui, hudCanvases);
            report.AppendLine("PASS shared prefab clone uses overlay/default/32050/overrideSorting above skill HUD, below scene fade");

            Call(dialogue, "SetVisible", false);
            Require(!ui.activeSelf, "Dialogue must hide normally");
            Call(dialogue, "SetVisible", true);
            VerifyVisible(ui, hudCanvases);
            report.AppendLine("PASS hide/reopen retains the dialogue sorting policy");
            Call(dialogue, "SetVisible", false);
            Require(sourceHash == Hash(sourcePath) && hudHash == Hash(HudPath)
                && sourceDirty == EditorUtility.IsDirty(source), "Authored prefab data must remain unchanged");
            report.AppendLine("PASS authored dialogue and HUD prefab bytes unchanged; no scenes or assets saved");
            report.AppendLine("RESULT: PASS");
        }
        catch (Exception error)
        {
            report.AppendLine("RESULT: FAIL\n" + error);
            Debug.LogException(error);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
            File.WriteAllText(Folder + "/result.txt", report.ToString());
        }
    }

    private static void VerifyVisible(GameObject ui, Canvas[] hudCanvases)
    {
        Require(ui.activeSelf, "Dialogue must be active when shown");
        var canvases = ui.GetComponentsInChildren<Canvas>(true);
        Require(canvases.Length > 0, "Shared dialogue must have a canvas");
        foreach (var canvas in canvases)
        {
            Require(canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.overrideSorting
                && canvas.sortingLayerID == 0 && canvas.sortingOrder == 32050,
                "Visible shared dialogue must receive runtime presentation sorting");
            foreach (var hudCanvas in hudCanvases)
                Require(canvas.sortingLayerID == hudCanvas.sortingLayerID
                    && canvas.sortingOrder > hudCanvas.sortingOrder,
                    "Dialogue must render above the actual skill HUD canvas");
            Require(canvas.sortingOrder < short.MaxValue, "Scene fade must remain above dialogue");
        }
    }

    private static string Hash(string path)
    {
        using var sha = SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
    }
    private static void Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, Private).Invoke(target, args);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
