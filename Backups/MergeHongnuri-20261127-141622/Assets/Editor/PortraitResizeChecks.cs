using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Editor-only import regression: synthetic resized PNGs never replace the artist's images.
[InitializeOnLoad]
public static class PortraitResizeChecks
{
    private const string Folder = "Temp/PortraitResize/";
    private static readonly StringBuilder Report = new();
    static PortraitResizeChecks() => EditorApplication.update += Poll;
    private static void Poll()
    {
        if (!File.Exists(Folder + "check.request") || EditorApplication.isPlayingOrWillChangePlaymode
            || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        File.Delete(Folder + "check.request");
        Run();
    }

    [MenuItem("Tools/Dialogue/Check portrait resize persistence")]
    public static void Run()
    {
        if (Application.unityVersion != "6000.3.23f1" || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Use Unity 6000.3.23f1 in Edit mode.");
        Directory.CreateDirectory(Folder);
        Report.Clear();
        string temporary = null;
        try
        {
            var library = AssetDatabase.LoadAssetAtPath<DialoguePortraitLibrary>("Assets/Resources/Dialogue/Portraits.asset");
            string[] keys = { "기본 표정", "곤란", "의아", "짜증", "눈 돌림", "눈 반 감음", "눈 감음", "엥" };
            var resolved = keys.Select(k => library.Resolve("Ethan", k)).ToArray();
            Require(resolved.All(s => s != null) && resolved.Distinct().Count() == keys.Length,
                "all eight Ethan expressions resolve to their own sprites after the repair");
            foreach (var sprite in resolved)
                Require(sprite.rect.xMax <= sprite.texture.width && sprite.rect.yMax <= sprite.texture.height,
                    sprite.name + " stays inside the imported image");
            string source = AssetDatabase.GetAssetPath(library.Resolve("Ethan", "곤란"));
            temporary = "Assets/!Pictures/__PortraitResizeCheck_" + Guid.NewGuid().ToString("N");
            Require(!Directory.Exists(temporary), "test directory is new and isolated");
            AssetDatabase.CreateFolder("Assets/!Pictures", Path.GetFileName(temporary));
            string path = temporary + "/이든_곤란.png";
            File.Copy(source, path);
            string meta = Regex.Replace(File.ReadAllText(source + ".meta"), @"(?m)^guid: [0-9a-f]+", "guid: " + Guid.NewGuid().ToString("N"));
            File.WriteAllText(path + ".meta", meta);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var original = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Single();
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(original, out string guid, out long fileId);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            string settings = ImportSettings(importer);
            var reference = ScriptableObject.CreateInstance<DialoguePortraitLibrary>();
            reference.ethan = original;
            reference.ethanExpressions = new[] { new DialoguePortraitLibrary.Expression { 표정 = "곤란", image = original } };
            string referencePath = temporary + "/Reference.asset";
            AssetDatabase.CreateAsset(reference, referencePath);

            ReplaceFixture(path, 600, 700, Color.white);
            var replacement = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Single();
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(replacement, out string nextGuid, out long nextId);
            Require(guid == nextGuid && fileId == nextId, "resizing PNG preserves GUID and sprite local file ID");
            Require(replacement.rect == new Rect(0, 0, 600, 700), "out-of-bounds crop is repaired to the resized source image");
            Require(reference.Resolve("Ethan", "곤란") == replacement,
                "an already serialized portrait reference survives PNG replacement");
            Require(ImportSettings((TextureImporter)AssetImporter.GetAtPath(path)) == settings,
                "sprite mode, PPU, filter, compression, pivot, borders and name remain unchanged");

            ReplaceFixture(path, 400, 500, Color.red);
            replacement = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Single();
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(replacement, out nextGuid, out nextId);
            Require(guid == nextGuid && fileId == nextId && reference.Resolve("Ethan", "곤란") == replacement,
                "a second resize still preserves the same expression connection");

            importer = (TextureImporter)AssetImporter.GetAtPath(path);
            using (var serialized = new SerializedObject(importer))
            {
                var slices = serialized.FindProperty("m_SpriteSheet.m_Sprites");
                slices.GetArrayElementAtIndex(0).FindPropertyRelative("m_Rect").rectValue = new Rect(10, 20, 300, 350);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.WriteImportSettingsIfDirty(path);
            ReplaceFixture(path, 400, 500, Color.green);
            replacement = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Single();
            Require(replacement.rect == new Rect(10, 20, 300, 350), "valid custom crop survives ordinary content edits");
            Require(!DialogueExpressionImporter.RepairOutOfBoundsPortraitSlice((TextureImporter)AssetImporter.GetAtPath(path)),
                "repair is idempotent for valid portrait imports");

            using (var serialized = new SerializedObject(AssetImporter.GetAtPath(path)))
            {
                var slices = serialized.FindProperty("m_SpriteSheet.m_Sprites");
                slices.InsertArrayElementAtIndex(0);
                slices.GetArrayElementAtIndex(0).FindPropertyRelative("m_Rect").rectValue = new Rect(0, 0, 10000, 10000);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            Require(!DialogueExpressionImporter.RepairOutOfBoundsPortraitSlice((TextureImporter)AssetImporter.GetAtPath(path)),
                "multiple-sprite sheets are never automatically resized");
            Require(keys.Select(k => library.Resolve("Ethan", k)).SequenceEqual(resolved), "the real portrait library is unchanged by fixture tests");
            Report.AppendLine("PASS portrait resize regression");
        }
        catch (Exception error) { Report.AppendLine("FAIL " + error); }
        finally
        {
            if (temporary != null && AssetDatabase.IsValidFolder(temporary)) AssetDatabase.DeleteAsset(temporary);
            File.WriteAllText(Folder + "check-result.txt", Report.ToString());
        }
    }

    private static void ReplaceFixture(string path, int width, int height, Color color)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        try { texture.SetPixel(0, 0, color); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); }
        finally { Object.DestroyImmediate(texture); }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
    }

    private static string ImportSettings(TextureImporter importer)
    {
        using var serialized = new SerializedObject(importer);
        var slice = serialized.FindProperty("m_SpriteSheet.m_Sprites").GetArrayElementAtIndex(0);
        return string.Join("|", importer.spriteImportMode, importer.spritePixelsPerUnit, importer.filterMode,
            importer.textureCompression, importer.mipmapEnabled, importer.alphaIsTransparency, importer.userData,
            slice.FindPropertyRelative("m_Name").stringValue, slice.FindPropertyRelative("m_Pivot").vector2Value,
            slice.FindPropertyRelative("m_Alignment").intValue, slice.FindPropertyRelative("m_Border").vector4Value,
            slice.FindPropertyRelative("m_SpriteID").stringValue);
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Report.AppendLine("PASS " + message);
    }
}
