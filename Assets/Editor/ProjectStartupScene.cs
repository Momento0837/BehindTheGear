using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Always enters Play mode through the first story map, regardless of the open scene.</summary>
[InitializeOnLoad]
public static class ProjectStartupScene
{
    private const string FirstMapPath = "Assets/Scenes/Main/Forest.unity";

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
