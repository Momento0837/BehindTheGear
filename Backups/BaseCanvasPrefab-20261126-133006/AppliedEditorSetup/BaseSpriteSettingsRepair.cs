using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class BaseSpriteSettingsRepair
{
    private const string Request = "Temp/BaseCanvasPrefab/sprites.request";
    private const string Folder = "Assets/!UI/Base/아이소메트릭.aseprite.sprites";
    static BaseSpriteSettingsRepair() => EditorApplication.update += Poll;
    private static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying) return;
        File.Delete(Request);
        try { Repair(); }
        catch (Exception error) { File.WriteAllText("Temp/BaseCanvasPrefab/sprites-result.txt", error.ToString()); Debug.LogException(error); }
    }
    private static void Repair()
    {
        if (Application.unityVersion != "6000.3.23f1") throw new InvalidOperationException("Use Unity 6000.3.23f1.");
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("PixelBridge.LayerSprites.LayerSpriteFiles")).First(t => t != null);
        var sync = type.GetMethod("WriteSprite", BindingFlags.Static | BindingFlags.NonPublic);
        var report = new StringBuilder();
        int repaired = 0, preserved = 0;
        foreach (string file in Directory.GetFiles(Folder, "*.png"))
        {
            string path = file.Replace('\\', '/');
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (importer.spritePixelsPerUnit != 11f || importer.filterMode != FilterMode.Point
                || importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.spritePixelsPerUnit = 11f;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                repaired++;
            }
            string marker = importer.userData;
            if (!marker.StartsWith("PixelBridge.LayerSprites/", StringComparison.Ordinal)) throw new InvalidDataException("Unowned base sprite: " + path);
            byte[] before = File.ReadAllBytes(path + ".meta");
            // Exercise the actual exporter with the old conflicting PPU. Existing metadata must remain byte-identical.
            using (var input = new MemoryStream(File.ReadAllBytes(path))) sync.Invoke(null, new object[] { path, marker, input, 100f, true });
            if (!before.SequenceEqual(File.ReadAllBytes(path + ".meta")) || guid != AssetDatabase.AssetPathToGUID(path))
                throw new InvalidOperationException("Exporter changed authored metadata: " + path);
            var checkedImporter = (TextureImporter)AssetImporter.GetAtPath(path);
            if (checkedImporter.spritePixelsPerUnit != 11 || checkedImporter.filterMode != FilterMode.Point
                || checkedImporter.textureCompression != TextureImporterCompression.Uncompressed)
                throw new InvalidOperationException("Sprite settings changed: " + path);
            preserved++;
        }
        // New layers of this base should also start at the artist's chosen PPU.
        var link = AssetImporter.GetAtPath("Assets/!UI/Base/아이소메트릭.aseprite.pblayers");
        var serialized = new SerializedObject(link);
        serialized.FindProperty("pixelsPerUnit").floatValue = 11;
        if (serialized.ApplyModifiedPropertiesWithoutUndo()) link.SaveAndReimport();
        report.AppendLine($"PASS {preserved} base sprites: PPU 11 / Point / Uncompressed; corrected {repaired} sprites.");
        report.AppendLine($"PASS existing exporter preserves all {preserved} .meta files byte-for-byte even with conflicting PPU 100.");
        report.AppendLine("PASS existing GUIDs preserved; new base layers use PPU 11.");
        File.WriteAllText("Temp/BaseCanvasPrefab/sprites-result.txt", report.ToString());
    }
}
