using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MenuParallaxPreview
{
    [MenuItem("Tools/Behind The Gear/Use UI Background In UITest %#F6")]
    private static void Apply()
    {
        if (Application.unityVersion != "6000.3.23f1" || EditorApplication.isPlayingOrWillChangePlaymode) return;
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/!UI/UITest.unity") return;
        string directory = "Temp/MenuParallaxBackup/Live-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        Directory.CreateDirectory(directory);
        // Preserve unsaved UI edits as well as the original sprite experiment.
        if (!EditorSceneManager.SaveScene(scene, directory + "/UITest.unity", true)) return;
        int layers = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.GetComponent<SpriteRenderer>() != null && root.name.StartsWith("Main_"))
            {
                Undo.RecordObject(root, "Use UI background");
                root.SetActive(false);
            }
            if (root.GetComponent<MenuPointerParallax>() == null) continue;
            foreach (Transform child in root.transform)
            {
                if (!child.name.StartsWith("Main_")) continue;
                Undo.RecordObject(child.gameObject, "Use UI background");
                child.gameObject.SetActive(true);
                PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);
                layers++;
            }
        }
        EditorSceneManager.MarkSceneDirty(scene);
        File.WriteAllText("Temp/MenuParallaxBackup/validation.txt", "Unity " + Application.unityVersion + ": activated UI layers=" + layers + "; preserved live scene in " + directory);
        Debug.Log("MENU_PARALLAX_UI_READY layers=" + layers);
    }
}
