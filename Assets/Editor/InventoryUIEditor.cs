using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Migrates legacy runtime UI to editable scene objects without replacing any existing layout.</summary>
[InitializeOnLoad]
[CustomEditor(typeof(InventoryUI))]
public sealed class InventoryUIEditor : Editor
{
    private static bool queued;

    static InventoryUIEditor()
    {
        QueueSetup();
        EditorSceneManager.sceneOpened += OnSceneOpened;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode) => QueueSetup();

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode) QueueSetup();
    }

    private static void QueueSetup()
    {
        if (queued || EditorApplication.isPlayingOrWillChangePlaymode) return;
        queued = true;
        EditorApplication.delayCall += SetupLoadedScenes;
    }

    private static void SetupLoadedScenes()
    {
        queued = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { QueueSetup(); return; }
        foreach (InventoryUI ui in Object.FindObjectsByType<InventoryUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (EditorUtility.IsPersistent(ui) || !ui.gameObject.scene.IsValid() || !ui.gameObject.scene.isLoaded
                || EditorSceneManager.IsPreviewScene(ui.gameObject.scene)) continue;
            if (!ui.HasSceneUI && ui.CanCreateSceneUI) CreateUI(ui);
        }
    }

    private static void CreateUI(InventoryUI ui)
    {
        if (ui.HasSceneUI || !ui.CanCreateSceneUI) return;
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Create editable inventory UI");
        Undo.RecordObject(ui, "Connect inventory scene UI");
        ui.CreateSceneUI();
        if (ui.SceneCanvas != null) Undo.RegisterCreatedObjectUndo(ui.SceneCanvas.gameObject, "Create inventory canvas");
        EditorUtility.SetDirty(ui);
        PrefabUtility.RecordPrefabInstancePropertyModifications(ui);
        EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
        Undo.CollapseUndoOperations(group);
        Debug.Log("[Inventory] Editable Inventory Canvas created. Adjust its Rect Transforms and save the scene with Ctrl+S.", ui);
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        InventoryUI ui = (InventoryUI)target;
        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (!ui.HasSceneUI)
            {
                EditorGUILayout.HelpBox("Create scene UI once to edit its layout before Play. Assign Inventory first.", MessageType.Info);
                using (new EditorGUI.DisabledScope(!ui.CanCreateSceneUI))
                    if (GUILayout.Button("Create Editable Scene UI")) CreateUI(ui);
                return;
            }
            EditorGUILayout.HelpBox("Edit Inventory Shortcut / Inventory Window with the Rect Tool (T). Save the scene to keep the layout. Play starts with the window closed.", MessageType.Info);
            if (GUILayout.Button("Select Bag Button") && ui.ShortcutButton != null)
                SelectUI(ui.ShortcutButton.gameObject);
            if (GUILayout.Button("Select Inventory Window") && ui.Window != null)
            {
                Undo.RecordObject(ui.Window, "Show inventory window preview");
                ui.Window.SetActive(true);
                EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
                SelectUI(ui.Window);
            }
            if (ui.Window != null)
            {
                bool preview = EditorGUILayout.Toggle("Show Window Preview", ui.Window.activeSelf);
                if (preview != ui.Window.activeSelf)
                {
                    Undo.RecordObject(ui.Window, "Toggle inventory window preview");
                    ui.Window.SetActive(preview);
                    EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
                }
            }
        }
    }

    private static void SelectUI(GameObject obj)
    {
        Selection.activeGameObject = obj;
        Tools.current = Tool.Rect;
        SceneView.lastActiveSceneView?.FrameSelected();
    }
}
