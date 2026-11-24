using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Adds the existing story Dialogue UI to HideOutSystemScene so Selena's base lines (SelenaBaseDialogue) also work
// after Continue, when the base is loaded on its own and no field scene (with its ConversationDialogue) is loaded.
// Explicit menu only; nothing runs on scene open.
//  - Copies Forest's "UI - Dialogue" canvas (read only; Forest is never saved) under one new root in the base scene,
//    adds a ConversationDialogue wired to the copy (no JSON, no control lock: it is only a template that
//    CreateTutorialPresentation clones), and saves only HideOutSystemScene.
//  - Refuses when the root already exists, when either scene has unsaved edits, or when a copied reference would
//    point back into Forest.
public static class SelenaDialogueSetup
{
    public const string HostName = "Selena Dialogue UI (base)";
    private const string BaseScenePath = "Assets/Scenes/HideOutSystemScene/HideOutSystemScene.unity";
    private const string SourceScenePath = "Assets/Scenes/Main/Forest.unity";
    private static readonly string[] ComponentFields = { "dialogueGroup", "portraitImage", "speakerText", "bodyText", "continueIndicator" };

    [MenuItem("Tools/HideOut/Add Selena dialogue UI to base (explicit)")]
    public static void Add()
    {
        if (Application.unityVersion != "6000.3.23f1" || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Requires edit mode in Unity 6000.3.23f1.");
        string basePath = BaseScenePath;

        var baseExisting = SceneManager.GetSceneByPath(basePath);
        var sourceExisting = SceneManager.GetSceneByPath(SourceScenePath);
        if (baseExisting.IsValid() && baseExisting.isDirty) throw new InvalidOperationException("HideOutSystemScene has unsaved edits; save or revert them first.");
        if (sourceExisting.IsValid() && sourceExisting.isDirty) throw new InvalidOperationException("Forest has unsaved edits; save or revert them first.");

        var previous = SceneManager.GetActiveScene();
        bool openedBase = !baseExisting.IsValid(), openedSource = !sourceExisting.IsValid();
        var baseScene = openedBase ? EditorSceneManager.OpenScene(basePath, OpenSceneMode.Additive) : baseExisting;
        var sourceScene = openedSource ? EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Additive) : sourceExisting;
        GameObject host = null;
        try
        {
            if (baseScene.GetRootGameObjects().Any(root => root.name == HostName))
                throw new InvalidOperationException($"'{HostName}' already exists in {basePath}.");

            var source = sourceScene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<ConversationDialogue>(true))
                .FirstOrDefault(d => d.BodyText != null && new SerializedObject(d).FindProperty("dialogueCanvasRoot").objectReferenceValue != null);
            if (source == null) throw new InvalidOperationException("No ConversationDialogue with a dialogue canvas in Forest.");
            var sourceData = new SerializedObject(source);
            var sourceCanvas = (GameObject)sourceData.FindProperty("dialogueCanvasRoot").objectReferenceValue;

            SceneManager.SetActiveScene(baseScene);
            host = new GameObject(HostName);
            SceneManager.MoveGameObjectToScene(host, baseScene);
            var canvas = Object.Instantiate(sourceCanvas, host.transform);
            canvas.name = sourceCanvas.name;
            canvas.SetActive(false); // Hidden until a line plays (ConversationDialogue.SetVisible also hides it on Awake).

            var dialogue = host.AddComponent<ConversationDialogue>();
            var data = new SerializedObject(dialogue);
            data.FindProperty("characterInterval").floatValue = sourceData.FindProperty("characterInterval").floatValue;
            data.FindProperty("dialogueCanvasRoot").objectReferenceValue = canvas;
            foreach (string field in ComponentFields)
                data.FindProperty(field).objectReferenceValue = Map(sourceData.FindProperty(field).objectReferenceValue, sourceCanvas, canvas);
            var sourceObjects = sourceData.FindProperty("dialogueUiObjects");
            var objects = data.FindProperty("dialogueUiObjects");
            objects.arraySize = sourceObjects.arraySize;
            for (int i = 0; i < sourceObjects.arraySize; i++)
                objects.GetArrayElementAtIndex(i).objectReferenceValue = Map(sourceObjects.GetArrayElementAtIndex(i).objectReferenceValue, sourceCanvas, canvas);
            data.ApplyModifiedPropertiesWithoutUndo();

            RequireNoReferenceInto(host, sourceScene);
            if (dialogue.BodyText == null) throw new InvalidOperationException("Copied dialogue UI has no body text.");

            EditorSceneManager.MarkSceneDirty(baseScene);
            if (!EditorSceneManager.SaveScene(baseScene)) throw new InvalidOperationException("HideOutSystemScene could not be saved.");
            Debug.Log($"[SelenaDialogueSetup] Added '{HostName}' to {basePath} (copied from {SourceScenePath}, which was not modified).");
        }
        catch
        {
            // Nothing is saved on failure: remove the half-built root (a base scene opened here is also closed unsaved).
            if (host != null) Object.DestroyImmediate(host);
            throw;
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (openedSource) EditorSceneManager.CloseScene(sourceScene, true); // Read only: closed without saving.
            if (openedBase) EditorSceneManager.CloseScene(baseScene, true);
        }
    }

    // Same index mapping as ConversationDialogue.CreateTutorialPresentation.
    private static Object Map(Object original, GameObject sourceRoot, GameObject copyRoot)
    {
        if (original == null) return null;
        if (original is GameObject go)
        {
            var mapped = Map(go.transform, sourceRoot, copyRoot) as Transform;
            return mapped != null ? mapped.gameObject : null;
        }
        if (original is not Component component) return null;
        var type = component.GetType();
        int index = Array.IndexOf(sourceRoot.GetComponentsInChildren(type, true), component);
        if (index < 0) throw new InvalidOperationException($"{component.name} ({type.Name}) is not inside the dialogue canvas.");
        return copyRoot.GetComponentsInChildren(type, true)[index];
    }

    private static void RequireNoReferenceInto(GameObject root, Scene forbidden)
    {
        foreach (var component in root.GetComponentsInChildren<Component>(true))
        {
            if (component == null) continue;
            var property = new SerializedObject(component).GetIterator();
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue == null) continue;
                var target = property.objectReferenceValue;
                var targetObject = target is Component c ? c.gameObject : target as GameObject;
                if (targetObject != null && targetObject.scene == forbidden)
                    throw new InvalidOperationException($"{component.name}.{property.propertyPath} still points into Forest.");
            }
        }
    }
}
