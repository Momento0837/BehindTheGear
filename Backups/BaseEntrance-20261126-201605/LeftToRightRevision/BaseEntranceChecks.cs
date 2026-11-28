using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BehindTheGear.HideOut;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class BaseEntranceChecks
{
    private const string Folder = "Temp/BaseEntrance/";
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly StringBuilder Report = new();
    private static bool running;
    private static int errors;
    static BaseEntranceChecks() => EditorApplication.update += Poll;
    private static void Poll()
    {
        if (!File.Exists(Folder + "check.request") || !EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        var view = Object.FindFirstObjectByType<HideOutScene>();
        if (view?.TrainingPlayer?.Initialized != true) return;
        File.Delete(Folder + "check.request");
        if (running || Application.unityVersion != "6000.3.23f1" || !view.TrainingPlayer.Sandbox)
            throw new InvalidOperationException("Run entrance checks in Unity 6000.3.23f1 direct Base_Test sandbox only.");
        running = true; errors = 0; Report.Clear();
        Application.logMessageReceived += Observe;
        view.TrainingPlayer.StartCoroutine(Run(view));
    }
    private static object Read(object item, string name) => item.GetType().GetField(name, Fields).GetValue(item);
    private static void Invoke(HideOutScene view, string method, params object[] args)
        => typeof(HideOutScene).GetMethod(method, Fields).Invoke(view, args);
    private static void Observe(string message, string trace, LogType type)
    {
        if (type is LogType.Error or LogType.Exception) { errors++; Report.AppendLine("ERROR " + message); }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Report.AppendLine("PASS " + message);
    }
    private static IEnumerator Run(HideOutScene view)
    {
        var steps = Verify(view); bool success = true;
        while (true)
        {
            object step;
            try { if (!steps.MoveNext()) break; step = steps.Current; }
            catch (Exception e) { Report.AppendLine("FAIL " + e); success = false; break; }
            yield return step;
        }
        Application.logMessageReceived -= Observe;
        Report.AppendLine("Runtime errors: " + errors);
        Report.AppendLine(success && errors == 0 ? "PASS entrance checks" : "FAIL entrance checks");
        File.WriteAllText(Folder + "check-result.txt", Report.ToString());
        running = false;
        EditorApplication.isPlaying = false;
    }
    private static Color Capture(string name, Vector3? sample = null)
    {
        var texture = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Folder + name + ".png", texture.EncodeToPNG());
        Color result = sample.HasValue ? texture.GetPixel(Mathf.RoundToInt(sample.Value.x), Mathf.RoundToInt(sample.Value.y)) : Color.clear;
        Object.Destroy(texture);
        return result;
    }
    private static IEnumerator Verify(HideOutScene view)
    {
        var entry = view.GetComponent<BaseEntrancePresentation>();
        var environment = view.spriteEnvironment;
        var controller = Object.FindFirstObjectByType<HideOutCameraController>();
        var camera = controller.GetComponent<Camera>();
        var canvas = view.GetComponentInChildren<HideOutCanvasView>(true);
        Require(entry != null && entry.IsPlaying, "entry starts on initial base creation");
        var maps = ((IList)Read(entry, "map")).Cast<object>().ToArray();
        var icons = ((IList)Read(entry, "icons")).Cast<object>().ToArray();
        var fog = ((IList)Read(entry, "clouds")).Cast<object>().ToArray();
        var sweeps = ((IList)Read(entry, "sweeps")).Cast<SpriteRenderer>().ToArray();
        Require(maps.Length >= 10 && icons.Length == 8 && fog.Length > 0 && sweeps.Length == 3, "captures objects, eight icons, locked fog and exactly three temporary sweeps");
        foreach (var group in new[] { maps, icons, fog })
        {
            var distances = group.Select(v => (float)Read(v, "distance")).ToArray();
            Require(distances.SequenceEqual(distances.OrderBy(d => d)), "elements sorted nearest screen center first");
        }
        Require((float)Read(icons[^1], "distance") - (float)Read(icons[0], "distance") > 50, "icon ordering uses positioned icons rather than prefab origin");
        Require(controller.PresentationLocked && !controller.TryFocus(environment.areas[0].focusAnchor, 2, null), "camera input and focus are blocked during entry");
        Invoke(view, "SelectFacility", Facility.Dining);
        Require(!(bool)Read(view, "hasSelection"), "facility clicks cannot interrupt entry");
        Require(view.GetComponentsInChildren<GraphicRaycaster>(true).All(r => !r.enabled), "base UI input stays locked during entry");
        var shader = Resources.Load<Shader>("Effects/BaseEntranceFog");
        Require(shader != null && shader.isSupported && !ShaderUtil.ShaderHasError(shader), "temporary fog shader compiles");
        Vector4 tint = sweeps[0].sharedMaterial.GetVector("_Tint");
        Require(Mathf.Abs(tint.x - 31f / 255) < .0001f && tint.x == tint.y && tint.y == tint.z, "temporary fog tint is #1f1f1f");
        int frontMapLayer = maps.SelectMany(m => (SpriteRenderer[])Read(m, "sprites")).Max(r => SortingLayer.GetLayerValueFromID(r.sortingLayerID));
        Require(sweeps.All(s => SortingLayer.GetLayerValueFromID(s.sortingLayerID) >= frontMapLayer)
            && canvas.GetComponent<Canvas>().renderMode == RenderMode.ScreenSpaceOverlay, "render hierarchy keeps UI icons above fog above the map");
        var states = new List<string>();
        var sweepFirst = new float?[3]; var sweepLast = new float?[3];
        var pictured = new HashSet<string>();
        bool visibleSweep = false;
        float stageStarted = Time.unscaledTime, started = Time.unscaledTime;
        string previous = "";
        while (entry.IsPlaying && Time.unscaledTime - started < 12)
        {
            string stage = entry.Stage;
            if (stage != previous) { states.Add(stage); previous = stage; stageStarted = Time.unscaledTime; }
            if (stage != "Map" && maps.Any(m => ((SpriteRenderer[])Read(m, "sprites")).Any(r => r.color.a != 0)))
                throw new InvalidOperationException("Map reveals before its stage.");
            if (stage is "Icons" or "Sweeps" && fog.Any(m => ((SpriteRenderer[])Read(m, "sprites")).Any(r => r.color.a != 0)))
                throw new InvalidOperationException("Locked fog reveals before its stage.");
            if (BaseArrivalTutorial.IsShowing) throw new InvalidOperationException("Story dialogue interrupted entry.");
            if (stage == "Icons")
            {
                float near = ((Transform)Read(icons[0], "target")).localScale.x / ((Vector3)Read(icons[0], "scale")).x;
                float far = ((Transform)Read(icons[^1], "target")).localScale.x / ((Vector3)Read(icons[^1], "scale")).x;
                if (near + .001f < far) throw new InvalidOperationException("Outer icon overtook the center icon.");
            }
            for (int i = 0; i < 3; i++)
                if (sweeps[i] != null && sweeps[i].color.a > .15f)
                { sweepFirst[i] ??= sweeps[i].transform.position.x; sweepLast[i] = sweeps[i].transform.position.x; }
            bool pictureReady = Time.unscaledTime - stageStarted > (stage == "Fog" ? .06f : .25f);
            if (stage == "Sweeps")
            {
                Vector3 center = sweeps[0].bounds.center;
                float x = camera.WorldToViewportPoint(center).x;
                pictureReady = x > .45f && x < .55f && sweeps[0].color.a > .4f;
            }
            if (pictureReady && pictured.Add(stage))
            {
                yield return new WaitForEndOfFrame();
                Color pixel = Capture(stage, stage == "Sweeps" ? camera.WorldToScreenPoint(sweeps[0].bounds.center) : (Vector3?)null);
                if (stage == "Sweeps")
                {
                    Report.AppendLine("Sweep center rendered color=" + pixel);
                    visibleSweep = pixel.r > .025f && Mathf.Abs(pixel.r - pixel.g) < .02f && Mathf.Abs(pixel.g - pixel.b) < .02f;
                }
            }
            yield return null;
        }
        Require(!entry.IsPlaying && states.Where(s => s != "WaitingForFade").SequenceEqual(new[] { "Icons", "Sweeps", "Fog", "Map" }), "plays icons -> lateral sweeps -> fog -> map in order");
        int[] directions = Enumerable.Range(0, 3).Select(i => Math.Sign((sweepLast[i] ?? 0) - (sweepFirst[i] ?? 0))).ToArray();
        Report.AppendLine("Measured sweep directions: " + string.Join(",", directions));
        Require(directions[0] != 0 && directions[0] == directions[2] && directions[0] == -directions[1], "three sweeps alternate left/right/left or right/left/right");
        Require(visibleSweep, "gray sweep visibly renders over the black background");
        yield return null;
        foreach (var item in maps.Concat(icons).Concat(fog))
        {
            var target = (Transform)Read(item, "target");
            if (target.localScale != (Vector3)Read(item, "scale")) throw new InvalidOperationException("Scale was not restored: " + target.name);
        }
        Require(maps.All(m => ((Transform)Read(m, "target")).localPosition == (Vector3)Read(m, "position")), "map placement and authored scales are exactly restored");
        Require(!controller.PresentationLocked && view.GetComponentsInChildren<GraphicRaycaster>(true).All(r => r.enabled), "camera and UI input restored");
        Require(sweeps.All(s => s == null), "temporary fog is removed after its entrance");
        Require(!view.TrainingPlayer.State.baseTutorialComplete && canvas.facilities.Single(f => f.facility == Facility.Clinic).button.interactable == false, "entry preserves locked-region story progression");
        yield return new WaitForEndOfFrame(); Capture("Complete");

        float overviewSize = camera.orthographicSize;
        float original = environment.Find(Facility.Dining).focusSize;
        Invoke(view, "SelectFacility", Facility.Dining);
        float focusStarted = Time.unscaledTime;
        while (controller.IsMoving && Time.unscaledTime - focusStarted < 5) yield return null;
        Require(Mathf.Abs(camera.orthographicSize - original / .8f) < .001f, "real facility click uses 80 percent of the previous magnification");
        Require(Mathf.Abs(overviewSize / camera.orthographicSize / (overviewSize / original) - .8f) < .001f, "measured camera magnification ratio is 0.8");
        yield return new WaitForEndOfFrame(); Capture("Focus80Percent");
        Invoke(view, "ReturnToOverview");
        while (controller.IsMoving) yield return null;
        environment.gameObject.SetActive(false); yield return null;
        environment.gameObject.SetActive(true); yield return null;
        Require(!entry.IsPlaying, "returning from a facility does not replay entry");
        Require(!ShaderUtil.ShaderHasError(shader), "used shader variant remains error-free");
    }
}
