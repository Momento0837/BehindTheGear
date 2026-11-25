using UnityEditor;
using UnityEngine;

// Only future expression files in this dedicated folder use this default import preset.
public sealed class DialogueExpressionImporter : AssetPostprocessor
{
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
}
