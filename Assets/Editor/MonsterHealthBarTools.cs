using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-click HP bars for every monster: adds MonsterHealthBar to each MonsterFollowController in monster prefabs and
/// scenes, and turns on showHealthBar in every MonsterProfile. Safe to run again whenever new monsters are added
/// (objects that already have a bar are skipped). Nothing is committed; scenes are saved only when you choose "all scenes".
/// </summary>
public static class MonsterHealthBarTools
{
    private const string Menu = "Tools/Behind The Gear/Monster/";
    private const string ControllerScript = "Assets/Monster/Scen/MonsterFollowController.cs";

    [MenuItem(Menu + "Add Health Bars to All Monsters")]
    public static void AddToAll() => Run(true);

    [MenuItem(Menu + "Remove Health Bars from All Monsters")]
    public static void RemoveFromAll() => Run(false);

    [MenuItem(Menu + "Add Health Bars to All Monsters", true)]
    [MenuItem(Menu + "Remove Health Bars from All Monsters", true)]
    private static bool CanRun() => !EditorApplication.isPlayingOrWillChangePlaymode;

    private static void Run(bool add)
    {
        string verb = add ? "추가" : "제거";
        var openScenePaths = OpenScenes().Select(s => s.path).ToHashSet();
        var closedMonsterScenes = MonsterScenePaths().Where(p => !openScenePaths.Contains(p)).ToList();
        string message = "MonsterFollowController가 있는 모든 몬스터에 체력바(MonsterHealthBar)를 " + verb + "합니다.\n\n"
            + "· 몬스터 프리팹과 MonsterProfile은 항상 처리합니다.\n"
            + "· 지금 열린 씬은 변경만 하고 저장하지 않습니다 (직접 저장하세요).\n";
        bool includeClosedScenes = false;
        if (closedMonsterScenes.Count > 0)
        {
            message += "· 닫혀 있는 몬스터 씬 " + closedMonsterScenes.Count + "개: "
                + string.Join(", ", closedMonsterScenes.Select(System.IO.Path.GetFileNameWithoutExtension))
                + "\n  '모든 씬'을 고르면 하나씩 추가로 열어 적용·저장하고 다시 닫습니다 (지금 열린 씬은 그대로).";
            int choice = EditorUtility.DisplayDialogComplex("몬스터 체력바 " + verb, message, "열린 씬만", "취소", "모든 씬 (열고 저장)");
            if (choice == 1) return;
            includeClosedScenes = choice == 2;
        }
        else if (!EditorUtility.DisplayDialog("몬스터 체력바 " + verb, message + "· 닫혀 있는 몬스터 씬은 없습니다.", verb, "취소"))
            return;

        var report = new List<string>();
        int profiles = ApplyToProfiles(add);
        if (profiles > 0) report.Add("MonsterProfile " + profiles + "개");
        int prefabCount = ApplyToPrefabs(add, report);

        int sceneObjects = 0;
        foreach (var scene in OpenScenes())
        {
            int changed = ApplyToScene(scene, add);
            if (changed == 0) continue;
            EditorSceneManager.MarkSceneDirty(scene);
            sceneObjects += changed;
            report.Add(scene.name + " (열린 씬, 저장 안 됨) " + changed + "개");
        }

        if (includeClosedScenes)
        {
            // Opened additively, so the scenes you are editing (and their unsaved changes) are never touched.
            foreach (string path in closedMonsterScenes)
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    int changed = ApplyToScene(scene, add);
                    if (changed == 0) continue;
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene)) { report.Add(scene.name + " 저장 실패"); continue; }
                    sceneObjects += changed;
                    report.Add(scene.name + " (저장됨) " + changed + "개");
                }
                finally
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        string summary = report.Count == 0
            ? "바꿀 몬스터가 없습니다 (이미 모두 " + (add ? "달려" : "제거되어") + " 있습니다)."
            : "체력바 " + verb + " 완료\n- " + string.Join("\n- ", report);
        Debug.Log("[Monster Health Bar] " + summary.Replace("\n", " | "));
        EditorUtility.DisplayDialog("몬스터 체력바 " + verb, summary, "확인");
    }

    private static IEnumerable<Scene> OpenScenes()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene.isLoaded && !string.IsNullOrEmpty(scene.path)) yield return scene;
        }
    }

    // Scenes under Assets that use MonsterFollowController: the scene text contains the script GUID (no scene is opened).
    private static IEnumerable<string> MonsterScenePaths()
    {
        string guid = AssetDatabase.AssetPathToGUID(ControllerScript);
        if (string.IsNullOrEmpty(guid)) return Enumerable.Empty<string>();
        return AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => System.IO.File.Exists(path) && System.IO.File.ReadAllText(path).Contains("guid: " + guid))
            .ToList();
    }

    private static int ApplyToScene(Scene scene, bool add)
    {
        int changed = 0;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var monster in root.GetComponentsInChildren<MonsterFollowController>(true))
                if (ApplyToObject(monster.gameObject, add, true)) changed++;
        return changed;
    }

    private static int ApplyToPrefabs(bool add, List<string> report)
    {
        int prefabs = 0;
        foreach (string path in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath))
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) continue;
            var monsters = asset.GetComponentsInChildren<MonsterFollowController>(true);
            if (monsters.Length == 0 || monsters.All(m => (m.GetComponent<MonsterHealthBar>() != null) == add)) continue;

            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int changed = contents.GetComponentsInChildren<MonsterFollowController>(true)
                    .Count(m => ApplyToObject(m.gameObject, add, false));
                if (changed == 0) continue;
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                prefabs++;
                report.Add(System.IO.Path.GetFileName(path) + " (프리팹) " + changed + "개");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }
        return prefabs;
    }

    private static bool ApplyToObject(GameObject target, bool add, bool undo)
    {
        var bar = target.GetComponent<MonsterHealthBar>();
        if (add)
        {
            if (bar != null) return false;
            if (undo) Undo.AddComponent<MonsterHealthBar>(target);
            else target.AddComponent<MonsterHealthBar>();
            return true;
        }
        if (bar == null) return false;
        if (undo) Undo.DestroyObjectImmediate(bar);
        else Object.DestroyImmediate(bar, true);
        return true;
    }

    private static int ApplyToProfiles(bool add)
    {
        int changed = 0;
        foreach (string path in AssetDatabase.FindAssets("t:MonsterProfile", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath))
        {
            var profile = AssetDatabase.LoadAssetAtPath<MonsterProfile>(path);
            if (profile == null || profile.showHealthBar == add) continue;
            Undo.RecordObject(profile, "Monster health bar");
            profile.showHealthBar = add;
            EditorUtility.SetDirty(profile);
            changed++;
        }
        if (changed > 0) AssetDatabase.SaveAssets();
        return changed;
    }
}
