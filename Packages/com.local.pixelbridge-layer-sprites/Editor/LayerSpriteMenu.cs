using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PixelBridge.LayerSprites
{
    internal static class LayerSpriteMenu
    {
        private const string ImportMenu = "Assets/Pixel Bridge/Import Visible Layer Sprites";
        internal const string SetExecutableMenu = "Tools/Pixel Bridge/Layer Sprites/Select Aseprite";
        internal const string Preference = "PixelBridge.LayerSprites.AsepriteExecutable";

        [MenuItem(ImportMenu, true)]
        private static bool CanImport()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            string extension = Path.GetExtension(path);
            return path.StartsWith("Assets/", StringComparison.Ordinal) &&
                (string.Equals(extension, ".ase", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(extension, ".aseprite", StringComparison.OrdinalIgnoreCase));
        }

        [MenuItem(ImportMenu)]
        private static void Import()
        {
            string source = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (!File.Exists(AsepriteCli.FindExecutable()) && !ChooseExecutable()) return;

            string guid = AssetDatabase.AssetPathToGUID(source);
            string link = source + ".pblayers";
            if (File.Exists(link))
            {
                if (File.ReadAllText(link).Trim() != guid)
                {
                    Debug.LogError("기존 레이어 에셋이 다른 원본에 연결되어 있습니다. 기존 에셋을 이동한 뒤 다시 시도하세요. " + link);
                    return;
                }
            }
            else
            {
                File.WriteAllText(link, guid);
            }
            AssetDatabase.ImportAsset(link, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            LayerSpriteFiles.Queue(link, selectFolder: true);
        }

        [MenuItem(SetExecutableMenu)]
        private static void SelectExecutable() => ChooseExecutable();

        private static bool ChooseExecutable()
        {
            string extension = Application.platform == RuntimePlatform.WindowsEditor ? "exe" : "";
            string path = EditorUtility.OpenFilePanel("Aseprite 실행 파일 선택", "", extension);
            if (string.IsNullOrEmpty(path)) return false;
            if (path.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                path = Path.Combine(path, "Contents/MacOS/aseprite");
            if (!File.Exists(path)) return false;
            EditorPrefs.SetString(Preference, path);
            return true;
        }
    }
}
