using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Only future expression files in this dedicated folder use this default import preset.
public sealed class DialogueExpressionImporter : AssetPostprocessor
{
    private const string LibraryPath = "Assets/Resources/Dialogue/Portraits.asset";
    private static readonly string[] Folders = { "Assets/Resources/Dialogue/Expressions", "Assets/!Pictures" };
    private static bool queued;

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Resources/Dialogue/Expressions/", System.StringComparison.Ordinal)) return;
        var importer = (TextureImporter)assetImporter;
        if (!importer.importSettingsMissing) return;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
    }

    [InitializeOnLoadMethod]
    private static void QueueConnections()
    {
        if (queued) return;
        queued = true;
        EditorApplication.delayCall += ConnectImportedExpressions;
    }

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (imported.Concat(moved).Any(path => path == LibraryPath || Folders.Any(folder => path.StartsWith(folder + "/", StringComparison.Ordinal))))
            QueueConnections();
    }

    private static void ConnectImportedExpressions()
    {
        queued = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            QueueConnections();
            return;
        }
        var library = AssetDatabase.LoadAssetAtPath<DialoguePortraitLibrary>(LibraryPath);
        if (library == null) return;
        bool changed = false;
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", Folders))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string filename = Path.GetFileNameWithoutExtension(path);
            bool ethan = filename.StartsWith("이든_", StringComparison.Ordinal);
            bool selena = filename.StartsWith("셀레나_", StringComparison.Ordinal);
            if (!ethan && !selena) continue;
            string expression = filename.Substring(ethan ? "이든_".Length : "셀레나_".Length).Trim();
            if (expression.Length == 0 || expression == "두상" || expression == "기본 표정") continue;
            // Preserve the authored Multiple/Single mode and slice IDs. Never reimport to create a connection.
            var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToArray();
            if (sprites.Length != 1) continue;
            var entries = new List<DialoguePortraitLibrary.Expression>(
                (ethan ? library.ethanExpressions : library.selenaExpressions) ?? Array.Empty<DialoguePortraitLibrary.Expression>());
            var entry = entries.FirstOrDefault(value => value != null && value.표정 == expression);
            if (entry?.image != null) continue; // An explicit Inspector assignment always takes priority.
            if (entry == null)
            {
                entry = new DialoguePortraitLibrary.Expression { 표정 = expression };
                entries.Add(entry);
            }
            entry.image = sprites[0];
            if (ethan) library.ethanExpressions = entries.ToArray();
            else library.selenaExpressions = entries.ToArray();
            changed = true;
        }
        if (!changed) return;
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssetIfDirty(library);
    }
}
