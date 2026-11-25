using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
[InitializeOnLoad]
public static class V005SelenaBuildRequest
{
    static V005SelenaBuildRequest() => EditorApplication.update += Poll;
    static void Poll()
    {
        const string root = "Temp/V005SelenaBuild/";
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(root + "build")) return;
        File.Delete(root + "build");
        string previous = Environment.GetEnvironmentVariable("BTG_BUILD_OUTPUT");
        try
        {
            if (Application.unityVersion != "6000.3.23f1") throw new Exception("Wrong Unity version");
            string backup = root + "SceneBackup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isDirty) continue;
                if (string.IsNullOrEmpty(scene.path)) throw new Exception("Unsaved unnamed scene: " + scene.name);
                string diskBackup = Path.Combine(backup, scene.path);
                Directory.CreateDirectory(Path.GetDirectoryName(diskBackup));
                File.Copy(scene.path, diskBackup, false);
                if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Could not save " + scene.path);
            }
            Environment.SetEnvironmentVariable("BTG_BUILD_OUTPUT", Path.GetFullPath(root + "v0.0.5"));
            WindowsStoryBuild.Build();
        }
        catch (Exception e) { File.WriteAllText(root + "request-error.txt", e.ToString()); Debug.LogException(e); }
        finally { Environment.SetEnvironmentVariable("BTG_BUILD_OUTPUT", previous); }
    }
}