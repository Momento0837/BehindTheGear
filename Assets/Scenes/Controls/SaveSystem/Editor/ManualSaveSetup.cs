#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BehindTheGear.HideOut;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Explicit scene wiring and isolated checks in the already open required editor.</summary>
[InitializeOnLoad]
public static class ManualSaveSetup
{
    public const string SettingsPath = "Assets/Scenes/HideOutSystemScene/Data/HideOutPrototype.asset";
    public const string PlayerPath = "Assets/Player/SavePlayerRig.prefab";
    private const string Request = "Temp/ManualSaveValidation/request.txt";
    static ManualSaveSetup() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        string command = File.ReadAllText(Request).Trim(); File.Delete(Request);
        try
        {
            if (Application.unityVersion != "6000.3.23f1") throw new InvalidOperationException("Required Unity: 6000.3.23f1");
            if (command == "stop") EditorApplication.isPlaying = false;
            else if (command == "setup" && !Application.isPlaying) Wire();
            else if (command == "checks" && !Application.isPlaying) ManualSaveChecks.Run();
            else if (command == "restart" && !Application.isPlaying) ManualSaveChecks.RunRestart();
            else if (command == "validate-wiring" && !Application.isPlaying) ValidateWiring();
            else if (command == "inspect") Inspect();
        }
        catch (Exception error) { File.WriteAllText("Temp/ManualSaveValidation/editor-error.txt", error.ToString()); Debug.LogException(error); }
    }
    private static void ValidateWiring()
    {
        var settings = AssetDatabase.LoadAssetAtPath<HideOutSettings>(SettingsPath); settings.ValidateCatalog();
        var report = new List<string>();
        foreach (string scenePath in new[] { "Assets/Scenes/Controls/ControlSchemeSelect.unity", "Assets/Scenes/Main/Forest.unity", "Assets/Scenes/Main/Market.unity" })
        {
            var scene = EditorSceneManager.OpenPreviewScene(scenePath);
            try
            {
                var roots = scene.GetRootGameObjects();
                if (scenePath.Contains("ControlSchemeSelect"))
                {
                    var menu = roots.SelectMany(r => r.GetComponentsInChildren<ManualSaveMenu>(true)).Single();
                    if (menu.settings != settings || menu.continuePlayerPrefab == null) throw new Exception("Menu references");
                }
                else
                {
                    var entrance = roots.SelectMany(r => r.GetComponentsInChildren<HideOutEntrance>(true)).Single();
                    if (entrance.settings != settings || entrance.gameObject.layer != LayerMask.NameToLayer("Interactable")
                        || entrance.GetComponent<SpriteRenderer>() == null || entrance.GetComponent<Interactable2D>() == null) throw new Exception("Entrance references");
                    foreach (var npc in roots.SelectMany(r => r.GetComponentsInChildren<QuestGiver2D>(true)))
                        if (new SerializedObject(npc).FindProperty("questDefinition").objectReferenceValue == null) throw new Exception("NPC definition");
                }
                report.Add("PASS " + scenePath);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        if (!EditorBuildSettings.scenes.Any(s => s.enabled && s.path == settings.scenePath)) throw new Exception("HideOut build scene");
        report.Add("PASS hideout build scene, catalog and continue player prefab");
        File.WriteAllLines("Temp/ManualSaveValidation/wiring-checks.txt", report);
    }
    private static void Inspect()
    {
        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/Main/Forest.unity");
        try { File.WriteAllLines("Temp/ManualSaveValidation/forest-roots.txt", scene.GetRootGameObjects().Select(r => r.name + ": " + string.Join(",", r.GetComponents<Component>().Select(c => c.GetType().Name)))); }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
    [MenuItem("Tools/Manual Save/Wire production scenes")]
    public static void Wire()
    {
        if (EditorSceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("현재 씬의 미저장 변경을 먼저 보존해야 합니다.");
        var settings = AssetDatabase.LoadAssetAtPath<HideOutSettings>(SettingsPath);
        // All existing definitions become part of the existing catalog; IDs are not generated at runtime.
        settings.items = AssetDatabase.FindAssets("t:InventoryItemDefinition").Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<InventoryItemDefinition>).OrderBy(i => i.ItemId).ToArray();
        settings.ValidateCatalog(); EditorUtility.SetDirty(settings);
        BuildPlayer();
        var menuScene = EditorSceneManager.OpenScene("Assets/Scenes/Controls/ControlSchemeSelect.unity", OpenSceneMode.Additive);
        try
        {
            var controls = menuScene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ControlSchemeSelectScene>(true)).Single();
            var menu = controls.GetComponent<ManualSaveMenu>() ?? controls.gameObject.AddComponent<ManualSaveMenu>();
            menu.settings = settings; menu.continuePlayerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            EditorSceneManager.MarkSceneDirty(menuScene); EditorSceneManager.SaveScene(menuScene);
        }
        finally { EditorSceneManager.CloseScene(menuScene, true); }
        foreach (string name in new[] { "Forest", "Market" })
        {
            string scenePath = "Assets/Scenes/Main/" + name + ".unity";
            var existing = SceneManager.GetSceneByPath(scenePath);
            bool wasOpen = existing.IsValid() && existing.isLoaded;
            var scene = wasOpen ? existing : EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                if (scene.isDirty) throw new InvalidOperationException("미저장 씬은 수정하지 않습니다: " + name);
                if (!scene.GetRootGameObjects().Any(r => r.GetComponent<HideOutEntrance>() != null))
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Scenes/HideOutSystemScene/Prefabs/HideOutEntrance.prefab");
                    var entrance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    entrance.transform.position = name == "Forest" ? new Vector3(-2f, -2.5f, 0) : new Vector3(52f, -2.5f, 0);
                    entrance.layer = LayerMask.NameToLayer("Interactable");
                    var visual = entrance.GetComponent<SpriteRenderer>();
                    if (visual == null) visual = entrance.AddComponent<SpriteRenderer>();
                    visual.sprite = settings.appearanceSprites.FirstOrDefault(); visual.color = new Color(.6f, .85f, 1f);
                    entrance.transform.localScale = new Vector3(.75f, .75f, 1);
                    visual.sortingOrder = 20;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(entrance.transform);
                }
                foreach (var npc in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<QuestGiver2D>(true)))
                {
                    var serialized = new SerializedObject(npc);
                    serialized.FindProperty("questDefinition").objectReferenceValue = Resources.Load<TextAsset>("Quests/" + (name == "Market" ? "market_quest" : "collection_quest"));
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            finally { if (!wasOpen) EditorSceneManager.CloseScene(scene, true); }
        }
        var scenes = EditorBuildSettings.scenes.ToList();
        if (!scenes.Any(s => s.path == settings.scenePath)) scenes.Add(new EditorBuildSettingsScene(settings.scenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        AssetDatabase.SaveAssets();
        File.WriteAllText("Temp/ManualSaveValidation/setup.txt", "PASS: catalog, player rig, menu, Forest/Market entrance and NPC references, HideOut build scene. Unity " + Application.unityVersion);
    }
    private static void BuildPlayer()
    {
        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/Main/Forest.unity");
        try
        {
            var player = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PersistentPlayer2D>(true)).Single();
            var included = new HashSet<GameObject> { player.transform.root.gameObject };
            foreach (var root in scene.GetRootGameObjects())
                if (root.GetComponentsInChildren<Canvas>(true).Any() && (root.name.Contains("Inventory") || root.name.Contains("InGame") || root.name.Contains("Ingame"))) included.Add(root);
            bool changed;
            do
            {
                changed = false;
                foreach (var root in included.ToArray())
                    foreach (var component in root.GetComponentsInChildren<Component>(true))
                    {
                        if (component == null) continue;
                        var iterator = new SerializedObject(component).GetIterator();
                        while (iterator.Next(true))
                        {
                            if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
                            var reference = iterator.objectReferenceValue as Component;
                            var target = reference != null ? reference.gameObject : iterator.objectReferenceValue as GameObject;
                            if (target == null || target.scene != scene) continue;
                            target = target.transform.root.gameObject;
                            if (target.GetComponent<Camera>() != null || target.GetComponent<ConversationDialogue>() != null
                                || target.GetComponent<CutsceneVideoSequence>() != null) continue;
                            if (included.Add(target)) changed = true;
                        }
                    }
            } while (changed);
            var rig = new GameObject("Save Player Rig"); SceneManager.MoveGameObjectToScene(rig, scene);
            foreach (var root in included) root.transform.SetParent(rig.transform, true);
            PrefabUtility.SaveAsPrefabAsset(rig, PlayerPath);
            File.WriteAllLines("Temp/ManualSaveValidation/player-rig.txt", included.Select(r => r.name));
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}
#endif
