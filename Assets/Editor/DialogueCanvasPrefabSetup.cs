using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// One-time extraction only. Existing prefabs are never regenerated over an artist's edits.
[InitializeOnLoad]
public static class DialogueCanvasPrefabSetup
{
    private const string Folder = "Assets/!UI/Dialogue";
    private const string StoryPath = Folder + "/DialogueCanvas.prefab";
    private const string LibraryPath = "Assets/Resources/Dialogue/CanvasPrefabs.asset";
    private const string Request = "Temp/DialogueCanvasPrefab/setup.request";
    private const string Result = "Temp/DialogueCanvasPrefab/setup-result.txt";
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    static DialogueCanvasPrefabSetup() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        try { Build(); }
        catch (Exception e) { File.WriteAllText(Result, "FAIL\n" + e); Debug.LogException(e); }
    }

    [MenuItem("Tools/Dialogue/Create editable Canvas prefabs")]
    public static void Build()
    {
        if (Application.unityVersion != "6000.3.23f1") throw new InvalidOperationException("Use Unity 6000.3.23f1.");
        Directory.CreateDirectory("Temp/DialogueCanvasPrefab");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/!UI", "Dialogue");
        var story = AssetDatabase.LoadAssetAtPath<GameObject>(StoryPath) ?? BuildStory();
        var treatment = TreatmentDialoguePrefabBuilder.Build();
        var library = AssetDatabase.LoadAssetAtPath<DialogueCanvasPrefabs>(LibraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<DialogueCanvasPrefabs>();
            library.storyCanvas = story;
            library.treatmentCanvas = treatment;
            AssetDatabase.CreateAsset(library, LibraryPath);
        }
        else if (library.storyCanvas == null || library.treatmentCanvas == null)
        {
            // Only fill missing slots; leave intentional artist-selected overrides untouched.
            if (library.storyCanvas == null) library.storyCanvas = story;
            if (library.treatmentCanvas == null) library.treatmentCanvas = treatment;
            EditorUtility.SetDirty(library);
        }
        AssetDatabase.SaveAssets();
        var view = library.storyCanvas.GetComponent<DialogueCanvasView>();
        Require(view != null && view.body != null && view.speaker != null && view.portrait != null, "Story Canvas bindings");
        Require(view.talking != null && view.continueIndicator != null, "Bubble and next triangle bindings");
        Require(view.choiceTemplate != null && view.choiceTemplate.accept != null && view.choiceTemplate.reject != null
            && view.choiceTemplate.acceptLabel != null && view.choiceTemplate.rejectLabel != null && view.choiceTemplate.holdProgress != null, "Editable choice bindings");
        Require(library.treatmentCanvas.GetComponent<TreatmentDialogueView>() != null, "Treatment Canvas binding");
        foreach (var prefab in new[] { library.storyCanvas, library.treatmentCanvas })
            ValidateReferences(prefab);
        File.WriteAllText(Result, "PASS Unity " + Application.unityVersion
            + "\nStory and treatment Canvas prefabs created and referenced through Resources/Dialogue/CanvasPrefabs."
            + "\nAll object references resolve; source scenes were not saved or modified."
            + "\n" + AssetDatabase.GetAssetPath(library.storyCanvas)
            + "\n" + AssetDatabase.GetAssetPath(library.treatmentCanvas));
    }

    private static GameObject BuildStory()
    {
        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/Main/Market.unity");
        try
        {
            var source = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ConversationDialogue>(true)).First();
            var data = new SerializedObject(source);
            T Read<T>(string field) where T : Object => data.FindProperty(field).objectReferenceValue as T;
            var root = Read<GameObject>("dialogueCanvasRoot");
            root.name = "DialogueCanvas";
            root.transform.localScale = Vector3.one;
            var view = root.AddComponent<DialogueCanvasView>();
            view.group = Read<CanvasGroup>("dialogueGroup");
            view.portrait = Read<Image>("portraitImage");
            view.speaker = Read<TMP_Text>("speakerText");
            view.body = Read<TMP_Text>("bodyText");
            view.talking = root.GetComponentsInChildren<Image>(true).FirstOrDefault(image => image.name == "Talking");
            var items = data.FindProperty("dialogueUiObjects");
            view.visibleObjects = Enumerable.Range(0, items.arraySize)
                .Select(i => items.GetArrayElementAtIndex(i).objectReferenceValue as GameObject).ToArray();
            if (view.talking != null) view.talking.gameObject.SetActive(false);

            var marker = new GameObject("Dialogue Next Triangle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = marker.GetComponent<RectTransform>();
            rect.SetParent(root.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(520f, 75f);
            rect.sizeDelta = new Vector2(28f, 28f);
            var image = marker.GetComponent<Image>();
            var visualStyle = Resources.Load<DialogueVisualStyle>("Dialogue/VisualStyle");
            image.sprite = visualStyle != null ? visualStyle.triangle : null;
            image.preserveAspect = true;
            image.raycastTarget = false;
            view.continueIndicator = marker;
            view.continueGlowShader = visualStyle != null ? visualStyle.glowShader : null;

            // Reuse the current choice builder once to retain its exact authored look and fonts.
            typeof(ConversationDialogue).GetMethod("CreateChoiceButtons", Fields).Invoke(source, new object[] { "수락", "거절" });
            var choices = (RectTransform)typeof(ConversationDialogue).GetField("choiceRoot", Fields).GetValue(source);
            var choiceView = choices.gameObject.AddComponent<DialogueChoiceView>();
            choiceView.accept = choices.Find("Accept").GetComponent<Button>();
            choiceView.reject = choices.Find("Reject").GetComponent<Button>();
            choiceView.acceptLabel = choiceView.accept.GetComponentInChildren<TMP_Text>(true);
            choiceView.rejectLabel = choiceView.reject.GetComponentInChildren<TMP_Text>(true);
            choiceView.holdProgress = (RectTransform)choiceView.accept.transform.Find("Accept Hold Progress");
            view.choiceTemplate = choiceView;
            choices.name = "Quest Choices (Template)";
            choices.gameObject.SetActive(false);
            root.SetActive(true);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, StoryPath);
            Require(prefab != null, "Save story Canvas prefab");
            return prefab;
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void ValidateReferences(GameObject prefab)
    {
        foreach (var component in prefab.GetComponentsInChildren<Component>(true))
        {
            Require(component != null, prefab.name + ": missing script");
            using var data = new SerializedObject(component);
            var property = data.GetIterator();
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                Require(property.objectReferenceValue != null || property.objectReferenceInstanceIDValue == 0,
                    prefab.name + ": broken reference " + component.name + "." + property.propertyPath);
                if (property.objectReferenceValue != null)
                    Require(EditorUtility.IsPersistent(property.objectReferenceValue),
                        prefab.name + ": unsaved reference " + component.name + "." + property.propertyPath);
            }
        }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
