using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Checks real inventory/UI code in isolated preview scenes without saving game assets.</summary>
public static class HotbarChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Checks/In Game Hotbar")]
    public static void Run()
    {
        const string resultPath = "Temp/HotbarValidation/result.txt";
        Directory.CreateDirectory(Path.GetDirectoryName(resultPath));
        Scene preview = default;
        GameObject hudRoot = null;
        try
        {
            Require(Application.unityVersion == "6000.3.23f1", "Unity version");
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play mode before checking");
            preview = EditorSceneManager.NewPreviewScene();
            var player = new GameObject("Hotbar Check Player");
            SceneManager.MoveGameObjectToScene(player, preview);
            var health = player.AddComponent<PlayerHealth>();
            Call(health, "Awake");
            var inventory = player.AddComponent<PlayerInventory>();
            Call(inventory, "Awake");
            var mana = player.AddComponent<PlayerMana>();
            var red = AssetDatabase.LoadAssetAtPath<InventoryItemDefinition>("Assets/Scenes/Inventory/Items/HealthPotion.asset");
            var blue = AssetDatabase.LoadAssetAtPath<InventoryItemDefinition>("Assets/Scenes/Inventory/Items/ManaPotion.asset");
            Require(red != null && blue != null, "Potion assets");
            Require(inventory.Add(red, 4) == 4 && inventory.Add(blue, 4) == 4, "Add potions");
            Require(inventory.GetQuickSlot(0) == null, "No automatic registration on pickup");
            Require(inventory.AssignQuickSlot(0, 0), "Register consumable");
            Require(inventory.AssignQuickSlot(0, 2), "Move registration");
            Require(inventory.GetQuickSlot(0) == null && inventory.GetQuickSlot(2) == red, "Unique slot registration");

            var ui = player.AddComponent<InventoryUI>();
            Set(ui, "inventory", inventory);
            var window = new GameObject("Inventory Check Window");
            SceneManager.MoveGameObjectToScene(window, preview);
            Set(ui, "window", window);
            ui.SetHoveredSlot(1, true);
            Call(ui, "HandleQuickSlotKey", 1);
            Require(inventory.GetQuickSlot(1) == blue && inventory.GetSlot(1).Amount == 4, "Hover key registers without consumption");
            ui.SetHoveredSlot(1, false);
            Call(ui, "HandleQuickSlotKey", 0);
            Require(inventory.GetSlot(0).Amount == 4, "Open inventory without hover does not consume");
            window.SetActive(false);
            health.RestoreSavedHealth(2);
            Call(ui, "HandleQuickSlotKey", 2);
            Require(inventory.GetSlot(0).Amount == 3 && health.CurrentHealth > 2, "Closed inventory key consumes and heals HP");
            Call(ui, "HandleQuickSlotKey", 2);
            Require(inventory.GetSlot(0).Amount == 3, "Cooldown blocks repeated key");
            Require(inventory.GetQuickSlotCooldownFill(2) > .95f, "Cooldown begins full");
            Require(inventory.AssignQuickSlot(0, 0) && !inventory.UseQuickSlot(0, player), "Moving registration retains item cooldown");
            mana.TrySpend(50);
            int beforeMana = mana.CurrentMana;
            Require(inventory.UseQuickSlot(1, player) && mana.CurrentMana > beforeMana, "Other slot independently consumes and restores MP");

            var deadlines = (float[])Get(inventory, "quickSlotReadyAt");
            var itemDeadlines = (Dictionary<InventoryItemDefinition, float>)Get(inventory, "itemReadyAt");
            deadlines[0] = deadlines[2] = Time.unscaledTime - 1f;
            itemDeadlines[red] = Time.unscaledTime + .25f;
            Near(inventory.GetQuickSlotCooldownFill(0), .5f, "Cooldown halfway");
            itemDeadlines[red] = Time.unscaledTime - 1f;
            Near(inventory.GetQuickSlotCooldownFill(0), 0f, "Cooldown complete");
            Require(inventory.UseQuickSlot(0, player), "Use after cooldown");

            var misc = ScriptableObject.CreateInstance<InventoryItemDefinition>();
            try
            {
                inventory.Add(misc, 1);
                Require(!inventory.AssignQuickSlot(2, 1), "Reject non-consumable category");
            }
            finally { UnityEngine.Object.DestroyImmediate(misc); }

            hudRoot = PrefabUtility.LoadPrefabContents("Assets/!UI/Ingame/InGame_Canvas.prefab");
            var hud = hudRoot.GetComponent<InGameHotbarHUD>();
            Require(hud != null, "HUD attached to prefab");
            hud.Bind(inventory);
            var images = (Image[])Get(hud, "slotImages");
            var overlays = (Image[])Get(hud, "cooldownImages");
            Require(images[0].sprite == red.Icon && images[1].sprite == blue.Icon && images[2].sprite == null, "Correct slot icons");
            Require(overlays[0].fillMethod == Image.FillMethod.Vertical && overlays[0].fillAmount > .95f, "Vertical cooldown image");
            Require(inventory.AssignQuickSlot(0, 2), "Register and flash");
            Near(images[2].material.GetFloat("_FlashAmount"), .5f, "Half-strength registration flash");
            var starts = (float[])Get(hud, "flashStartedAt");
            starts[2] = Time.unscaledTime - .15f;
            hud.Refresh();
            Near(images[2].material.GetFloat("_FlashAmount"), .25f, "Flash halfway at .15 seconds");
            starts[2] = Time.unscaledTime - .3f;
            hud.Refresh();
            Near(images[2].material.GetFloat("_FlashAmount"), 0f, "Flash complete at .3 seconds");
            Require(!ShaderUtil.ShaderHasError(images[2].material.shader), "Flash shader import");
            var deadlinesBefore = inventory.GetQuickSlotCooldownFill(2);
            inventory.RestoreEquipmentAndQuickSlots(new InventoryItemDefinition[6], new[] { red, red, blue });
            Require(inventory.GetQuickSlot(0) == null && inventory.GetQuickSlot(1) == red && inventory.GetQuickSlot(2) == blue, "Restored bindings are unique");
            File.WriteAllText(resultPath, "PASS Unity " + Application.unityVersion + "\nHover registration, no auto-registration, category restriction, unique move, actual HP/MP recovery, repeat-use rejection, independent cooldowns, cooldown migration and progress, prefab icons, vertical overlay, .3 second flash, restore deduplication.\nPreview scenes only; no game assets saved.");
            Debug.Log("IN_GAME_HOTBAR_CHECK_PASS");
        }
        catch (Exception error)
        {
            File.WriteAllText(resultPath, "FAIL\n" + error);
            Debug.LogException(error);
        }
        finally
        {
            if (hudRoot != null) PrefabUtility.UnloadPrefabContents(hudRoot);
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static object Get(object value, string field) => value.GetType().GetField(field, Private).GetValue(value);
    private static void Set(object value, string field, object content) => value.GetType().GetField(field, Private).SetValue(value, content);
    private static void Call(object value, string method, params object[] args) => value.GetType().GetMethod(method, Private).Invoke(value, args);
    private static void Require(bool condition, string label) { if (!condition) throw new Exception(label); }
    private static void Near(float actual, float expected, string label) => Require(Mathf.Abs(actual - expected) < .03f, label);
}
