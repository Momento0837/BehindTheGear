using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Starts production scenes from Forest while allowing UI sandboxes to run directly.</summary>
[InitializeOnLoad]
public static class ProjectStartupScene
{
    private const string FirstMapPath = "Assets/Scenes/Main/Forest.unity";
    private const string InventoryTestPath = "Assets/Scenes/Inventory/InventoryTestScene.unity";
    private const string UiTestPath = "Assets/!UI/UITest.unity";

    static ProjectStartupScene()
    {
        SetStartupScene();
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode && !SetStartupScene())
            EditorApplication.isPlaying = false;
    }

    private static bool SetStartupScene()
    {
        // UI sandboxes must start from the open scene. All production
        // scenes continue to enter Play mode through the first story map.
        string activePath = EditorSceneManager.GetActiveScene().path;
        if (activePath == InventoryTestPath || activePath == UiTestPath)
        {
            EditorSceneManager.playModeStartScene = null;
            return true;
        }

        SceneAsset firstMap = AssetDatabase.LoadAssetAtPath<SceneAsset>(FirstMapPath);
        if (firstMap == null)
        {
            Debug.LogError($"[Startup] First map is missing: {FirstMapPath}");
            return false;
        }

        EditorSceneManager.playModeStartScene = firstMap;
        return true;
    }
}
