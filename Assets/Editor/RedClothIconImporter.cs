using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Connect the future authored image to the item without changing other UI imports.
public sealed class RedClothIconImporter : AssetPostprocessor
{
    const string ItemPath = "Assets/Resources/Story/RedCloth.asset";
    static readonly string[] ImagePaths = { "Assets/!UI/Other/Red_Cloth.png", "Assets/!UI/Others/Red_Cloth.png" };
    static bool queued;

    void OnPreprocessTexture()
    {
        if (!ImagePaths.Contains(assetPath)) return;
        var importer = (TextureImporter)assetImporter;
        if (!importer.importSettingsMissing) return;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
    }

    [InitializeOnLoadMethod]
    static void QueueConnection()
    {
        if (queued) return;
        queued = true;
        EditorApplication.delayCall += () => { queued = false; ConnectIcon(); };
    }

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (imported.Concat(moved).Any(path => path == ItemPath || ImagePaths.Contains(path))) QueueConnection();
    }

    static void ConnectIcon()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var item = AssetDatabase.LoadAssetAtPath<InventoryItemDefinition>(ItemPath);
        if (item == null) return;
        var serialized = new SerializedObject(item);
        var icon = serialized.FindProperty("icon");
        if (icon.objectReferenceValue != null) return; // Keep an explicitly assigned icon.
        var sprite = ImagePaths.Where(File.Exists).SelectMany(path => AssetDatabase.LoadAllAssetsAtPath(path)).OfType<Sprite>().FirstOrDefault();
        if (sprite == null) return;
        string backup = "Temp/RedClothIconBackup/" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        Directory.CreateDirectory(backup);
        File.Copy(ItemPath, Path.Combine(backup, "RedCloth.asset"));
        icon.objectReferenceValue = sprite;
        serialized.ApplyModifiedProperties();
        AssetDatabase.SaveAssetIfDirty(item);
    }
}
