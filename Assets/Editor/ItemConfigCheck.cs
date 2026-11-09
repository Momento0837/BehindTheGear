#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class ItemConfigCheck
{
    const string Folder = "Temp/ItemConfigValidation";
    const string Key = "BTG.ItemConfigCheck";
    static IEnumerator routine;
    static double deadline;
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly FieldInfo Source = typeof(InventoryItemCatalog).GetField("source", BindingFlags.Static | BindingFlags.NonPublic);
    static ItemConfigCheck() { EditorApplication.update += Tick; }
    [MenuItem("Tools/Checks/Item Config and Stats")]
    public static void Run()
    {
        if (Application.unityVersion != "6000.3.23f1" || EditorApplication.isPlayingOrWillChangePlaymode) return;
        Directory.CreateDirectory(Folder);
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }
    static object Read(object o, string field) => o.GetType().GetField(field, Private).GetValue(o);
    static void Call(object o, string method) => o.GetType().GetMethod(method, Private).Invoke(o, null);
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Tick()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && File.Exists(Folder + "/run"))
        { File.Delete(Folder + "/run"); Run(); }
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (routine == null) { routine = Check(); deadline = EditorApplication.timeSinceStartup + 60; }
            Require(EditorApplication.timeSinceStartup < deadline, "Timeout");
            if (routine.MoveNext()) return;
            Finish("PASS Unity " + Application.unityVersion + "\nAuthored JSON cloak text; inventory tooltip including name/positive/negative fields; live JSON reload; minus activation/refund/zero bound; equipment modifiers without refundable bonus; rarity stack counts; exact Status text/whitespace preservation; equipment removal.");
        }
        catch (Exception e) { Finish("FAIL\n" + e); Debug.LogException(e); }
    }
    static void Finish(string result)
    {
        Source.SetValue(null, Resources.Load<TextAsset>("Inventory/inventory_ui_text"));
        InventoryItemCatalog.ReloadIfChanged();
        File.WriteAllText(Folder + "/result.txt", result);
        SessionState.SetBool(Key, false);
        routine = null;
        EditorApplication.isPlaying = false;
    }
    static IEnumerator Check()
    {
        EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/Inventory/InventoryTestScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;
        var ui = UnityEngine.Object.FindFirstObjectByType<InventoryUI>();
        Require(ui != null && (bool)Read(ui, "ready"), "UI ready");
        var inv = ui.GetComponent<PlayerInventory>();
        var stats = ui.GetComponent<PlayerStats>();
        var cloak = Resources.Load<InventoryItemDefinition>("Story/TutorialGear");
        Require(cloak.DisplayName == "헤진 망토" && cloak.Description == "쓰레기 더미에서 주웠다.", "User authored cloak text resolves actual asset id");
        // Temporary in-memory catalog only: do not alter the user's JSON for validation.
        const string fixture = "{\"items\":[{\"id\":\"tutorial_gear\",\"display\":{\"name\":\"검증 망토\",\"description\":\"검증 설명\"},\"rarity\":\"Rare\",\"stats\":{\"increase\":{\"Attack\":3},\"decrease\":{\"Stealth\":2}}},{\"id\":\"health_potion\",\"display\":{\"name\":\"검증 포션\",\"description\":\"회복\"},\"rarity\":\"Advanced\"}]}";
        Source.SetValue(null, new TextAsset(fixture)); InventoryItemCatalog.ReloadIfChanged();
        inv.Add(cloak, 1);
        var potion = AssetDatabase.LoadAssetAtPath<InventoryItemDefinition>("Assets/Scenes/Inventory/Items/HealthPotion.asset");
        inv.Add(potion, 2);
        var filterType = typeof(InventoryUI).GetNestedType("Filter", BindingFlags.NonPublic);
        typeof(InventoryUI).GetMethod("SetFilter", Private).Invoke(ui, new[] { Enum.Parse(filterType, "Equipment") });
        Call(ui, "ToggleInventory");
        yield return null;
        var buttons = (Button[])Read(ui, "statMinusButtons");
        int original = stats.GetBaseValue(PersonalStatType.Evasion);
        while (stats.GetBaseValue(PersonalStatType.Evasion) > 0) stats.TryDowngrade(PersonalStatType.Evasion, out _);
        Require(!buttons[0].interactable, "Minus disabled at zero");
        int points = stats.AvailablePoints;
        Require(stats.TryUpgrade(PersonalStatType.Evasion, out _), "Increase stat");
        Require(buttons[0].interactable, "Minus enabled at one");
        buttons[0].onClick.Invoke();
        Require(stats.GetBaseValue(PersonalStatType.Evasion) == 0 && stats.AvailablePoints == points && !buttons[0].interactable, "Minus returns point and disables");
        Require(!stats.TryDowngrade(PersonalStatType.Evasion, out _) && stats.AvailablePoints == points, "Zero cannot refund");
        var slots = (InventorySlotUI[])Read(ui, "slots");
        int display = Array.FindIndex(slots, s => ((PlayerInventory.Slot)typeof(InventoryUI).GetMethod("GetDisplayedSlot", Private).Invoke(ui, new object[] {Array.IndexOf(slots, s)}))?.Item == cloak);
        Require(display >= 0, "Cloak in inventory");
        var data = new PointerEventData(EventSystem.current);
        ExecuteEvents.Execute(slots[display].gameObject, data, ExecuteEvents.pointerEnterHandler);
        var info = ui.SceneCanvas.transform.Find("Info_Equip");
        Require(info.gameObject.activeSelf, "Inventory item shows description panel");
        Require(info.Find("Equip_Name").GetComponent<TMP_Text>().text == "검증 망토", "Dynamic tooltip title");
        Require(info.Find("Equip_Desc").GetComponent<TMP_Text>().text == "검증 설명", "Dynamic tooltip description");
        Require(info.Find("Equip_Up").GetComponent<TMP_Text>().text == "Atk +3" && info.Find("Equip_Down").GetComponent<TMP_Text>().text == "Ste -2", "Signed tooltip modifiers");
        Source.SetValue(null, new TextAsset(fixture.Replace("검증 설명", "갱신 설명"))); InventoryItemCatalog.ReloadIfChanged();
        double until = EditorApplication.timeSinceStartup + .7;
        while (EditorApplication.timeSinceStartup < until) yield return null;
        Require(info.Find("Equip_Desc").GetComponent<TMP_Text>().text == "갱신 설명", "Hover updates after catalog edit");
        var status = (TMP_Text)Read(ui, "capacityText");
        string template = ((Dictionary<TMP_Text, string>)Read(ui, "authoredText"))[status];
        Require(Regex.Replace(status.text, @"\d+", "#") == Regex.Replace(template, @"\d+", "#"), "Status whitespace and words preserved");
        int rare = 0, advanced = 0;
        for(int i=0;i<inv.Capacity;i++) { var slot=inv.GetSlot(i); if(slot.IsEmpty)continue; if(slot.Item.Rarity==InventoryItemRarity.Rare)rare+=slot.Amount; if(slot.Item.Rarity==InventoryItemRarity.Advanced)advanced+=slot.Amount; }
        Require(Regex.IsMatch(status.text, @"Rare\s+" + rare + @"\b") && Regex.IsMatch(status.text, @"Advenced\s+" + advanced + @"\b"), "Rarity stack counts");
        int attack = stats.GetBaseValue(PersonalStatType.Attack);
        int stealth = stats.GetBaseValue(PersonalStatType.Stealth);
        ui.DoubleClick(display);
        Require(stats.Attack == attack+3 && stats.Stealth == Mathf.Max(0,stealth-2), "Equipped increases and decreases applied");
        Require(stats.GetBaseValue(PersonalStatType.Attack) == attack, "Gear cannot create refundable stats");
        Require(inv.Unequip(cloak.Slot) && stats.Attack == attack && stats.Stealth == stealth, "Unequip restores stats");
        ExecuteEvents.Execute(slots[display].gameObject, data, ExecuteEvents.pointerEnterHandler);
        ScreenCapture.CaptureScreenshot(Folder + "/tooltip.png");
        until = EditorApplication.timeSinceStartup + .3;
        while (EditorApplication.timeSinceStartup < until) yield return null;
    }
}
#endif
