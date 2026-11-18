using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Builds all enabled scenes, including HideOut and training.</summary>
public static class WindowsStoryBuild
{
    [MenuItem("Tools/Build/Windows - Full Prototype")]
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
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new OperationCanceledException("Scene save was cancelled.");

            string folder = Path.GetFullPath(Environment.GetEnvironmentVariable("BTG_BUILD_OUTPUT") ?? ("Builds/Windows/BehindTheGear-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")));
            Directory.CreateDirectory(folder);
            string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
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
                + "\nScenes: " + string.Join(", ", scenes)
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
                + "Startup: ControlSchemeSelect. All enabled story, HideOut and training scenes are included.\r\n"
                + "Hold Esc for 2 seconds to quit.\r\n");
            Debug.Log("WINDOWS_STORY_BUILD_SUCCEEDED: " + folder);
        }
        catch (Exception exception)
        {
            File.AppendAllText(resultPath, "\nFAILED: " + exception);
            Debug.LogException(exception);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
