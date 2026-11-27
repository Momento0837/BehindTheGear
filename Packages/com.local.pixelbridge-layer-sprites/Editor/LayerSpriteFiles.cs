using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Experimental;
using UnityEngine;

namespace PixelBridge.LayerSprites
{
    internal sealed class LayerSpriteFiles : AssetPostprocessor
    {
        internal const string ArtifactName = "layerpngs";
        private static readonly Dictionary<string, bool> Pending = new Dictionary<string, bool>();

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (string path in imported.Concat(moved))
                if (path.EndsWith(".pblayers", StringComparison.OrdinalIgnoreCase)) Queue(path);
        }

        internal static void Queue(string link, bool selectFolder = false)
        {
            if (Pending.Count == 0) EditorApplication.delayCall += Flush;
            Pending.TryGetValue(link, out bool alreadySelected);
            Pending[link] = selectFolder || alreadySelected;
        }

        private static void Flush()
        {
            var batch = Pending.ToArray();
            Pending.Clear();
            foreach (var item in batch)
            {
                try
                {
                    var importer = AssetImporter.GetAtPath(item.Key) as LayerSpriteImporter;
                    if (importer == null) continue;
                    string folder = Sync(importer);
                    if (item.Value && folder != null)
                    {
                        Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(folder);
                        EditorGUIUtility.PingObject(Selection.activeObject);
                    }
                }
                catch (Exception error) { Debug.LogError($"Pixel Bridge Layer Sprites ({item.Key}) — {error.Message}"); }
            }
        }

        private static string Sync(LayerSpriteImporter link)
        {
            string guid = AssetDatabase.AssetPathToGUID(link.assetPath);
            var artifact = AssetDatabaseExperimental.LookupArtifact(new ArtifactKey(new GUID(guid)));
            if (!artifact.isValid) return null;
            AssetDatabaseExperimental.GetArtifactPaths(artifact, out string[] paths);
            string archive = paths?.FirstOrDefault(p => p.EndsWith("." + ArtifactName, StringComparison.Ordinal));
            if (archive == null) return null; // The source import failed; preserve the last successful export.

            using (var zip = ZipFile.OpenRead(archive))
            using (var reader = new StreamReader(zip.GetEntry("layers.txt").Open()))
            {
                string folder = OutputFolder(link.assetPath, guid);
                string owner = "PixelBridge.LayerSprites/" + guid + "/";
                var existing = new Dictionary<string, string>();
                foreach (string file in Directory.GetFiles(folder, "*.png"))
                {
                    string path = file.Replace('\\', '/');
                    string marker = AssetImporter.GetAtPath(path)?.userData;
                    if (marker != null && marker.StartsWith(owner, StringComparison.Ordinal)) existing.Add(marker, path);
                }

                int index = 0;
                string row;
                while ((row = reader.ReadLine()) != null)
                {
                    string[] fields = row.Split('\t');
                    if (fields.Length != 2) throw new InvalidDataException("레이어 목록 형식이 잘못되었습니다.");
                    string marker = owner + fields[0];
                    string name = LayerNameEncoding.DecodeUtf8Hex(fields[1]);
                    string entry = (++index).ToString(CultureInfo.InvariantCulture) + ".png";
                    using (var png = zip.GetEntry(entry).Open())
                    {
                        bool managed = existing.TryGetValue(marker, out string path);
                        if (!managed)
                            path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + FileStem(name) + ".png");
                        WriteSprite(path, marker, png, link.pixelsPerUnit, managed);
                    }
                    existing.Remove(marker);
                }
                // Only this link's tagged outputs can be removed, after all current PNGs are imported.
                foreach (string stale in existing.Values)
                    if (!AssetDatabase.DeleteAsset(stale)) throw new IOException("숨겨진 레이어 파일을 정리하지 못했습니다. " + stale);
                return folder;
            }
        }

        private static string OutputFolder(string link, string guid)
        {
            string label = "PixelBridgeLayers_" + guid;
            string owned = AssetDatabase.FindAssets("l:" + label, new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(AssetDatabase.IsValidFolder);
            if (owned != null) return owned;
            string desired = link.Substring(0, link.Length - ".pblayers".Length) + ".sprites";
            string path = AssetDatabase.GenerateUniqueAssetPath(desired);
            string id = AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
            string folder = AssetDatabase.GUIDToAssetPath(id);
            if (string.IsNullOrEmpty(folder)) throw new IOException("레이어 폴더를 만들 수 없습니다. " + path);
            AssetDatabase.SetLabels(AssetDatabase.LoadMainAssetAtPath(folder), new[] { label });
            return folder;
        }

        private static string FileStem(string name)
        {
            string stem = Regex.Replace(name.Replace(" / ", "_"), @"[\x00-\x1f<>:""/\\|?*]", "_");
            if (stem.Length > 80) stem = stem.Substring(0, char.IsHighSurrogate(stem[79]) ? 79 : 80);
            stem = stem.Trim(' ', '.');
            if (stem.Length == 0) return "Layer";
            if (Regex.IsMatch(stem, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", RegexOptions.IgnoreCase))
                stem = "_" + stem;
            return stem;
        }

        private static void WriteSprite(string path, string marker, Stream source, float ppu, bool managed)
        {
            byte[] bytes;
            using (var buffer = new MemoryStream()) { source.CopyTo(buffer); bytes = buffer.ToArray(); }
            bool exists = File.Exists(path);
            if (!managed && (exists || File.Exists(path + ".meta")))
                throw new IOException("같은 경로에 다른 파일이 있습니다. " + path);
            if (!exists || !File.ReadAllBytes(path).SequenceEqual(bytes))
            {
                // Publish by same-directory rename. A failed write leaves the previous PNG and .meta intact.
                string temp = path + "." + Guid.NewGuid().ToString("N") + "~";
                try
                {
                    File.WriteAllBytes(temp, bytes);
                    if (exists) File.Replace(temp, path, null);
                    else File.Move(temp, path);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            var texture = AssetImporter.GetAtPath(path) as TextureImporter;
            if (texture == null) throw new IOException("TextureImporter를 찾을 수 없습니다. " + path);
            // Existing PNGs belong to the artist: updating their pixels must not reset PPU,
            // filtering, compression, pivot, slicing, platform overrides, or any other .meta settings.
            // The link's PPU/default import settings apply only when creating a new layer PNG.
            if (managed && exists) return;
            var settings = new TextureImporterSettings();
            texture.ReadTextureSettings(settings);
            if (texture.userData == marker && texture.textureType == TextureImporterType.Sprite &&
                texture.spriteImportMode == SpriteImportMode.Single && texture.spritePixelsPerUnit == ppu &&
                texture.filterMode == FilterMode.Point && !texture.mipmapEnabled &&
                texture.textureCompression == TextureImporterCompression.Uncompressed &&
                texture.maxTextureSize == 16384 && settings.spriteAlignment == (int)SpriteAlignment.Center &&
                settings.spriteMeshType == SpriteMeshType.FullRect) return;

            texture.textureType = TextureImporterType.Sprite;
            texture.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            texture.SetTextureSettings(settings);
            texture.spriteImportMode = SpriteImportMode.Single;
            texture.spritePixelsPerUnit = ppu;
            texture.filterMode = FilterMode.Point;
            texture.mipmapEnabled = false;
            texture.textureCompression = TextureImporterCompression.Uncompressed;
            texture.maxTextureSize = 16384;
            texture.userData = marker;
            texture.SaveAndReimport();
        }
    }
}
