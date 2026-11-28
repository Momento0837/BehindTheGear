using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BehindTheGear.HideOut;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class BaseCanvasPrefabSetup
{
    private const string Folder = "Temp/BaseCanvasPrefab/";
    private const string PrefabPath = "Assets/!UI/Base/Base_Canvas.prefab";
    private const string ArtPath = "Assets/!UI/Base/Base_Canvas_Artwork.asset";
    static BaseCanvasPrefabSetup() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (Application.unityVersion != "6000.3.23f1") return;
        if (EditorApplication.isPlaying && File.Exists(Folder + "export.request"))
        {
            var scene = Object.FindFirstObjectByType<HideOutScene>();
            if (scene == null || scene.TrainingPlayer == null || !scene.TrainingPlayer.Initialized
                || scene.GetComponentsInChildren<Canvas>(true).Length == 0) return;
            File.Delete(Folder + "export.request");
            try { Export(scene); File.WriteAllText(Folder + "connect.request", "Connect saved canvas"); }
            catch (Exception error) { File.WriteAllText(Folder + "result.txt", error.ToString()); Debug.LogException(error); }
            EditorApplication.isPlaying = false;
            return;
        }
        if (!Application.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode && File.Exists(Folder + "connect.request"))
        {
            File.Delete(Folder + "connect.request");
            try
            {
                var scene = SceneManager.GetActiveScene();
                if (scene.path != "Assets/!UI/Base/Base_Test.unity" || scene.isDirty)
                    throw new InvalidOperationException("Save Base_Test before connecting the prefab.");
                var controller = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<HideOutScene>(true)).Single();
                controller.canvasPrefab = AssetDatabase.LoadAssetAtPath<HideOutCanvasView>(PrefabPath);
                if (controller.canvasPrefab == null) throw new InvalidOperationException("Exported Canvas prefab missing.");
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                File.AppendAllText(Folder + "result.txt", "\nConnected Base_Test to Base_Canvas.prefab.\n");
            }
            catch (Exception error) { File.AppendAllText(Folder + "result.txt", error.ToString()); Debug.LogException(error); }
        }
        if (EditorApplication.isPlaying && File.Exists(Folder + "check.request"))
        {
            var controller = Object.FindFirstObjectByType<HideOutScene>();
            if (controller == null || controller.canvasPrefab == null || controller.TrainingPlayer == null
                || !controller.TrainingPlayer.Initialized || controller.GetComponentInChildren<HideOutCanvasView>(true) == null) return;
            File.Delete(Folder + "check.request");
            Base2DFacilityChecks.RunChecks();
        }
    }

    private static void Export(HideOutScene scene)
    {
        if (File.Exists(PrefabPath) || File.Exists(ArtPath)) throw new InvalidOperationException("Do not overwrite authored prefab/artwork.");
        Canvas.ForceUpdateCanvases();
        var source = scene.CaptureCanvasForPrefab();
        var clone = Object.Instantiate(source.gameObject);
        clone.name = "Base_Canvas";
        try
        {
            // Store all existing generated circle/HUD sprites as assets, so the prefab never depends on Play-mode objects.
            var sprites = new Dictionary<Sprite, Sprite>();
            var textures = new Dictionary<Texture2D, Texture2D>();
            foreach (var image in clone.GetComponentsInChildren<Image>(true))
            {
                var original = image.sprite;
                if (original == null || AssetDatabase.Contains(original)) continue;
                if (!sprites.TryGetValue(original, out var saved))
                {
                    if (!textures.TryGetValue(original.texture, out var texture))
                    {
                        texture = Object.Instantiate(original.texture);
                        texture.name = original.texture.name.Replace("Runtime", "Canvas");
                        if (!File.Exists(ArtPath)) AssetDatabase.CreateAsset(texture, ArtPath);
                        else AssetDatabase.AddObjectToAsset(texture, ArtPath);
                        textures.Add(original.texture, texture);
                    }
                    saved = Sprite.Create(texture, original.rect, original.pivot / original.rect.size,
                        original.pixelsPerUnit, 0, SpriteMeshType.FullRect, original.border);
                    saved.name = string.IsNullOrEmpty(original.name) ? texture.name + " Sprite" : original.name;
                    AssetDatabase.AddObjectToAsset(saved, ArtPath);
                    sprites.Add(original, saved);
                }
                image.sprite = saved;
            }
            var bindings = clone.GetComponent<HideOutCanvasView>();
            // Give the formerly anonymous labels stable, useful editing names.
            foreach (var field in typeof(HideOutCanvasView).GetFields())
                if (field.GetValue(bindings) is TMP_Text text) text.name = ObjectNames.NicifyVariableName(field.Name);
            foreach (var row in bindings.inventorySlots.Select((slot, index) => (slot, index)))
                row.slot.button.name = "Inventory Slot " + (row.index + 1).ToString("00");
            foreach (var row in bindings.storageSlots.Select((slot, index) => (slot, index)))
                row.slot.button.name = "Storage Slot " + (row.index + 1).ToString("00");
            foreach (var button in bindings.facilities.SelectMany(f => new[] { f.button, f.shortcut }))
            {
                HideOutFacilityLockCover.ShowLock(button, true);
                button.transform.Find("Temporary Facility Lock").gameObject.SetActive(false);
            }
            // The canvas is a clean level-zero editing template; runtime decides locked/active states.
            foreach (var facility in bindings.facilities) facility.levelLabel.text = "Lv. 0";
            foreach (var button in clone.GetComponentsInChildren<Button>(true)) button.interactable = true;
            bindings.resourceValues[0].text = "10/10";
            bindings.resourceValues[1].text = "100/100";
            bindings.resourceValues[2].text = "0";
            bindings.resourceValues[3].text = "0";
            bindings.feedbackText.text = "시설을 선택하세요.";
            bindings.storagePanel.SetActive(false);
            bindings.dummyPanel.SetActive(false);
            bindings.exhibitionPanel.SetActive(false);
            bindings.upgradeOverlay.SetActive(false);
            bindings.tooltip.SetActive(false);
            AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(ArtPath));
            var savedPrefab = PrefabUtility.SaveAsPrefabAsset(clone, PrefabPath, out bool success);
            if (!success || savedPrefab == null) throw new IOException("Canvas prefab save failed.");
            File.WriteAllText(Folder + "result.txt", $"Exported full Canvas: {clone.GetComponentsInChildren<Button>(true).Length} buttons, "
                + $"{clone.GetComponentsInChildren<TMP_Text>(true).Length} text elements; {sprites.Count} generated sprites persisted.\n");
        }
        finally { Object.DestroyImmediate(clone); }
    }
}
