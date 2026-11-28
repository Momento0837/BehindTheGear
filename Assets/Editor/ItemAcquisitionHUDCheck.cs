#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Opt-in preview-scene validation; never enters Play mode or changes the active scene/save state.</summary>
[InitializeOnLoad]
public static class ItemAcquisitionHUDCheck
{
    private const string Folder = "Temp/ItemAcquisitionPopupValidation";
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static ItemAcquisitionHUDCheck() => EditorApplication.update += Poll;
    private static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Folder + "/run")) return;
        File.Delete(Folder + "/run");
        Run();
    }

    [MenuItem("Tools/Checks/Item Acquisition Stack (Preview Scene)")]
    public static void Run()
    {
        Directory.CreateDirectory(Folder);
        var previewScene = EditorSceneManager.NewPreviewScene();
        GameObject owner = null;
        InventoryItemDefinition first = null, sameId = null, second = null, third = null;
        try
        {
            owner = new GameObject("ItemAcquisition Validation (temporary)", typeof(Canvas));
            owner.hideFlags = HideFlags.HideAndDontSave;
            SceneManager.MoveGameObjectToScene(owner, previewScene);
            var source = new GameObject("Info style", typeof(RectTransform), typeof(Image));
            source.transform.SetParent(owner.transform, false);
            var template = Resources.Load<GameObject>("UI/InfoPopup");
            var templateImage = template != null ? template.GetComponent<Image>() : null;
            Require(templateImage != null, "Authored InfoPopup available");
            var sourceImage = source.GetComponent<Image>();
            sourceImage.sprite = templateImage.sprite;
            sourceImage.pixelsPerUnitMultiplier = templateImage.pixelsPerUnitMultiplier;
            var text = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            text.transform.SetParent(source.transform, false);
            var label = text.GetComponent<TextMeshProUGUI>();
            var style = Resources.Load<LocationHUDStyle>("Story/LocationHUDStyle");
            label.font = style != null ? style.font : TMP_Settings.defaultFontAsset;
            label.fontSize = 28f;
            var hud = ItemAcquisitionHUD.Create(owner.transform, sourceImage, label, 100f);
            first = Item("popup_check_a", "점검 아이템 A");
            sameId = Item("popup_check_a", "점검 아이템 A");
            second = Item("popup_check_b", "점검 아이템 B");
            third = Item("popup_check_c", "점검 아이템 C");
            hud.Show(first, 1);
            var entries = (IList)Read(hud, "notices");
            Require(entries.Count == 1, "First pickup creates a row");
            var a = entries[0];
            var aRect = (RectTransform)Read(a, "rect");
            Require(aRect.anchoredPosition.x < -aRect.rect.width * aRect.localScale.x,
                "New popup starts completely off-screen to the left");
            Step(hud, .2f);
            Require(aRect.anchoredPosition.x < -.1f, "Entrance moves smoothly instead of snapping");
            Step(hud, .3f);
            Require(Mathf.Abs(aRect.anchoredPosition.x - 24f) < .01f, "Entrance arrives at the screen margin");
            Require(aRect.GetComponent<Image>().type == Image.Type.Sliced && aRect.GetComponent<Image>().maskable, "Authored image uses sliced/maskable rendering");
            Require(((TMP_Text)Read(a, "label")).maskable, "Popup text participates in viewport clipping");
            var viewport = (RectTransform)Read(hud, "viewport");
            Require(Mathf.Abs(viewport.anchoredPosition.x) < .01f, "Left mask starts at the actual screen edge");
            hud.Show(second, 1);
            hud.Show(third, 1);
            Step(hud, .5f);
            Require(entries.Count == 3 && ReferenceEquals(entries[2], a), "Different items stack oldest above newest");
            float oldY = aRect.anchoredPosition.y;
            hud.Show(sameId, 2);
            Require(entries.Count == 3 && ReferenceEquals(entries[0], a), "Same ID merges across definition instances");
            Require((int)Read(a, "amount") == 3, "Merged quantity accumulates");
            Require(((TMP_Text)Read(a, "label")).text == "점검 아이템 A 3개 획득", "Merged count is shown in Korean");
            Require(Mathf.Approximately(aRect.anchoredPosition.y, oldY), "Refreshing starts from current row position");
            Step(hud, .15f);
            Require(aRect.anchoredPosition.y < oldY && aRect.anchoredPosition.y > ((Vector2)Read(a, "moveTo")).y,
                "Refreshed old item moves smoothly down to newest position");
            Step(hud, .25f);
            Require(Mathf.Abs(aRect.anchoredPosition.y - ((Vector2)Read(a, "moveTo")).y) < .01f, "Reordering reaches target");
            Step(hud, 2.7f);
            Require(entries.Count == 1 && ReferenceEquals(entries[0], a), "Independent old rows expire; refreshed item survives");
            float alpha = ((CanvasGroup)Read(a, "opacity")).alpha;
            Require(alpha > 0f && alpha < 1f, "The refreshed item fades only after its new three-second hold");
            Step(hud, .4f);
            Require(entries.Count == 0, "Expired rows are removed after fade-out");
            hud.Show(first, 1);
            hud.Show(second, 1);
            hud.Clear();
            Require(entries.Count == 0, "Scene dismissal clears all acquisition notices");
            string result = "PASS: off-screen entry, smooth movement, different-item stacking, same-ID merging, quantity text, newest-bottom reorder, independent three-second lifetime, fade-out, cleanup.";
            File.WriteAllText(Folder + "/result.txt", result);
            Debug.Log("[ItemAcquisitionCheck] " + result);
        }
        catch (Exception error)
        {
            File.WriteAllText(Folder + "/result.txt", "FAIL: " + error);
            Debug.LogException(error);
        }
        finally
        {
            if (owner != null) UnityEngine.Object.DestroyImmediate(owner);
            if (previewScene.IsValid()) EditorSceneManager.ClosePreviewScene(previewScene);
            foreach (var item in new[] { first, sameId, second, third })
                if (item != null) UnityEngine.Object.DestroyImmediate(item);
        }
    }

    private static InventoryItemDefinition Item(string id, string name)
    {
        var item = ScriptableObject.CreateInstance<InventoryItemDefinition>();
        typeof(InventoryItemDefinition).GetField("itemId", Fields).SetValue(item, id);
        typeof(InventoryItemDefinition).GetField("displayName", Fields).SetValue(item, name);
        return item;
    }
    private static object Read(object target, string name) => target.GetType().GetField(name, Fields).GetValue(target);
    private static void Step(ItemAcquisitionHUD hud, float seconds) => typeof(ItemAcquisitionHUD)
        .GetMethod("AdvanceNotices", Fields).Invoke(hud, new object[] { seconds });
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
#endif