#if UNITY_EDITOR_WIN
using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

// GIFs are baked in the editor, so no decoding process is started during gameplay.
public sealed class PortalTutorialGifImporter : AssetPostprocessor
{
    const string Source = "Assets/!UI/Other/W.gif";
    const string Output = "Assets/Resources/ControlHints";
    static bool queued;
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (queued || Array.IndexOf(imported, Source) < 0) return;
        queued = true;
        EditorApplication.delayCall += () => { queued = false; Rebuild(); };
    }
    [MenuItem("Tools/Behind The Gear/Rebuild W Tutorial GIF")]
    public static void Rebuild()
    {
        if (Application.unityVersion != "6000.3.23f1") throw new InvalidOperationException("Use Unity 6000.3.23f1.");
        if (EditorApplication.isPlayingOrWillChangePlaymode) { UnityEngine.Debug.LogWarning("Exit Play mode before replacing W.gif."); return; }
        // Preserve the previous generated atlas before replacing it.
        var backup = "Temp/CodexBackups/W-GIF-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        Directory.CreateDirectory(backup);
        foreach (var name in new[] { "W.json", "W_Frames.bytes" })
            if (File.Exists(Output + "/" + name)) File.Copy(Output + "/" + name, backup + "/" + name);
        string Quote(string path) => "\"" + Path.GetFullPath(path) + "\"";
        var info = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "
            + Quote("Assets/Editor/ConvertPortalTutorialGif.ps1") + " -Source " + Quote(Source) + " -OutputDirectory " + Quote(Output))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        using (var process = Process.Start(info))
        {
            var error = process.StandardError.ReadToEnd(); process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException("W tutorial GIF conversion failed: " + error);
        }
        AssetDatabase.ImportAsset(Output + "/W.json");
        AssetDatabase.ImportAsset(Output + "/W_Frames.bytes");
        UnityEngine.Debug.Log("W tutorial GIF rebuilt. Prefab: " + Output + "/Portal_W_Tutorial.prefab");
    }
}
#endif
