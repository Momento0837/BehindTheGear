using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Creates only this skill's new assets; does not edit players, scenes, packages or existing weapon assets.</summary>
[InitializeOnLoad]
public static class PoisonDaggerAssetSetup
{
    public const string Folder = "Assets/Resources/PlayerSkills/";
    public const string SettingsPath = Folder + "PoisonDaggerSettings.asset";
    public const string PrefabPath = Folder + "PoisonDaggerProjectile.prefab";
    private const string Request = "Temp/PoisonDaggerValidation/assets-request.flag";
    static PoisonDaggerAssetSetup() => EditorApplication.update += Tick;
    private static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        File.Delete(Request);
        try { Build(); File.WriteAllText("Temp/PoisonDaggerValidation/assets-result.txt", "PASS Unity " + Application.unityVersion); }
        catch (Exception error) { File.WriteAllText("Temp/PoisonDaggerValidation/assets-result.txt", "FAIL " + error); }
    }
    [MenuItem("Tools/Player/Create Poison Dagger Assets")]
    public static void Build()
    {
        if (Application.unityVersion != "6000.3.23f1") throw new InvalidOperationException("Unity 6000.3.23f1 required.");
        var settings = AssetDatabase.LoadAssetAtPath<PoisonDaggerSettings>(SettingsPath);
        var existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (settings != null && settings.daggerSprite != null && settings.projectilePrefab != null && existingPrefab != null) return;
        string imagePath = Folder + "PoisonDagger.png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(imagePath);
        if (importer == null) { AssetDatabase.ImportAsset(imagePath); importer = (TextureImporter)AssetImporter.GetAtPath(imagePath); }
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.isReadable = true; importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point; importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.npotScale = TextureImporterNPOTScale.None; importer.alphaIsTransparency = true;
        importer.spritePixelsPerUnit = 1000f;
        var textureSettings = new TextureImporterSettings(); importer.ReadTextureSettings(textureSettings);
        textureSettings.spriteMeshType = SpriteMeshType.Tight; textureSettings.spriteGenerateFallbackPhysicsShape = true;
        importer.SetTextureSettings(textureSettings);
        importer.SaveAndReimport();
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(imagePath);
        var pixels = texture.GetPixels32();
        int minX = texture.width, minY = texture.height, maxX = -1, maxY = -1;
        for (int y = 0; y < texture.height; y++) for (int x = 0; x < texture.width; x++)
            if (pixels[y * texture.width + x].a > 8)
            { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
        if (maxX < minX) throw new InvalidDataException("Dagger image contains no visible pixels.");
        var factories = new SpriteDataProviderFactories(); factories.Init();
        var provider = factories.GetSpriteEditorDataProviderFromObject(importer); provider.InitSpriteEditorDataProvider();
        var id = GUID.Generate();
        provider.SetSpriteRects(new[] { new SpriteRect { name = "PoisonDagger", alignment = SpriteAlignment.Center,
            pivot = Vector2.one * .5f, rect = new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1), spriteID = id } });
        provider.GetDataProvider<ISpriteNameFileIdDataProvider>()?.SetNameFileIdPairs(new[] { new SpriteNameFileIdPair("PoisonDagger", id) });
        provider.Apply();
        importer.SaveAndReimport();
        var sprite = AssetDatabase.LoadAllAssetsAtPath(imagePath).OfType<Sprite>().Single();
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<PoisonDaggerSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
        }
        settings.daggerSprite = sprite;
        if (existingPrefab == null)
        {
            var root = new GameObject("Poison Dagger Projectile");
            try
            {
                var collider = root.AddComponent<CapsuleCollider2D>(); collider.direction = CapsuleDirection2D.Horizontal;
                collider.size = new Vector2(settings.daggerLength, settings.hitboxWidth); collider.isTrigger = true;
                var body = root.AddComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Kinematic;
                body.gravityScale = 0; body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                root.AddComponent<PlayerPoisonDaggerProjectile>();
                var visual = new GameObject("Visual", typeof(SpriteRenderer)); visual.transform.SetParent(root.transform, false);
                visual.transform.localRotation = Quaternion.Euler(0, 0, -settings.spriteForwardAngle);
                visual.transform.localScale = Vector3.one * (settings.daggerLength / sprite.bounds.size.magnitude);
                var renderer = visual.GetComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.sortingOrder = 5;
                existingPrefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
        settings.projectilePrefab = existingPrefab.GetComponent<PlayerPoisonDaggerProjectile>();
        EditorUtility.SetDirty(settings); AssetDatabase.SaveAssetIfDirty(settings);
    }
}
