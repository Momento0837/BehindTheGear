#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Runs isolated checks only: no scene saves, gameplay saves, or player build.
public static class GameplayFeedbackChecks
{
    public static void RunBatch()
    {
        Run();
        WorldItemPhysicsChecks.RunBatch();
    }
    public static void Run()
    {
        const string folder = "Logs/Validation/GameplayFeedback";
        Directory.CreateDirectory(folder);
        try
        {
            CheckOverlappingBaseInput();
            foreach (string path in new[] { "Dialogue/Market/market_pursuit_shout", "Dialogue/Market/market_pursuit_monologue" })
            {
                var text = Resources.Load<TextAsset>(path);
                if (text == null || ConversationDialogue.GetGreetingLines(text, int.MaxValue).Length != 1)
                    throw new Exception("Missing or invalid single-line dialogue: " + path);
            }
            if (Resources.Load<TextAsset>("Dialogue/Neighborhood/neighborhood_conversation") != null)
                throw new Exception("Removed neighborhood walking dialogue is still present.");
            File.WriteAllText(folder + "/result.txt", "PASS: compiled scripts, overlapping base input locks, and Market dialogue resources.");
            Debug.Log("[GameplayFeedbackChecks] PASS");
        }
        catch (Exception error)
        {
            File.WriteAllText(folder + "/result.txt", "FAIL: " + error);
            Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }


    private static void CheckOverlappingBaseInput()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        try
        {
            root = new GameObject("Base input check", typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var raycaster = root.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            using (var entrance = BasePresentationInputLock.Acquire(root.transform))
            using (var dialogue = BasePresentationInputLock.Acquire(root.transform))
            {
                entrance.Dispose();
                if (raycaster.enabled) throw new Exception("Base input unlocked while dialogue was still running.");
            }
            if (!raycaster.enabled) throw new Exception("Base input did not restore after both presentations.");
            using (var entrance = BasePresentationInputLock.Acquire(root.transform))
            using (var dialogue = BasePresentationInputLock.Acquire(root.transform))
            {
                dialogue.Dispose();
                if (raycaster.enabled) throw new Exception("Base input unlocked while the entrance was still running.");
            }
            raycaster.enabled = false;
            using (BasePresentationInputLock.Acquire(root.transform)) { }
            if (raycaster.enabled) throw new Exception("An intentionally disabled raycaster was re-enabled.");
        }
        finally
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
    }

}
#endif
