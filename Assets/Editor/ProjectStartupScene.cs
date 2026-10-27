using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Enters Play mode through the first story map except for approved test scenes.</summary>
[InitializeOnLoad]
public static class ProjectStartupScene
{
    private const string FirstMapPath = "Assets/Scenes/Main/Forest.unity";
    private const string InventoryTestPath = "Assets/Scenes/Inventory/InventoryTestScene.unity";
    private const string HideOutTestPath = "Assets/Scenes/HideOutSystemScene/HideOutSystemScene.unity";

    // Temporary direct-play switch for HideOut development. Set to false to
    // restore the original behavior that always starts production scenes in Forest.
    private const bool AllowHideOutDirectPlay = true;

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
        string activeScenePath = EditorSceneManager.GetActiveScene().path;
        if (activeScenePath == InventoryTestPath
            || AllowHideOutDirectPlay && activeScenePath == HideOutTestPath)
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
