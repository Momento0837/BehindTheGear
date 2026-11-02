using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Starts production scenes from control selection while allowing approved test scenes to run directly.
/// </summary>
[InitializeOnLoad]
public static class ProjectStartupScene
{
    private const string StartupPath = "Assets/Scenes/Controls/ControlSchemeSelect.unity";
    private const string InventoryTestPath = "Assets/Scenes/Inventory/InventoryTestScene.unity";
    private const string UiTestPath = "Assets/UI/UITest.unity";
    private const string HideOutTestPath = "Assets/Scenes/HideOutSystemScene/HideOutSystemScene.unity";

    // Temporary direct-play switch for HideOut development.
    // Set to false to use control selection for HideOut development too.
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
        // The inventory sandbox must start from the open scene. All production
        // scenes enter Play mode through the control selection screen.
        string activeScenePath = EditorSceneManager.GetActiveScene().path;

        if (activeScenePath == InventoryTestPath
            || activeScenePath == UiTestPath
            || (AllowHideOutDirectPlay && activeScenePath == HideOutTestPath))
        {
            EditorSceneManager.playModeStartScene = null;
            return true;
        }

        SceneAsset startupScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(StartupPath);
        if (startupScene == null)
        {
            Debug.LogError($"[Startup] Control selection scene is missing: {StartupPath}");
            return false;
        }

        EditorSceneManager.playModeStartScene = startupScene;
        return true;
    }
}
