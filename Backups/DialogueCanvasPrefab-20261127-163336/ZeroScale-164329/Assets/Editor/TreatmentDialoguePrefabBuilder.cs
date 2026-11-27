using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BehindTheGear.HideOut;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>One-time extraction of the treatment room's authored procedural dialogue.</summary>
public static class TreatmentDialoguePrefabBuilder
{
    public const string PrefabPath = "Assets/!UI/Dialogue/TreatmentDialogueCanvas.prefab";
    private const string ArtFolder = "Assets/!UI/Dialogue/Art";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    // Existing prefabs belong to the artist: never rebuild over their edits.
    public static GameObject Build()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null)
        {
            if (existing.GetComponent<TreatmentDialogueView>()?.IsValid != true)
                throw new InvalidOperationException("Existing treatment dialogue prefab has incomplete UI references: " + PrefabPath);
            return existing;
        }
        if (File.Exists(PrefabPath))
            throw new InvalidOperationException("The treatment dialogue prefab exists but could not be loaded: " + PrefabPath);
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Build treatment dialogue assets only in Edit mode.");

        EnsureFolder(ArtFolder);
        var preview = EditorSceneManager.OpenPreviewScene(TreatmentRoomScene.ScenePath);
        GameObject root = null;
        try
        {
            var room = preview.GetRootGameObjects()
                .SelectMany(go => go.GetComponentsInChildren<TreatmentRoomScene>(true)).FirstOrDefault();
            if (room == null || room.settings == null)
                throw new InvalidOperationException("The treatment scene has no configured TreatmentRoomScene.");
            room.enabled = false;

            Set(room, "chamfer", PersistPanelSprite("TreatmentPanel", true));
            Set(room, "chamferTop", PersistPanelSprite("TreatmentNameTab", false));

            root = new GameObject("TreatmentDialogueCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(TreatmentDialogueView));
            SceneManager.MoveGameObjectToScene(root, preview);
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = .5f;

            typeof(TreatmentRoomScene).GetMethod("BuildDialogue", PrivateInstance)
                .Invoke(room, new object[] { root.transform });
            var view = root.GetComponent<TreatmentDialogueView>();
            view.group = Read<CanvasGroup>(room, "dialogueGroup");
            view.body = Read<TMP_Text>(room, "bodyLabel");
            view.continueMark = Read<Image>(room, "continueMark");
            view.advance = Read<Button>(room, "tutorialAdvanceButton");
            view.speaker = view.group.transform.Find("Name tab")?.GetComponentInChildren<TMP_Text>(true);
            if (!view.IsValid) throw new InvalidOperationException("Treatment dialogue extraction lost a required UI reference.");

            // Keep the prefab visible while editing. Playback initializes visibility itself.
            view.group.alpha = 1f;
            view.advance.onClick.RemoveAllListeners();
            foreach (var image in root.GetComponentsInChildren<Image>(true))
                if (image.sprite != null && !AssetDatabase.Contains(image.sprite))
                    throw new InvalidOperationException("Unpersisted treatment UI sprite: " + image.name);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (prefab == null) throw new InvalidOperationException("Could not save " + PrefabPath);
            AssetDatabase.SaveAssets();
            return prefab;
        }
        finally
        {
            if (root != null) Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static T Read<T>(TreatmentRoomScene room, string field) where T : Object =>
        (T)typeof(TreatmentRoomScene).GetField(field, PrivateInstance).GetValue(room);

    private static void Set(TreatmentRoomScene room, string field, Object value) =>
        typeof(TreatmentRoomScene).GetField(field, PrivateInstance).SetValue(room, value);

    private static Sprite PersistPanelSprite(string name, bool bottomToo)
    {
        string path = ArtFolder + "/" + name + ".asset";
        var existing = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
        if (existing != null) return existing;
        if (File.Exists(path))
            throw new InvalidOperationException("Panel art asset exists without its Sprite subasset: " + path);

        // Use the same source generator and 9-slice border as BuildScreen, without a texture importer.
        var texture = (Texture2D)typeof(TreatmentRoomScene)
            .GetMethod("CreateChamferTexture", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { 24, 6, bottomToo });
        texture.name = name + " Texture";
        AssetDatabase.CreateAsset(texture, path);
        var sprite = Sprite.Create(texture, new Rect(0, 0, 24, 24), new Vector2(.5f, .5f),
            100f, 0, SpriteMeshType.FullRect, new Vector4(8, 8, 8, 8));
        sprite.name = name;
        AssetDatabase.AddObjectToAsset(sprite, path);
        EditorUtility.SetDirty(texture);
        EditorUtility.SetDirty(sprite);
        AssetDatabase.SaveAssets();
        return sprite;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}