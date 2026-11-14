using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class CloakIdleOffsetRestore
{
    static CloakIdleOffsetRestore() => EditorApplication.update += Run;
    static void Run()
    {
        const string root = "Temp/CloakIdleOffsetRestore/";
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(root + "run")) return;
        File.Delete(root + "run");
        try
        {
            if (Application.unityVersion != "6000.3.23f1") throw new Exception("Editor version");
            var library = Resources.Load<PlayerCloakLibrary>("PlayerSkills/PlayerCloakLibrary");
            foreach (var body in library.bodySprites)
            {
                var cloak = library.Frame(body);
                if (cloak == null || cloak.rect != body.rect || cloak.pivot != body.pivot || cloak.pixelsPerUnit != body.pixelsPerUnit)
                    throw new Exception("Canvas/pivot mismatch: " + body.name);
            }
            int count = 0;
            foreach (var visual in UnityEngine.Object.FindObjectsByType<PlayerCloakVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (visual.gameObject.scene.name != "Forest") continue;
                var cloak = visual.transform.Find("Cloak");
                if (cloak == null) continue;
                var scene = visual.gameObject.scene;
                EditorSceneManager.SaveScene(scene, root + "Forest.live-before.unity", true);
                var position = cloak.localPosition; position.y = .15f; cloak.localPosition = position;
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                if (cloak.localScale != Vector3.one || cloak.localRotation != Quaternion.identity) throw new Exception("Unexpected cloak scale/rotation");
                count++;
            }
            File.WriteAllText(root + "results.txt", $"PASS Unity {Application.unityVersion}: {library.bodySprites.Length} sprite pairs have identical rect/pivot/PPU. Updated {count} Forest Cloak Idle Y to 0.15; scale/rotation verified.");
        }
        catch (Exception e) { File.WriteAllText(root + "results.txt", "FAIL " + e); Debug.LogException(e); }
    }
}
