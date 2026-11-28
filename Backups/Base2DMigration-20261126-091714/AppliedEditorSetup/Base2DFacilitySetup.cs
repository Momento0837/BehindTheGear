using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BehindTheGear.HideOut;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Explicit, one-scene wiring; no edit-mode spawning or alteration of the authored art.
[InitializeOnLoad]
public static class Base2DFacilitySetup
{
    public const string ScenePath = "Assets/!UI/Base/Base_Test.unity";
    private const string OldScenePath = "Assets/Scenes/HideOutSystemScene/HideOutSystemScene.unity";
    private const string Request = "Temp/Base2DMigration/setup.request";
    static Base2DFacilitySetup() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode) return;
        const string layoutRequest = "Temp/Base2DMigration/layout.request";
        if (File.Exists(layoutRequest))
        {
            File.Delete(layoutRequest);
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || scene.isDirty) throw new InvalidOperationException("Save Base_Test before adjusting its anchors.");
            var environment = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<HideOutSpriteEnvironment>(true)).Single();
            var cache = new Dictionary<Sprite, Bounds>();
            foreach (var area in environment.areas)
                if (area.facility == Facility.Dummy || area.facility == Facility.Gym)
                    area.iconAnchor.position = VisibleBounds(area.root, cache).center
                        + new Vector3(area.facility == Facility.Dummy ? -1.4f : 1.4f, -.35f, 0f);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            File.WriteAllText("Temp/Base2DMigration/layout-result.txt", "Training anchors separated.");
        }
        if (!File.Exists(Request)) return;
        File.Delete(Request);
        try { Connect(); }
        catch (Exception error)
        {
            File.WriteAllText("Temp/Base2DMigration/setup-result.txt", error.ToString());
            Debug.LogException(error);
        }
    }

    [MenuItem("Tools/HideOut/Connect authored 2D base")]
    public static void Connect()
    {
        if (Application.unityVersion != "6000.3.23f1") throw new InvalidOperationException("Use Unity 6000.3.23f1.");
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) throw new InvalidOperationException("Open Base_Test first.");
        if (scene.isDirty) throw new InvalidOperationException("Save the authored scene before connecting facilities.");
        string backup = File.ReadAllText("Temp/Base2DMigration/backup-path.txt").Trim();
        string savedScene = Path.Combine(backup, "Base_Test-before-connection.unity");
        if (!File.Exists(savedScene)) File.Copy(ScenePath, savedScene);
        Transform art = scene.GetRootGameObjects().Single(g => g.name == "Base").transform;
        var camera = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).Single(c => c.CompareTag("MainCamera"));
        if (art.GetComponent<HideOutSpriteEnvironment>() != null)
            throw new InvalidOperationException("This base is already connected; edit its Area/Upgrade Props entries instead.");
        var environment = Undo.AddComponent<HideOutSpriteEnvironment>(art.gameObject);
        var areas = new List<HideOutSpriteEnvironment.Area>();
        var props = new List<HideOutSpriteEnvironment.UpgradeProp>();
        var pixelBounds = new Dictionary<Sprite, Bounds>();
        var groups = new Dictionary<string, Facility>
        {
            ["Heal_Objects"] = Facility.Clinic, ["BedRoom_Objects"] = Facility.Bed,
            ["Potion_Objects"] = Facility.Potions, ["Training_Objects"] = Facility.Dummy,
            ["LivingRoom_Objects"] = Facility.Dining, ["Workbench_Objects"] = Facility.Workshop
        };
        bool hasBounds = false;
        Bounds mapBounds = default;
        foreach (var entry in groups)
        {
            var root = art.Find(entry.Key);
            if (root == null) throw new InvalidOperationException("Missing authored area: " + entry.Key);
            Bounds bounds = VisibleBounds(root, pixelBounds);
            if (!hasBounds) { mapBounds = bounds; hasBounds = true; } else mapBounds.Encapsulate(bounds);
            var primary = entry.Value switch
            {
                Facility.Clinic => root.Find("Desk"), Facility.Bed => root.Find("Bed"),
                Facility.Potions => root.Find("Shelf"), Facility.Dummy => root.Find("Training Dummy"),
                Facility.Dining => root.Find("Dining Table"), _ => root.Find("Workbench")
            };
            string icon = entry.Value switch
            {
                Facility.Clinic => "Heal", Facility.Bed => "Bed", Facility.Potions => "Potion",
                Facility.Dining => "Meal", _ => null
            };
            areas.Add(MakeArea(environment, root, primary ?? root, entry.Value, icon, bounds, camera, pixelBounds));
            if (entry.Value == Facility.Potions)
                areas.Add(MakeArea(environment, root, root.Find("Box") ?? root, Facility.Storage, null, bounds, camera, pixelBounds));
            if (entry.Value == Facility.Dummy)
                areas.Add(MakeArea(environment, root, root.Find("Punching Bag") ?? root, Facility.Gym, null, bounds, camera, pixelBounds));

            // Bottom-to-top authored order. Active children remain baseline; only disabled props unlock.
            var levels = new Dictionary<Facility, int>();
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (child.gameObject.activeSelf || child.name == "Room_Group" || child.name == "Floor_Group") continue;
                Facility owner = entry.Value;
                if (entry.Value == Facility.Potions && (child.name == "Box" || child.name == "Cart")) owner = Facility.Storage;
                if (entry.Value == Facility.Dummy && (child.name == "Punching Bag" || child.name == "Instrument Panel")) owner = Facility.Gym;
                levels.TryGetValue(owner, out int level);
                levels[owner] = ++level;
                props.Add(new HideOutSpriteEnvironment.UpgradeProp { facility = owner, target = child.gameObject, requiredLevel = level });
            }
        }
        environment.areas = areas.ToArray();
        environment.upgradeProps = props.ToArray();
        environment.worldBounds = mapBounds;
        environment.dropPixels = 10f;
        environment.dropDuration = .4f;

        var preview = EditorSceneManager.OpenPreviewScene(OldScenePath);
        HideOutScene view;
        try
        {
            var oldView = preview.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<HideOutScene>(true)).Single();
            var systems = new GameObject("HideOut Systems");
            Undo.RegisterCreatedObjectUndo(systems, "Connect 2D base");
            SceneManager.MoveGameObjectToScene(systems, scene);
            view = systems.AddComponent<HideOutScene>();
            EditorUtility.CopySerialized(oldView, view);
            view.spriteEnvironment = environment;
            view.editorAutoPreview = false;
            foreach (var root in preview.GetRootGameObjects())
                if (root.GetComponentInChildren<ConversationDialogue>(true) != null)
                {
                    var dialogue = Object.Instantiate(root);
                    dialogue.name = root.name;
                    SceneManager.MoveGameObjectToScene(dialogue, scene);
                    Undo.RegisterCreatedObjectUndo(dialogue, "Reuse existing base dialogue");
                }
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
        view.settings.scenePath = ScenePath;
        EditorUtility.SetDirty(view.settings);
        AssetDatabase.SaveAssetIfDirty(view.settings);
        var scenes = EditorBuildSettings.scenes.ToList();
        int buildIndex = scenes.FindIndex(s => s.path == ScenePath);
        if (buildIndex < 0) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        else scenes[buildIndex].enabled = true;
        EditorBuildSettings.scenes = scenes.ToArray();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save Base_Test.");
        string report = "Connected 8 facilities / " + props.Count + " authored disabled upgrade props.\n"
            + string.Join("\n", props.Select(p => p.facility + " Lv." + p.requiredLevel + " -> " + p.target.name));
        File.WriteAllText("Temp/Base2DMigration/setup-result.txt", report);
        Debug.Log("[2D Base] " + report);
    }

    private static HideOutSpriteEnvironment.Area MakeArea(HideOutSpriteEnvironment environment, Transform root,
        Transform primary, Facility facility, string iconName, Bounds bounds, Camera camera, Dictionary<Sprite, Bounds> cache)
    {
        var icon = new GameObject(facility + " Icon Anchor").transform;
        icon.SetParent(environment.transform, false);
        var primaryBounds = VisibleBounds(primary, cache);
        icon.position = primaryBounds.center + Vector3.up * .55f;
        if (facility == Facility.Dummy || facility == Facility.Gym)
            icon.position = bounds.center + new Vector3(facility == Facility.Dummy ? -1.4f : 1.4f, -.35f, 0f);
        var focus = new GameObject(facility + " Focus Anchor").transform;
        focus.SetParent(environment.transform, false);
        float size = Mathf.Max(2.0f, bounds.extents.y + .65f, bounds.extents.x / Mathf.Max(1f, camera.aspect) + .4f);
        focus.position = new Vector3(bounds.center.x + size * .55f, bounds.center.y, camera.transform.position.z);
        Sprite sprite = iconName == null ? null : AssetDatabase.LoadAllAssetsAtPath("Assets/!UI/Base/Icons/" + iconName + ".png").OfType<Sprite>().FirstOrDefault();
        if (iconName != null && sprite == null) throw new InvalidOperationException("Missing Sprite icon: " + iconName);
        return new HideOutSpriteEnvironment.Area
        { facility = facility, root = root, iconAnchor = icon, focusAnchor = focus, icon = sprite, focusSize = size };
    }

    // Exported sprites retain the full Aseprite canvas. Use actual alpha, not the transparent canvas bounds.
    private static Bounds VisibleBounds(Transform root, Dictionary<Sprite, Bounds> cache)
    {
        bool found = false;
        Bounds result = default;
        foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
        {
            var sprite = renderer.sprite;
            if (sprite == null) continue;
            if (!cache.TryGetValue(sprite, out var bounds))
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    texture.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(sprite)));
                    var pixels = texture.GetPixels32();
                    Rect rect = sprite.rect;
                    int x0 = Mathf.RoundToInt(rect.xMin), y0 = Mathf.RoundToInt(rect.yMin);
                    int x1 = Mathf.RoundToInt(rect.xMax), y1 = Mathf.RoundToInt(rect.yMax);
                    int minX = x1, minY = y1, maxX = x0, maxY = y0;
                    for (int y = y0; y < y1; y++)
                        for (int x = x0; x < x1; x++)
                            if (pixels[y * texture.width + x].a > 8)
                            { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x + 1); maxY = Math.Max(maxY, y + 1); }
                    Vector3 min = new((minX - x0 - sprite.pivot.x) / sprite.pixelsPerUnit, (minY - y0 - sprite.pivot.y) / sprite.pixelsPerUnit, 0);
                    Vector3 max = new((maxX - x0 - sprite.pivot.x) / sprite.pixelsPerUnit, (maxY - y0 - sprite.pivot.y) / sprite.pixelsPerUnit, 0);
                    bounds = new Bounds((min + max) * .5f, max - min);
                    cache[sprite] = bounds;
                }
                finally { Object.DestroyImmediate(texture); }
            }
            var world = new Bounds(renderer.transform.TransformPoint(bounds.min), Vector3.zero);
            world.Encapsulate(renderer.transform.TransformPoint(bounds.max));
            if (!found) { result = world; found = true; } else result.Encapsulate(world);
        }
        if (!found) throw new InvalidOperationException("No visible sprite bounds for " + root.name);
        return result;
    }
}
