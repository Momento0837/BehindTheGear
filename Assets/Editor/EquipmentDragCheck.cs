#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class EquipmentDragCheck
{
    const string Folder = "Temp/EquipmentDragValidation";
    const string Key = "BTG.EquipmentDragCheck";
    static IEnumerator routine;
    static double deadline;
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static EquipmentDragCheck() { EditorApplication.update += Tick; }
    [MenuItem("Tools/Checks/Equipment Drag")]
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
            Finish("PASS Unity " + Application.unityVersion + "\nJSON tooltip; hover and drag highlight at 105%; mouse-follow icon; wrong-slot rejection and .5 second return; matching-slot raycast/drop; swap; double-click equip; close cleanup.");
        }
        catch (Exception e) { Finish("FAIL\n" + e); Debug.LogException(e); }
    }
    static void Finish(string result)
    {
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
        Require(ui != null && (bool)Read(ui, "ready"), "Inventory UI initialized");
        var inventory = ui.GetComponent<PlayerInventory>();
        var gear = Resources.Load<InventoryItemDefinition>("Story/TutorialGear");
        Require(inventory.Add(gear, 1) == 1, "Add cloak");
        var filterType = typeof(InventoryUI).GetNestedType("Filter", BindingFlags.NonPublic);
        typeof(InventoryUI).GetMethod("SetFilter", Private).Invoke(ui, new[] { Enum.Parse(filterType, "Equipment") });
        Call(ui, "ToggleInventory");
        yield return null;
        var slots = (InventorySlotUI[])Read(ui, "slots");
        var targets = (EquipmentSlotDropTarget[])Read(ui, "equipmentDropTargets");
        int display = Array.FindIndex(slots, s => {
            int index = Array.IndexOf(slots, s);
            return ((PlayerInventory.Slot)typeof(InventoryUI).GetMethod("GetDisplayedSlot", Private).Invoke(ui, new object[] { index }))?.Item == gear;
        });
        Require(display >= 0, "Cloak visible in equipment tab");
        var source = slots[display];
        var target = targets[2];
        Vector3 normalScale = target.transform.localScale;
        var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, pointerDrag = source.gameObject };
        ExecuteEvents.Execute(source.gameObject, data, ExecuteEvents.pointerEnterHandler);
        double until = EditorApplication.timeSinceStartup + .2;
        while (EditorApplication.timeSinceStartup < until) yield return null;
        Require(Vector3.Distance(target.transform.localScale, normalScale * 1.05f) < .001f, "Hover scales matching slot 105%");
        ui.ShowDetails(display);
        var detail = (TMPro.TMP_Text)Read(ui, "detailText");
        Require(detail.text == InventoryUITextCatalog.Load(null).ItemDescription(gear), "Tooltip uses JSON");
        data.position = new Vector2(Screen.width * .65f, Screen.height * .5f);
        ExecuteEvents.Execute(source.gameObject, data, ExecuteEvents.beginDragHandler);
        ExecuteEvents.Execute(source.gameObject, data, ExecuteEvents.pointerExitHandler);
        ExecuteEvents.Execute(source.gameObject, data, ExecuteEvents.dragHandler);
        var icon = (Image)Read(ui, "dragIcon");
        Require(icon.enabled && icon.gameObject.activeInHierarchy && !icon.raycastTarget, "Visible non-blocking drag icon");
        Require(Vector2.Distance(RectTransformUtility.WorldToScreenPoint(null, icon.transform.position), data.position) < 1f, "Icon follows pointer");
        ExecuteEvents.Execute(targets[0].gameObject, data, ExecuteEvents.dropHandler);
        Require(inventory.GetEquipped(gear.Slot) != gear, "Wrong slot rejects cloak");
        Vector3 start = icon.transform.position;
        ExecuteEvents.Execute(source.gameObject, data, ExecuteEvents.endDragHandler);
        until = EditorApplication.timeSinceStartup + .2;
        while (EditorApplication.timeSinceStartup < until) yield return null;
        Require(icon.gameObject.activeSelf && Vector3.Distance(start, icon.transform.position) > 1f, "Rejected drag animates back");
        until = EditorApplication.timeSinceStartup + .4;
        while (EditorApplication.timeSinceStartup < until) yield return null;
        Require(!icon.gameObject.activeSelf && Mathf.Abs(target.transform.localScale.x - normalScale.x) < .001f, "Return clears icon and highlight");
        ExecuteEvents.Execute(source.gameObject, data, ExecuteEvents.beginDragHandler);
        data.position = RectTransformUtility.WorldToScreenPoint(null, target.transform.TransformPoint(((RectTransform)target.transform).rect.center));
        ExecuteEvents.Execute(source.gameObject, data, ExecuteEvents.dragHandler);
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(data, hits);
        Require(hits.Count > 0 && ExecuteEvents.GetEventHandler<IDropHandler>(hits[0].gameObject) == target.gameObject, "Empty matching slot receives actual raycast");
        ExecuteEvents.Execute(target.gameObject, data, ExecuteEvents.dropHandler);
        ExecuteEvents.Execute(source.gameObject, data, ExecuteEvents.endDragHandler);
        Require(inventory.GetEquipped(gear.Slot) == gear && !icon.gameObject.activeSelf, "Drop equips and hides ghost");
        until = EditorApplication.timeSinceStartup + .2;
        while (EditorApplication.timeSinceStartup < until) yield return null;
        Require(Vector3.Distance(target.transform.localScale, normalScale * 1.05f) < .001f, "Success keeps highlight");
        ScreenCapture.CaptureScreenshot(Folder + "/equipped.png");
        until = EditorApplication.timeSinceStartup + .2;
        while (EditorApplication.timeSinceStartup < until) yield return null;
        var replacement = UnityEngine.Object.Instantiate(gear);
        JsonUtility.FromJsonOverwrite("{\"itemId\":\"validation_cloak\"}", replacement);
        Require(inventory.Add(replacement, 1) == 1, "Add replacement");
        int replaceIndex = Array.FindIndex(slots, s => ((PlayerInventory.Slot)typeof(InventoryUI).GetMethod("GetDisplayedSlot", Private).Invoke(ui, new object[] { Array.IndexOf(slots, s) }))?.Item == replacement);
        ui.BeginDrag(replaceIndex, data);
        ui.DropEquipment(2);
        ui.EndDrag();
        Require(inventory.GetEquipped(gear.Slot) == replacement && Enumerable.Range(0, inventory.Capacity).Any(i => inventory.GetSlot(i).Item == gear), "Swap preserves previous equipment");
        int oldIndex = Array.FindIndex(slots, s => ((PlayerInventory.Slot)typeof(InventoryUI).GetMethod("GetDisplayedSlot", Private).Invoke(ui, new object[] { Array.IndexOf(slots, s) }))?.Item == gear);
        ui.DoubleClick(oldIndex);
        Require(inventory.GetEquipped(gear.Slot) == gear, "Double click still equips");
        Call(ui, "CloseInventory");
        Require(!icon.gameObject.activeSelf && Vector3.Distance(target.transform.localScale, normalScale) < .001f, "Close restores visuals");
    }
}
#endif
