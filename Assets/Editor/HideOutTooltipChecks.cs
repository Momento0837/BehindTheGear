using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BehindTheGear.HideOut;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Explicit live UI checks. No scene changes, clicks, save writes or Play restart.</summary>
[InitializeOnLoad]
public static class HideOutTooltipChecks
{
    private const string Output = "Temp/HideOutTooltipValidation/";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly StringBuilder Report = new();
    private static int checks;
    static HideOutTooltipChecks() => EditorApplication.update += Tick;

    private static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || !Application.isPlaying) return;
        if (File.Exists(Output + "request.flag")) { File.Delete(Output + "request.flag"); Run(); }
    }

    private static object Read(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
    private static object Call(object target, string method, params object[] values) => target.GetType().GetMethod(method, Private).Invoke(target, values);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++; Report.AppendLine("PASS " + message);
    }
    private static string Progress(HideOutPlayerBridge player)
    {
        var copy = JsonUtility.FromJson<HideOutSave>(JsonUtility.ToJson(player.State)); copy.activeClock = 0;
        return JsonUtility.ToJson(copy);
    }
    private static void Hover(Button button, bool enter)
    {
        var data = new PointerEventData(EventSystem.current)
        { position = RectTransformUtility.WorldToScreenPoint(null, button.GetComponent<RectTransform>().TransformPoint(button.GetComponent<RectTransform>().rect.center)) };
        if (enter) ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerEnterHandler);
        else ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerExitHandler);
    }
    private static void CheckPanel(RectTransform panel, TMP_Text text)
    {
        Canvas.ForceUpdateCanvases(); text.ForceMeshUpdate();
        var parent = (RectTransform)panel.parent;
        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(parent, panel);
        Check(bounds.min.x >= parent.rect.xMin + 9f && bounds.max.x <= parent.rect.xMax - 9f
            && bounds.min.y >= parent.rect.yMin + 9f && bounds.max.y <= parent.rect.yMax - 9f,
            "Tooltip and text rectangles fit inside the canvas with a visible margin.");
        Check(!text.isTextOverflowing && text.textInfo.characterCount > 0, "All tooltip text fits without overflow.");
    }

    [MenuItem("Tools/HideOut/Verify facility tooltips during Play")]
    public static void Run()
    {
        if (!Application.isPlaying || Application.unityVersion != "6000.3.23f1")
            throw new InvalidOperationException("Requires the open hideout in Unity 6000.3.23f1 Play mode.");
        var view = Object.FindObjectsByType<HideOutScene>(FindObjectsSortMode.None)
            .First(candidate => candidate.gameObject.activeInHierarchy && candidate.TrainingPlayer != null);
        Directory.CreateDirectory(Output); Report.Clear(); checks = 0;
        var tooltip = (GameObject)Read(view, "tooltip");
        var panel = tooltip.GetComponent<RectTransform>();
        var text = (TMP_Text)Read(view, "tooltipText");
        string before = Progress(view.TrainingPlayer);
        Vector3 playerPosition = view.TrainingPlayer.transform.position;
        var camera = (Camera)Read(view, "hideoutCamera");
        Vector3 cameraPosition = camera.transform.position;
        float cameraSize = camera.orthographicSize;
        GameObject temporaryAnchor = null;
        Exception failure = null;
        try
        {
            var buttons = view.GetComponentsInChildren<Button>(true);
            var icons = new Dictionary<Facility, Button>();
            foreach (Facility facility in view.settings.topView.shortcutOrder)
            {
                string label = (string)Call(view, "DisplayName", facility);
                var button = buttons.Single(candidate => candidate.name == label + " Facility Icon");
                icons.Add(facility, button);
                // Runtime-built UnityEvent listeners need rebinding after an editor hot reload.
                // Use the same production binding as fresh UI, without rebuilding the user's live scene.
                Call(view, "AddFacilityHover", button, facility, button.GetComponent<RectTransform>());
                var shortcut = buttons.Single(candidate => candidate.name == "Shortcut " + facility);
                Call(view, "AddFacilityHover", shortcut, facility, shortcut.GetComponent<RectTransform>());
            }
            var dummyTarget = (Button)Read(view, "dummyTargetButton");
            dummyTarget.transform.SetAsFirstSibling();
            Call(view, "AddFacilityHover", dummyTarget, Facility.Dummy, icons[Facility.Dummy].GetComponent<RectTransform>());

            foreach (var pair in icons)
            {
                var anchor = pair.Value.GetComponent<RectTransform>();
                Hover(pair.Value, true);
                Check(tooltip.activeSelf && text.text.StartsWith((string)Call(view, "DisplayName", pair.Key)),
                    pair.Key + ": real PointerEnter shows the corresponding facility information.");
                Check((RectTransform)Read(view, "tooltipAnchor") == anchor, pair.Key + ": tooltip follows this icon rather than a fixed clinic coordinate.");
                CheckPanel(panel, text);
                var corners = new Vector3[4]; anchor.GetWorldCorners(corners);
                var parent = (RectTransform)panel.parent;
                Vector3 topRight = parent.InverseTransformPoint(corners[2]);
                if (parent.rect.xMax - topRight.x > panel.rect.width + 26 && parent.rect.yMax - topRight.y > panel.rect.height + 22)
                {
                    panel.GetWorldCorners(corners);
                    Vector3 bottomLeft = parent.InverseTransformPoint(corners[0]);
                    Check(bottomLeft.x > topRight.x && bottomLeft.y > topRight.y,
                        pair.Key + ": the whole tooltip is above and to the right of the hovered icon.");
                }
                Hover(pair.Value, false);
                Check(!tooltip.activeSelf, pair.Key + ": PointerExit hides the panel.");
            }
            Hover(dummyTarget, true);
            Check(tooltip.activeSelf && text.text.StartsWith((string)Call(view, "DisplayName", Facility.Dummy))
                && (RectTransform)Read(view, "tooltipAnchor") == icons[Facility.Dummy].GetComponent<RectTransform>(),
                "The dummy's transparent body target also shows the tooltip anchored to its facility icon.");
            Hover(dummyTarget, false);
            var pointer = new PointerEventData(EventSystem.current)
            { position = RectTransformUtility.WorldToScreenPoint(null, icons[Facility.Dummy].transform.TransformPoint(icons[Facility.Dummy].GetComponent<RectTransform>().rect.center)) };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            var topButton = hits.Count > 0 ? hits[0].gameObject.GetComponentInParent<Button>() : null;
            Check(topButton == icons[Facility.Dummy] || topButton == dummyTarget,
                "Actual UI raycasting over the dummy reaches its icon or its existing body click target.");
            Hover(topButton, true);
            Check(tooltip.activeSelf && text.text.StartsWith((string)Call(view, "DisplayName", Facility.Dummy))
                && (RectTransform)Read(view, "tooltipAnchor") == icons[Facility.Dummy].GetComponent<RectTransform>(),
                "The topmost actual dummy raycast target shows the correct tooltip at the icon.");
            Hover(topButton, false);
            foreach (var pair in icons)
            {
                var shortcut = buttons.Single(candidate => candidate.name == "Shortcut " + pair.Key);
                Hover(shortcut, true);
                Check(tooltip.activeSelf && (RectTransform)Read(view, "tooltipAnchor") == shortcut.GetComponent<RectTransform>(),
                    pair.Key + ": shortcut hover anchors to the shortcut itself, outside its scroll mask.");
                CheckPanel(panel, text); Hover(shortcut, false);
            }
            Check(tooltip.GetComponentsInChildren<Graphic>(true).All(graphic => !graphic.raycastTarget),
                "Tooltip graphics do not block mouse hover or facility clicks.");

            temporaryAnchor = new GameObject("Temporary tooltip edge check", typeof(RectTransform));
            var testAnchor = temporaryAnchor.GetComponent<RectTransform>();
            testAnchor.SetParent(panel.parent, false); testAnchor.sizeDelta = Vector2.one * 80;
            var parentRect = (RectTransform)panel.parent;
            foreach (float x in new[] { parentRect.rect.xMin + 5, parentRect.rect.xMax - 5 })
            foreach (float y in new[] { parentRect.rect.yMin + 5, parentRect.rect.yMax - 5 })
            {
                testAnchor.localPosition = new Vector3(x, y, 0);
                Call(view, "ShowLockedTooltip", testAnchor); CheckPanel(panel, text);
            }
            testAnchor.localPosition = parentRect.rect.center;
            Call(view, "ShowFacilityTooltip", Facility.Clinic, testAnchor);
            Vector3 oldPanel = panel.position, oldAnchor = testAnchor.position;
            testAnchor.localPosition += new Vector3(-40, -20, 0);
            Call(view, "PositionTooltip");
            Check(Vector3.Distance(panel.position - oldPanel, testAnchor.position - oldAnchor) < .1f,
                "An open tooltip tracks icon movement in canvas coordinates.");
            temporaryAnchor.SetActive(false); Call(view, "PositionTooltip");
            Check(!tooltip.activeSelf, "Hiding the source icon also hides its tooltip.");
            Check(Progress(view.TrainingPlayer) == before && view.TrainingPlayer.transform.position == playerPosition
                && camera.transform.position == cameraPosition && camera.orthographicSize == cameraSize,
                "Hover checks preserve the live player's data, position and current camera view.");
        }
        catch (Exception error) { failure = error; }
        finally
        {
            Call(view, "HideTooltip");
            if (temporaryAnchor != null) Object.DestroyImmediate(temporaryAnchor);
            if (failure != null) Report.AppendLine("FAIL " + failure);
            Report.AppendLine($"Unity {Application.unityVersion}; {checks} checks; {(failure == null ? "PASSED" : "FAILED")}");
            File.WriteAllText(Output + "result.txt", Report.ToString());
        }
    }
}
