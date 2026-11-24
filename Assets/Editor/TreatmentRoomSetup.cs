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
using Object = UnityEngine.Object;

// TreatmentScene authoring. Explicit menu items only; nothing runs on scene open.
//  - Build:   converts the copied ScareCrowScene (first time) into the treatment room.
//  - Rebuild: strips the old walk-in room (set, HUD, F interaction, entry spot, heal pulse) from an already
//             converted scene and links the wall / lamp glow art used behind the treatment screen.
//             The screen UI itself is built at runtime by TreatmentRoomScene.
// Keeps the scene GUID, its camera and floor colliders. TreatmentScene/Art files are only referenced, not changed.
public static class TreatmentRoomSetup
{
    private static readonly string[] LegacyGroups =
    {
        "Background - replaceable", "Treatment Booth - replaceable", "Medicine shelf - replaceable",
        "Treatment Set - replaceable", "Treatment HUD", "Booth Interaction (F)", "Entry Spot",
    };

    [MenuItem("Tools/HideOut/Build Treatment room (explicit)")]
    public static void Build()
    {
        RequireEditMode();
        string path = TreatmentRoomScene.ScenePath;
        if (File.ReadAllText(path).Contains("Treatment Room"))
            throw new InvalidOperationException("Treatment room already exists; use Tools > HideOut > Rebuild Treatment room set.");
        WithScene(path, scene =>
        {
            var training = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<ScareCrowTrainingScene>(true)).SingleOrDefault();
            if (training == null) throw new InvalidOperationException("Expected the copied ScareCrow training room in TreatmentScene.");
            var room = new GameObject("Treatment Room").AddComponent<TreatmentRoomScene>();
            room.settings = training.settings; room.playerPrefab = training.playerPrefab;
            var camera = training.trainingCamera;
            var impact = camera.GetComponent<ScareCrowCameraImpact>();
            if (impact != null) Object.DestroyImmediate(impact); // Training-only hit shake.
            room.roomCamera = camera;
            var floor = training.transform.Find("Floor and safety boundaries");
            if (floor != null) floor.SetParent(room.transform, true); // Keep the Ground colliders and side/ceiling bounds.
            Object.DestroyImmediate(training.gameObject);            // Dummy, training HUD, decor, start, input context.
            var events = new GameObject("Treatment input context", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(room.transform, false); room.inputContext = events;
            BuildSet(scene, room);
        });
    }

    [MenuItem("Tools/HideOut/Rebuild Treatment room set (explicit)")]
    public static void Rebuild()
    {
        RequireEditMode();
        WithScene(TreatmentRoomScene.ScenePath, scene =>
        {
            var room = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<TreatmentRoomScene>(true)).SingleOrDefault();
            if (room == null) throw new InvalidOperationException("No Treatment Room in TreatmentScene; run Build Treatment room first.");
            BuildSet(scene, room);
        });
    }

    private static void RequireEditMode()
    {
        if (Application.unityVersion != "6000.3.23f1" || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Requires edit mode in Unity 6000.3.23f1.");
        if (!File.Exists(TreatmentRoomScene.ScenePath)) throw new InvalidOperationException("TreatmentScene not found: " + TreatmentRoomScene.ScenePath);
    }

    private static void WithScene(string path, Action<Scene> edit)
    {
        var existing = SceneManager.GetSceneByPath(path);
        if (existing.IsValid() && existing.isDirty) throw new InvalidOperationException("TreatmentScene has unsaved edits; save or revert them first.");
        var previous = SceneManager.GetActiveScene();
        bool opened = !existing.IsValid();
        var scene = opened ? EditorSceneManager.OpenScene(path, OpenSceneMode.Additive) : existing;
        try
        {
            SceneManager.SetActiveScene(scene);
            edit(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            var list = EditorBuildSettings.scenes.ToList();
            if (!list.Any(x => x.path == path)) list.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
            AssetDatabase.SaveAssets(); // Persist the Build Settings scene list to disk.
            Debug.Log("[TreatmentRoomSetup] TreatmentScene saved and registered in Build Settings.");
        }
        finally
        {
            if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void BuildSet(Scene scene, TreatmentRoomScene room)
    {
        if (room.settings == null || room.settings.font == null) throw new InvalidOperationException("Treatment Room has no HideOutSettings / font.");
        foreach (string name in LegacyGroups)
        {
            var old = room.transform.Find(name);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }
        room.legacyHud = null;
        // Backdrop art for the runtime screen (already imported as sprites; import settings are not touched).
        room.wallSprite = ArtSprite("TreatmentWall");
        room.glowSprite = ArtSprite("TreatmentGlow");

        // The (hidden, frozen) player is parked here while the screen is open.
        if (room.treatmentSpot == null)
        {
            room.treatmentSpot = new GameObject("Treatment Spot").transform;
            room.treatmentSpot.SetParent(room.transform, false);
            room.treatmentSpot.gameObject.AddComponent<SceneSpawnPoint2D>();
        }
        room.treatmentSpot.position = new Vector3(.7f, -.85f, 0f);
        var pulse = room.treatmentSpot.Find("Heal pulse - replaceable"); // Old walk-in room effect.
        if (pulse != null) Object.DestroyImmediate(pulse.gameObject);

        EditorUtility.SetDirty(room);
        CheckReferences(scene, room);
    }

    private static Sprite ArtSprite(string name)
    {
        string path = $"Assets/Scenes/HideOutSystemScene/TreatmentScene/Art/{name}.png";
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null) Debug.LogWarning($"[TreatmentRoomSetup] {path} is missing or not a sprite; the screen uses its generated fallback.");
        return sprite;
    }

    private static void CheckReferences(Scene scene, TreatmentRoomScene room)
    {
        if (room.settings == null || room.playerPrefab == null || room.roomCamera == null || room.inputContext == null || room.treatmentSpot == null)
            throw new InvalidOperationException("Missing treatment room references.");
        var roots = scene.GetRootGameObjects();
        if (roots.SelectMany(x => x.GetComponentsInChildren<ScareCrowTrainingScene>(true)).Any()
            || roots.SelectMany(x => x.GetComponentsInChildren<HideOutDummy>(true)).Any()
            || roots.SelectMany(x => x.GetComponentsInChildren<PlayerInventory>(true)).Any())
            throw new InvalidOperationException("Copied training controller / dummy / pre-spawned player must not remain.");
        if (roots.SelectMany(x => x.GetComponentsInChildren<Camera>(true)).Count() != 1)
            throw new InvalidOperationException("Treatment room must have exactly one camera.");
        if (roots.SelectMany(x => x.GetComponentsInChildren<EventSystem>(true)).Count() != 1)
            throw new InvalidOperationException("Treatment room must have exactly one input context.");
        if (roots.SelectMany(x => x.GetComponentsInChildren<Interactable2D>(true)).Any())
            throw new InvalidOperationException("Old booth F interaction must not remain.");
    }
}
