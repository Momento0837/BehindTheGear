using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PixelBridge.LayerSprites
{
    [ScriptedImporter(1, "pblayers")]
    public sealed class LayerSpriteImporter : ScriptedImporter
    {
        private const string ScriptGuid = "5faab248ec39495cbb01a152c0f0ece0";
        [Min(0.01f)] public float pixelsPerUnit = 100;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            string output = Path.Combine(Path.GetTempPath(), "PixelBridgeLayers-" + Guid.NewGuid().ToString("N"));
            var pending = new List<KeyValuePair<string, Object>>();
            try
            {
                if (!(pixelsPerUnit > 0) || float.IsInfinity(pixelsPerUnit))
                    throw new InvalidDataException("Pixels Per Unit에 0보다 큰 값을 입력하세요.");
                string guid = File.ReadAllText(ctx.assetPath).Trim();
                if (!Guid.TryParseExact(guid, "N", out _)) throw new InvalidDataException("원본 GUID가 올바르지 않습니다.");
                ctx.DependsOnSourceAsset(new GUID(guid));
                string source = AssetDatabase.GUIDToAssetPath(guid);
                if (!File.Exists(source)) throw new FileNotFoundException("연결된 Aseprite 원본을 찾을 수 없습니다.");

                string scriptAsset = AssetDatabase.GUIDToAssetPath(ScriptGuid);
                if (string.IsNullOrEmpty(scriptAsset)) throw new FileNotFoundException("ExportVisibleLayers.lua를 찾을 수 없습니다.");
                ctx.DependsOnSourceAsset(scriptAsset);
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(scriptAsset);
                string script = package == null ? Path.GetFullPath(scriptAsset) :
                    Path.Combine(package.resolvedPath, scriptAsset.Substring(package.assetPath.Length + 1));
                Directory.CreateDirectory(output);
                AsepriteCli.Export(source, script, output);

                string[] layers = File.ReadAllLines(Path.Combine(output, "layers.txt"));
                var names = new List<string>();
                for (int i = 0; i < layers.Length; i++)
                {
                    string[] fields = layers[i].Split('\t');
                    if (fields.Length != 2) throw new InvalidDataException("레이어 목록을 읽을 수 없습니다.");
                    string name = LayerNameEncoding.DecodeUtf8Hex(fields[1]);
                    string png = Path.Combine(output, (i + 1).ToString(CultureInfo.InvariantCulture) + ".png");
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                    {
                        name = name + " (Texture)", filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideInHierarchy
                    };
                    pending.Add(new KeyValuePair<string, Object>("texture" + fields[0], texture));
                    if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(png), true))
                        throw new InvalidDataException("PNG를 읽을 수 없습니다. " + name);
                    var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f), pixelsPerUnit, 0, SpriteMeshType.FullRect);
                    sprite.name = name;
                    pending.Add(new KeyValuePair<string, Object>("sprite" + fields[0], sprite));
                    names.Add(name);
                }

                // A persistent main object also represents a document with all eyes turned off.
                var index = new TextAsset(string.Join("\n", names)) { name = Path.GetFileName(source) };
                pending.Add(new KeyValuePair<string, Object>("layers", index));
                foreach (var item in pending) ctx.AddObjectToAsset(item.Key, item.Value);
                ctx.SetMainObject(index);
                pending.Clear();
            }
            catch (Exception error)
            {
                ctx.LogImportError("Pixel Bridge Layer Sprites — " + error.Message);
            }
            finally
            {
                for (int i = pending.Count - 1; i >= 0; i--) Object.DestroyImmediate(pending[i].Value);
                try { if (Directory.Exists(output)) Directory.Delete(output, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
