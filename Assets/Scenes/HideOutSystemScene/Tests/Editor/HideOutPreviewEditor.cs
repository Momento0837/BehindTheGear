using BehindTheGear.HideOut;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One preview on scene open / Play exit. No ExecuteAlways, polling regeneration, or save reads.
[CustomEditor(typeof(HideOutScene))]
public sealed class HideOutPreviewEditor : Editor
{
    private Facility sample = Facility.Storage;
    private static GameObject previewRoot;
    private static HideOutScene owner;
    [InitializeOnLoadMethod]
    private static void RegisterCleanup()
    {
        AssemblyReloadEvents.beforeAssemblyReload += Clear;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode) Clear();
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += EnsureScenePreview;
        };
        EditorSceneManager.sceneOpened += (_, _) => EditorApplication.delayCall += EnsureScenePreview;
        EditorApplication.delayCall += EnsureScenePreview;
    }
    public static void EnsureScenePreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling
            || PrefabStageUtility.GetCurrentPrefabStage() != null || previewRoot != null) return;
        foreach (var view in Object.FindObjectsByType<HideOutScene>(FindObjectsSortMode.None))
            if (view.editorAutoPreview && view.settings != null && view.gameObject.hideFlags == HideFlags.None
                && view.gameObject.scene.path == view.settings.scenePath)
            { Generate(view); return; }
    }
    public static GameObject PreviewRoot => previewRoot;
    public static void Clear()
    {
        if (previewRoot != null) Object.DestroyImmediate(previewRoot);
        previewRoot = null; owner = null;
    }
    public static void Generate(HideOutScene source)
    {
        if (Application.isPlaying || PrefabStageUtility.GetCurrentPrefabStage() != null || source.settings == null) return;
        Clear(); owner = source;
        previewRoot = new GameObject("HideOut Editor Preview (temporary)");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(previewRoot, source.gameObject.scene);
        previewRoot.hideFlags = HideFlags.DontSave;
        previewRoot.AddComponent<HideOutScene>().BuildEditorPreview(source.settings);
        foreach (Transform child in previewRoot.GetComponentsInChildren<Transform>(true)) child.gameObject.hideFlags = HideFlags.DontSave;
        if (!EditorSceneManager.IsPreviewScene(source.gameObject.scene) && SceneView.lastActiveSceneView != null)
        {
            var sceneView = SceneView.lastActiveSceneView;
            sceneView.in2DMode = false;
            sceneView.orthographic = true;
            sceneView.LookAtDirect(previewRoot.GetComponentInChildren<HideOutIsometricEnvironment>().WorldBounds.center,
                Quaternion.Euler(source.settings.topView.cameraEuler), source.settings.topView.overviewOrthographicSize * 1.15f);
        }
        SceneView.RepaintAll(); EditorApplication.QueuePlayerLoopUpdate();
    }
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        if (Application.isPlaying) return;
        var source = (HideOutScene)target;
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("씬 열기 / Play 종료 시 Scene·Game 미리보기를 복원합니다. 샘플 데이터만 사용하며 Play 진입 전 정리됩니다. 외형 수정 후에는 갱신 버튼을 누르세요.", MessageType.Info);
        using (new EditorGUI.DisabledScope(PrefabStageUtility.GetCurrentPrefabStage() != null || source.settings == null))
        {
            if (GUILayout.Button("하이드아웃 미리보기 생성 / 갱신"))
            {
                Generate(source);
            }
            sample = (Facility)EditorGUILayout.EnumPopup("미리 볼 시설", sample);
            if (owner == source && previewRoot != null)
            {
                var view = previewRoot.GetComponent<HideOutScene>();
                if (GUILayout.Button("선택 시설 상세 미리보기")) view.ShowEditorSample(sample);
                if (GUILayout.Button("상세 미리보기 끄기 (배치 유지)")) view.HideEditorDetails();
                if (GUILayout.Button("임시 미리보기 전체 정리")) Clear();
            }
        }
    }
}
