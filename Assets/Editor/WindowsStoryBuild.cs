using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Builds control selection followed by the two story scenes.</summary>
public static class WindowsStoryBuild
{
    [MenuItem("Tools/Build/Windows - Forest and Market")]
    public static void Build()
    {
        const string requiredVersion = "6000.3.23f1";
        string resultDirectory = Path.GetFullPath("Temp/WindowsStoryBuild");
        Directory.CreateDirectory(resultDirectory);
        string resultPath = Path.Combine(resultDirectory, "result.txt");
        try
        {
            if (Application.unityVersion != requiredVersion)
                throw new InvalidOperationException("Build requires Unity " + requiredVersion);
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before building.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new OperationCanceledException("Scene save was cancelled.");

            string folder = Path.GetFullPath("Builds/Windows/BehindTheGear-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(folder);
            string[] scenes = { "Assets/Scenes/Controls/ControlSchemeSelect.unity", "Assets/Scenes/Main/Forest.unity", "Assets/Scenes/Main/Market.unity" };
            File.WriteAllText(resultPath, "BUILDING\n" + folder);
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(folder, "BehindTheGear.exe"),
                target = BuildTarget.StandaloneWindows64,
                subtarget = (int)StandaloneBuildSubtarget.Player,
                options = BuildOptions.None
            });
            string summary = report.summary.result + "\n" + folder
                + "\nUnity: " + Application.unityVersion
                + "\nTarget: Windows x86_64"
                + "\nScenes: ControlSchemeSelect, Forest, Market (startup: ControlSchemeSelect)"
                + "\nErrors: " + report.summary.totalErrors
                + "\nWarnings: " + report.summary.totalWarnings
                + "\nBytes: " + report.summary.totalSize;
            File.WriteAllText(resultPath, summary);
            File.WriteAllText(Path.Combine(folder, "build-report.txt"), summary);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Windows build failed; see Console and " + resultPath);
            File.WriteAllText(Path.Combine(folder, "README.txt"),
                "BehindTheGear - Windows 10/11 (64-bit)\r\n"
                + "Run BehindTheGear.exe. Keep all files and BehindTheGear_Data together.\r\n"
                + "Startup: choose WASD or Arrow Keys, then enter Forest. Included scenes: ControlSchemeSelect, Forest, Market.\r\n"
                + "Press Esc to quit.\r\n");
            Debug.Log("WINDOWS_STORY_BUILD_SUCCEEDED: " + folder);
        }
        catch (Exception exception)
        {
            File.AppendAllText(resultPath, "\nFAILED: " + exception);
            Debug.LogException(exception);
        }
    }
}
