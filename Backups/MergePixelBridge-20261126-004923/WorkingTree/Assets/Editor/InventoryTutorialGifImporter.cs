#if UNITY_EDITOR_WIN
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed class InventoryTutorialGifImporter : AssetPostprocessor
{
    const string Source = "Assets/!UI/Other/Inventory_Tutorial.gif";
    const string Output = "Assets/Resources/ControlHints/Inventory_Tutorial";
    static bool queued;
    [Serializable] class Data { public int width, height, columns; public float[] durations; public Vector2 pivot; }
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (queued || Array.IndexOf(imported, Source) < 0) return;
        queued = true;
        EditorApplication.delayCall += () => { queued = false; Rebuild(); };
    }
    [MenuItem("Tools/Behind The Gear/Rebuild Inventory Tutorial GIF")]
    public static void Rebuild()
    {
        if (Application.unityVersion != "6000.3.23f1") throw new InvalidOperationException("Use Unity 6000.3.23f1.");
        if (EditorApplication.isPlayingOrWillChangePlaymode) { UnityEngine.Debug.LogWarning("Exit Play mode before rebaking Inventory_Tutorial.gif."); return; }
        string backup = "Temp/CodexBackups/Inventory-GIF-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        Directory.CreateDirectory(backup);
        foreach (string suffix in new[] { ".json", "_Frames.bytes" })
            if (File.Exists(Output + suffix)) File.Copy(Output + suffix, Path.Combine(backup, Path.GetFileName(Output + suffix)));
        string Quote(string path) => "\"" + Path.GetFullPath(path) + "\"";
        var info = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + Quote("Assets/Editor/ConvertPortalTutorialGif.ps1")
            + " -Source " + Quote(Source) + " -OutputDirectory " + Quote("Assets/Resources/ControlHints") + " -Name Inventory_Tutorial")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        using (var process = Process.Start(info))
        {
            string error = process.StandardError.ReadToEnd(); process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException(error);
        }
        var data = JsonUtility.FromJson<Data>(File.ReadAllText(Output + ".json"));
        var sprite = AssetDatabase.LoadAllAssetsAtPath(Source).OfType<Sprite>().FirstOrDefault();
        // Sprite.pivot is the actual applied Sprite Editor pivot, including alignment presets.
        data.pivot = sprite != null ? new Vector2((sprite.rect.x + sprite.pivot.x) / data.width,
            (sprite.rect.y + sprite.pivot.y) / data.height) : new Vector2(.5f, .5f);
        File.WriteAllText(Output + ".json", JsonUtility.ToJson(data));
        AssetDatabase.ImportAsset(Output + ".json");
        AssetDatabase.ImportAsset(Output + "_Frames.bytes");
    }
}
#endif
