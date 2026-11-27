using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class BaseStoryFogSetup
{
    private const string Folder = "Temp/BaseStoryUnlocks/";
    static BaseStoryFogSetup() => EditorApplication.update += Poll;
    private static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (Application.unityVersion != "6000.3.23f1") return;
        if (File.Exists(Folder + "setup.request") && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(Folder + "setup.request");
            try { Connect(); }
            catch (Exception error) { File.WriteAllText(Folder + "setup-result.txt", error.ToString()); Debug.LogException(error); }
        }
        if (File.Exists(Folder + "check.request") && EditorApplication.isPlaying
            && UnityEngine.Object.FindFirstObjectByType<BehindTheGear.HideOut.HideOutScene>()?.TrainingPlayer?.Initialized == true)
        {
            File.Delete(Folder + "check.request");
            BaseStoryUnlockChecks.RunChecks();
        }
    }

    private static void Connect()
    {
        const string path = "Assets/!UI/Base/Base_Test.unity";
        var open = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
        if (open.IsValid() && open.isDirty) throw new InvalidOperationException("Base_Test has unsaved changes; save it before connecting fog.");
        string original = File.ReadAllText(path);
        var fields = new StringBuilder();
        foreach (string prefix in new[] { "Bigfog", "Fog" })
        {
            fields.AppendLine(prefix == "Bigfog" ? "  bigFogSprites:" : "  smallFogSprites:");
            int count = prefix == "Bigfog" ? 3 : 4;
            for (int i = 1; i <= count; i++)
            {
                string assetPath = $"Assets/!UI/Base/Fogs/{prefix}{i}.png";
                var sprite = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Sprite>().Single();
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string guid, out long id)) throw new Exception(assetPath);
                fields.AppendLine($"  - {{fileID: {id}, guid: {guid}, type: 3}}");
            }
        }
        fields.AppendLine("  fogCoverage: 1\n  fogEdgeJitterPixels: 10\n  fogDepthRange: {x: 0.25, y: 1.5}\n  fogParallax: 0.035");
        var component = Regex.Matches(original, @"(?ms)^--- !u!114 &[^\r\n]+\r?\n.*?(?=^--- !u!|\z)")
            .Cast<Match>().Single(m => m.Value.Contains("guid: 7471869a096836e4191d0c8967272e17"));
        if (component.Value.Contains("  bigFogSprites:")) throw new InvalidOperationException("Fog references already exist; preserve authored settings.");
        string newline = original.Contains("\r\n") ? "\r\n" : "\n";
        string added = fields.ToString().Replace("\r\n", "\n").Replace("\n", newline);
        string updated = component.Value.Replace("  worldBounds:", added + "  worldBounds:");
        File.WriteAllText(path, original.Replace(component.Value, updated), new UTF8Encoding(false));
        AssetDatabase.ImportAsset(path);
        File.WriteAllText(Folder + "setup-result.txt", "PASS connected all 3 Bigfog and 4 Fog sprites to Base_Test; no texture importer changes.");
    }
}
