using System;
using System.IO;
using System.Linq;
using BehindTheGear.HideOut;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

// BedScene authoring. Explicit menu item only; nothing runs on scene open.
//  - Creates Assets/Scenes/HideOutSystemScene/BedScene/BedScene.unity (first run) with the bedroom controller,
//    a camera, an input context and the spot where the hidden, frozen player is parked.
//  - Re-running it keeps the scene GUID and only refreshes references and the backdrop art.
//  - BedScene/Art/BedroomBackdrop.png is imported as a point-filtered sprite (pixel art).
// The screen UI itself is built at runtime by BedRoomScene. Registers the scene in Build Settings.
public static class BedRoomSetup
{
    private const string Folder = "Assets/Scenes/HideOutSystemScene/BedScene";
    private const string ArtPath = Folder + "/Art/BedroomBackdrop.png";
    private const string SettingsPath = "Assets/Scenes/HideOutSystemScene/Data/HideOutPrototype.asset";
    private const string PlayerPrefabPath = "Assets/Player/SavePlayerRig.prefab";

    [MenuItem("Tools/HideOut/Create Bed room (explicit)")]
    public static void Create()
    {
        if (Application.unityVersion != "6000.3.23f1" || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Requires edit mode in Unity 6000.3.23f1.");
        if (!AssetDatabase.IsValidFolder(Folder)) throw new InvalidOperationException("Folder not found: " + Folder);
        var settings = AssetDatabase.LoadAssetAtPath<HideOutSettings>(SettingsPath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (settings == null || settings.font == null) throw new InvalidOperationException("HideOutSettings / font not found: " + SettingsPath);
        if (prefab == null) throw new InvalidOperationException("Player rig not found: " + PlayerPrefabPath);
        var backdrop = ImportBackdrop();

        string path = BedRoomScene.ScenePath;
        var existing = SceneManager.GetSceneByPath(path);
        if (existing.IsValid() && existing.isDirty) throw new InvalidOperationException("BedScene has unsaved edits; save or revert them first.");
        var previous = SceneManager.GetActiveScene();
        bool created = !File.Exists(path);
        bool opened = !existing.IsValid();
        var scene = existing.IsValid() ? existing
            : created ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive)
            : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var room = scene.GetRootGameObjects().Select(x => x.GetComponent<BedRoomScene>()).SingleOrDefault(x => x != null);
            if (room == null) room = new GameObject("Bed Room").AddComponent<BedRoomScene>(); // Root object: HideOutTravel looks at roots.
            room.settings = settings;
            room.playerPrefab = prefab;
            room.backdrop = backdrop;

            if (room.roomCamera == null)
            {
                var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                cameraObject.tag = "MainCamera";
                cameraObject.transform.SetParent(room.transform, false);
                room.roomCamera = cameraObject.GetComponent<Camera>();
            }
            var camera = room.roomCamera;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.orthographic = true; camera.orthographicSize = 5.2f; // Same framing as TreatmentScene.
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.045f, .055f, .065f);

            if (room.inputContext == null)
            {
                var events = new GameObject("Bed input context", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(room.transform, false);
                room.inputContext = events;
            }
            if (room.bedSpot == null)
            {
                room.bedSpot = new GameObject("Bed Spot").transform;
                room.bedSpot.SetParent(room.transform, false);
                room.bedSpot.gameObject.AddComponent<SceneSpawnPoint2D>();
            }
            room.bedSpot.position = new Vector3(0f, -.85f, 0f);

            EditorUtility.SetDirty(room);
            CheckReferences(scene, room);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, path)) throw new InvalidOperationException("Could not save " + path);

            var list = EditorBuildSettings.scenes.ToList();
            if (!list.Any(x => x.path == path)) list.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
            AssetDatabase.SaveAssets(); // Persist the Build Settings scene list to disk.
            Debug.Log($"[BedRoomSetup] BedScene {(created ? "created" : "refreshed")} and registered in Build Settings.");
        }
        finally
        {
            if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    // Pixel-art backdrop: Sprite, point filter, no compression / mipmaps so the 320x180 art scales cleanly.
    private static Sprite ImportBackdrop()
    {
        if (AssetImporter.GetAtPath(ArtPath) is not TextureImporter importer)
        {
            Debug.LogWarning($"[BedRoomSetup] {ArtPath} is missing; the screen uses a flat wall color.");
            return null;
        }
        bool changed = importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single
            || importer.filterMode != FilterMode.Point || importer.mipmapEnabled
            || importer.textureCompression != TextureImporterCompression.Uncompressed || !Mathf.Approximately(importer.spritePixelsPerUnit, 100f);
        if (changed)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.spritePixelsPerUnit = 100f;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath);
    }

    private static void CheckReferences(Scene scene, BedRoomScene room)
    {
        if (room.settings == null || room.playerPrefab == null || room.roomCamera == null || room.inputContext == null || room.bedSpot == null)
            throw new InvalidOperationException("Missing bed room references.");
        var roots = scene.GetRootGameObjects();
        if (roots.SelectMany(x => x.GetComponentsInChildren<PlayerInventory>(true)).Any())
            throw new InvalidOperationException("A pre-spawned player must not be in BedScene.");
        if (roots.SelectMany(x => x.GetComponentsInChildren<Camera>(true)).Count() != 1)
            throw new InvalidOperationException("Bed room must have exactly one camera.");
        if (roots.SelectMany(x => x.GetComponentsInChildren<EventSystem>(true)).Count() != 1)
            throw new InvalidOperationException("Bed room must have exactly one input context.");
    }
}
