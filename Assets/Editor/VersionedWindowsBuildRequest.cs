#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Explicit request file only: reuse the running editor without opening another project instance.
[InitializeOnLoad]
public static class VersionedWindowsBuildRequest
{
    private const string Folder = "Logs/VersionedWindowsBuild";
    [Serializable] private sealed class Request { public string version; public string output; }
    static VersionedWindowsBuildRequest() => EditorApplication.update += Poll;
    private static void Poll()
    {
        string refreshPath = Folder + "/refresh.request";
        if (File.Exists(refreshPath) && !EditorApplication.isCompiling && !EditorApplication.isUpdating
            && !EditorApplication.isPlayingOrWillChangePlaymode && !BuildPipeline.isBuildingPlayer)
        {
            File.Delete(refreshPath);
            AssetDatabase.Refresh();
            return;
        }
        string requestPath = Folder + "/request.json";
        if (!File.Exists(requestPath) || EditorApplication.isCompiling || EditorApplication.isUpdating
            || BuildPipeline.isBuildingPlayer) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.WriteAllText(Folder + "/status.txt", "WAITING: stopping Play mode before building");
            EditorApplication.isPlaying = false;
            return;
        }
        string previousOutput = Environment.GetEnvironmentVariable("BTG_BUILD_OUTPUT");
        try
        {
            var request = JsonUtility.FromJson<Request>(File.ReadAllText(requestPath));
            File.Delete(requestPath);
            if (Application.unityVersion != "6000.3.23f1") throw new Exception("Requires Unity 6000.3.23f1");
            if (request == null || !Version.TryParse(request.version, out _)) throw new Exception("Invalid build version");
            string expected = Path.GetFullPath(Path.Combine("..", "Builds", "v" + request.version));
            string output = Path.GetFullPath(request.output);
            if (!string.Equals(output, expected, StringComparison.OrdinalIgnoreCase)) throw new Exception("Unexpected output folder");
            if (Directory.Exists(output) && Directory.GetFileSystemEntries(output).Length != 0)
                throw new Exception("Build output already contains files: " + output);

            string backup = "Backups/BuildV" + request.version.Replace(".", "") + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            BackupFile("ProjectSettings/ProjectSettings.asset", backup);
            BackupFile("ProjectSettings/EditorBuildSettings.asset", backup);
            var dirtyAssets = new HashSet<string>();
            foreach (var asset in Resources.FindObjectsOfTypeAll<UnityEngine.Object>())
            {
                if (asset == null || !EditorUtility.IsPersistent(asset) || !EditorUtility.IsDirty(asset)) continue;
                string path = AssetDatabase.GetAssetPath(asset);
                if (path.StartsWith("Assets/", StringComparison.Ordinal) && File.Exists(path)) dirtyAssets.Add(path);
            }
            foreach (string path in dirtyAssets) { BackupFile(path, backup); BackupFile(path + ".meta", backup); }
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || !scene.isDirty) continue;
                if (string.IsNullOrEmpty(scene.path)) throw new Exception("Unnamed scene must be saved before build: " + scene.name);
                BackupFile(scene.path, backup);
                if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Could not save scene: " + scene.path);
            }
            PlayerSettings.bundleVersion = request.version;
            AssetDatabase.SaveAssets();
            File.WriteAllText(Folder + "/status.txt", "BUILDING\n" + output + "\nVersion: " + request.version + "\nBackup: " + backup);
            Environment.SetEnvironmentVariable("BTG_BUILD_OUTPUT", output);
            WindowsStoryBuild.Build();
            string reportPath = Path.Combine(output, "build-report.txt");
            string report = File.Exists(reportPath) ? File.ReadAllText(reportPath)
                : File.ReadAllText("Temp/WindowsStoryBuild/result.txt");
            if (!report.StartsWith("Succeeded", StringComparison.Ordinal)) throw new Exception(report);
            File.WriteAllText(Folder + "/status.txt", "SUCCEEDED\nVersion: " + request.version + "\n" + report);
        }
        catch (Exception error)
        {
            File.WriteAllText(Folder + "/status.txt", "FAILED\n" + error);
            Debug.LogException(error);
        }
        finally { Environment.SetEnvironmentVariable("BTG_BUILD_OUTPUT", previousOutput); }
    }
    private static void BackupFile(string source, string backup)
    {
        if (!File.Exists(source)) return;
        string destination = Path.Combine(backup, source);
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        if (!File.Exists(destination)) File.Copy(source, destination, false);
    }
}
#endif
