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
using UnityEngine.Rendering.Universal;
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
    private static float Expansion(object visual, float startingScale)
        => Mathf.Clamp01((((Transform)Read(visual, "target")).localScale.x / ((Vector3)Read(visual, "scale")).x - startingScale) / (1 - startingScale));
    private static IEnumerator Verify(HideOutScene view)
    {
        var entry = view.GetComponent<BaseEntrancePresentation>();
        var environment = view.spriteEnvironment;
        var controller = Object.FindFirstObjectByType<HideOutCameraController>();
        var camera = controller.GetComponent<Camera>();
        var canvas = view.GetComponentInChildren<HideOutCanvasView>(true);
        var lighting = environment.GetComponent<BaseAreaLighting>();
        var lightBindings = ((IList)Read(lighting, "lights")).Cast<object>().ToArray();
        var allLights = environment.gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Light2D>(true)).ToArray();
        var globalLights = allLights.Where(l => l.lightType == Light2D.LightType.Global).ToDictionary(l => l, l => l.intensity);
        Require(lightBindings.Length == 8 && lightBindings.Select(b => (Facility)Read(b, "facility")).Distinct().Count() == 8,
            "all eight named area lights bind to distinct facilities");
        Require(lightBindings.All(b => ((Light2D)Read(b, "light")).intensity == 0), "all area lights start at zero before their entrance");
        bool gradualInitialLighting = false;
        Require(entry != null && entry.IsPlaying, "entry starts on initial base creation");
        var maps = ((IList)Read(entry, "map")).Cast<object>().ToArray();
        var icons = ((IList)Read(entry, "icons")).Cast<object>().ToArray();
        Require(icons.All(icon => canvas.facilities.Single(f => f.facility == (Facility)Read(icon, "facility")).worldRoot
            == (Transform)Read(icon, "target")), "each entrance icon retains its actual facility identity after sorting");
        var fog = ((IList)Read(entry, "clouds")).Cast<object>().ToArray();
        var sweeps = ((IList)Read(entry, "sweeps")).Cast<SpriteRenderer>().ToArray();
        Require(maps.Length >= 10 && icons.Length == 8 && fog.Length > 0 && sweeps.Length == 3, "captures objects, eight icons, locked fog and exactly three temporary sweeps");
        foreach (var group in new[] { maps, icons, fog })
        {
            var distances = group.Select(v => (float)Read(v, "distance")).ToArray();
            Require(distances.SequenceEqual(distances.OrderBy(d => d)), group == icons ? "icons sorted by screen X, left to right" : "world elements sorted nearest screen center first");
        }
        Require((float)Read(icons[^1], "distance") - (float)Read(icons[0], "distance") > 50, "icon ordering uses positioned icons rather than prefab origin");
        Require(controller.PresentationLocked && !controller.TryFocus(environment.areas[0].focusAnchor, 2, null), "camera input and focus are blocked during entry");
        Invoke(view, "SelectFacility", Facility.Dining);
        Require(!(bool)Read(view, "hasSelection"), "facility clicks cannot interrupt entry");
        Require(view.GetComponentsInChildren<GraphicRaycaster>(true).All(r => !r.enabled), "base UI input stays locked during entry");
        var shader = Resources.Load<Shader>("Effects/BaseEntranceFog");
        Require(shader != null && shader.isSupported && !ShaderUtil.ShaderHasError(shader), "temporary fog shader compiles");
        Vector4 tint = sweeps[0].sharedMaterial.GetVector("_Tint");
        Require(Mathf.Abs(tint.x - 31f / 255 * .3f) < .0001f && tint.x == tint.y && tint.y == tint.z, "temporary fog tint is 70 percent darker than #1f1f1f");
        int frontMapLayer = maps.SelectMany(m => (SpriteRenderer[])Read(m, "sprites")).Max(r => SortingLayer.GetLayerValueFromID(r.sortingLayerID));
        Require(sweeps.All(s => SortingLayer.GetLayerValueFromID(s.sortingLayerID) >= frontMapLayer)
            && canvas.GetComponent<Canvas>().renderMode == RenderMode.ScreenSpaceOverlay, "render hierarchy keeps UI icons above fog above the map");
        var states = new List<string>();
        var sweepFirst = new float?[3]; var sweepLast = new float?[3];
        var sizeFirst = new float?[3]; var sizeLast = new float?[3];
        var pictured = new HashSet<string>();
        bool visibleSweep = false, iconFogOverlap = false, fogMapOverlap = false, fogLayersStaggered = false, mapContinuesAfterFog = false;
        var lightStartOrder = new List<Facility>();
        float maximumLightIconDifference = 0;
        float iconArrivalExpansion = -1;
        float iconsAtFogArrival = -1, fogFirstTime = -1, fogFullTime = -1, mapFirstTime = -1, mapFullTime = -1;
        float initialScale = environment.entrance.startingScale;
        float stageStarted = Time.unscaledTime, started = Time.unscaledTime;
        string previous = "";
        while (entry.IsPlaying && Time.unscaledTime - started < 30)
        {
            string stage = entry.Stage;
            foreach (var binding in lightBindings)
            {
                var light = (Light2D)Read(binding, "light");
                bool unlocked = HideOutModel.StoryUnlocked(view.TrainingPlayer.State, (Facility)Read(binding, "facility"));
                if ((!unlocked || stage == "Sweeps") && light.intensity != 0)
                    throw new InvalidOperationException("Area light revealed before its icon or story unlock: " + light.name);
                gradualInitialLighting |= unlocked && light.intensity > .001f && light.intensity < (float)Read(binding, "authoredIntensity") - .001f;
            }
            if (stage != previous) { states.Add(stage); previous = stage; stageStarted = Time.unscaledTime; }
            if (stage is "WaitingForFade" or "Sweeps" or "Icons" && maps.Any(m => ((SpriteRenderer[])Read(m, "sprites")).Any(r => r.color.a != 0)))
                throw new InvalidOperationException("Map reveals before its stage.");
            if (stage is "Sweeps" or "Icons" && fog.Any(m => ((SpriteRenderer[])Read(m, "sprites")).Any(r => r.color.a != 0)))
                throw new InvalidOperationException("Locked fog reveals before its stage.");
            if (BaseArrivalTutorial.IsShowing) throw new InvalidOperationException("Story dialogue interrupted entry.");
            if (stage == "Icons")
            {
                if (iconArrivalExpansion < 0 && ((CanvasGroup)Read(icons[0], "group")).alpha > 0)
                    iconArrivalExpansion = (sweeps[2].bounds.size.x / (2 * camera.orthographicSize * camera.aspect) - .18f) / 1.17f;
                float near = ((Transform)Read(icons[0], "target")).localScale.x / ((Vector3)Read(icons[0], "scale")).x;
                float far = ((Transform)Read(icons[^1], "target")).localScale.x / ((Vector3)Read(icons[^1], "scale")).x;
                if (near + .001f < far) throw new InvalidOperationException("Right icon overtook the left icon.");
            }
            bool fogVisible = fog.Any(f => Expansion(f, initialScale) > .0001f);
            bool mapVisible = maps.Any(m => Expansion(m, initialScale) > .0001f);
            if (fogVisible && iconsAtFogArrival < 0)
            {
                iconsAtFogArrival = icons.Average(i => Expansion(i, initialScale));
                fogFirstTime = Time.unscaledTime;
            }
            if (fogVisible && fogFullTime < 0 && fog.All(f => ((SpriteRenderer[])Read(f, "sprites"))
                .Select((r, i) => Mathf.Abs(r.color.a - ((Color[])Read(f, "colors"))[i].a)).All(d => d < .00001f)))
                fogFullTime = Time.unscaledTime;
            if (mapVisible && mapFirstTime < 0) mapFirstTime = Time.unscaledTime;
            if (mapVisible && mapFullTime < 0 && maps.All(m => ((SpriteRenderer[])Read(m, "sprites"))
                .Select((r, i) => Mathf.Abs(r.color.a - ((Color[])Read(m, "colors"))[i].a)).All(d => d < .00001f)))
                mapFullTime = Time.unscaledTime;
            mapContinuesAfterFog |= fogFullTime >= 0 && maps.Any(m => Expansion(m, initialScale) < .999f);
            iconFogOverlap |= fogVisible && icons.Any(i => Expansion(i, initialScale) < .999f);
            fogMapOverlap |= mapVisible && fog.Any(f => Expansion(f, initialScale) < .999f);
            fogLayersStaggered |= fogVisible && fog.Max(f => Expansion(f, initialScale)) - fog.Min(f => Expansion(f, initialScale)) > .1f;
            for (int i = 0; i < 3; i++)
                if (sweeps[i] != null && sweeps[i].color.a > .15f)
                {
                    sweepFirst[i] ??= sweeps[i].transform.position.x; sweepLast[i] = sweeps[i].transform.position.x;
                    sizeFirst[i] ??= sweeps[i].transform.localScale.x; sizeLast[i] = sweeps[i].transform.localScale.x;
                }
            bool pictureReady = Time.unscaledTime - stageStarted > (stage == "IconsAndWorld" ? .04f : .15f);
            if (stage == "Sweeps")
            {
                Vector3 center = sweeps[0].bounds.center;
                float x = camera.WorldToViewportPoint(center).x;
                pictureReady = Mathf.Abs(x - .5f) > .12f && Mathf.Abs(x - .5f) < .2f && sweeps[0].color.a > .4f;
            }
            if (pictureReady && pictured.Add(stage))
            {
                var hidden = stage == "Sweeps" ? maps.SelectMany(m => (SpriteRenderer[])Read(m, "sprites")).Where(r => !r.forceRenderingOff).ToArray() : Array.Empty<SpriteRenderer>();
                foreach (var renderer in hidden) renderer.forceRenderingOff = true;
                yield return new WaitForEndOfFrame();
                Color pixel = Capture(stage, stage == "Sweeps" ? camera.WorldToScreenPoint(sweeps[0].bounds.center) : (Vector3?)null);
                if (stage == "Sweeps")
                {
                    Report.AppendLine("Sweep center rendered color=" + pixel);
                    visibleSweep = pixel.r > .005f && Mathf.Abs(pixel.r - pixel.g) < .02f && Mathf.Abs(pixel.g - pixel.b) < .02f;
                    Report.AppendLine($"Sweep visible={sweeps[0].isVisible}, alpha={sweeps[0].color.a}, center={sweeps[0].bounds.center}, size={sweeps[0].bounds.size}, order={sweeps[0].sortingOrder}");
                }
                foreach (var renderer in hidden) renderer.forceRenderingOff = false;
            }
            // Compare rendered-frame values after lighting's LateUpdate, not a frame-old intensity.
            yield return new WaitForEndOfFrame();
            if (entry.IsPlaying)
                foreach (var binding in lightBindings)
                {
                    var facility = (Facility)Read(binding, "facility");
                    var light = (Light2D)Read(binding, "light");
                    var icon = icons.Single(v => (Transform)Read(v, "target")
                        == canvas.facilities.Single(f => f.facility == facility).worldRoot);
                    float iconOpacity = ((CanvasGroup)Read(icon, "group")).alpha / (float)Read(icon, "alpha");
                    bool unlocked = HideOutModel.StoryUnlocked(view.TrainingPlayer.State, facility);
                    float intensity = light.intensity / (float)Read(binding, "authoredIntensity");
                    maximumLightIconDifference = Mathf.Max(maximumLightIconDifference, Mathf.Abs(intensity - (unlocked ? iconOpacity : 0)));
                    if (intensity > .001f && !lightStartOrder.Contains(facility)) lightStartOrder.Add(facility);
                }
            yield return null;
        }
        if (fogFullTime < 0 && !entry.IsPlaying) fogFullTime = Time.unscaledTime;
        if (mapFullTime < 0 && !entry.IsPlaying) mapFullTime = Time.unscaledTime;
        Report.AppendLine("Observed stages: " + string.Join(",", states) + "; finished=" + !entry.IsPlaying
            + "; elapsed=" + (Time.unscaledTime - started).ToString("F3"));
        Require(!entry.IsPlaying && states.Where(s => s != "WaitingForFade").SequenceEqual(new[] { "Sweeps", "Icons", "IconsAndWorld", "FogAndMap", "Map" }), "radial fog and icons lead into simultaneous fog/map zoom, with the map finishing last");
        Report.AppendLine("Fog expansion when first icon appears=" + iconArrivalExpansion.ToString("F3"));
        Require(iconArrivalExpansion >= 0 && iconArrivalExpansion < .08f && environment.entrance.radialDurationMultiplier == 1.5f,
            "icons now appear before the final radial cloud advances, while radial duration is 1.5x");
        Report.AppendLine($"Mean icon expansion at world arrival={iconsAtFogArrival:F3}; fog/map start difference={mapFirstTime - fogFirstTime:F3}s; observed fog duration={fogFullTime - fogFirstTime:F3}s; map duration={mapFullTime - mapFirstTime:F3}s");
        Require(iconsAtFogArrival > .1f && iconsAtFogArrival < .4f && environment.entrance.revealTimingMultiplier == .5f,
            "world reveal starts earlier during icon enlargement, with reveal start times halved");
        Require(Mathf.Abs(mapFirstTime - fogFirstTime) < .05f, "fog and map start enlarging together on the same sampled frame");
        Require(fogFullTime - fogFirstTime >= 1.8f && fogFullTime - fogFirstTime < 3.5f && environment.entrance.fogZoomDuration == 2,
            "layered fog completes its two-second sequence including stagger");
        Require(mapFullTime - mapFirstTime >= 2.8f && mapFullTime - mapFirstTime < 4.5f && environment.entrance.mapZoomDuration == 3,
            "map completes its three-second sequence including stagger");
        Require(mapContinuesAfterFog && mapFullTime - fogFullTime > .85f, "map keeps enlarging for about one second after the fog settles");
        Require(iconFogOverlap && fogMapOverlap && fogLayersStaggered, "fog layers grow sequentially and overlap both icon and map enlargement");
        int[] directions = Enumerable.Range(0, 3).Select(i => Math.Sign((sweepLast[i] ?? 0) - (sweepFirst[i] ?? 0))).ToArray();
        Report.AppendLine("Measured sweep directions: " + string.Join(",", directions));
        Require(directions[0] != 0 && directions[0] == directions[2] && directions[0] == -directions[1], "radial lanes alternate left/right/left or right/left/right");
        Require(Enumerable.Range(0, 3).All(i => sizeFirst[i].HasValue && sizeLast[i] > sizeFirst[i] * 3), "all three clouds enlarge toward the viewer while leaving the center");
        yield return null;
        Require(gradualInitialLighting, "initially unlocked lights fade gradually with their icon entrance");
        Report.AppendLine("Maximum normalized light/icon difference=" + maximumLightIconDifference.ToString("F6"));
        Require(maximumLightIconDifference < .001f, "area lights match their own icon opacity on every sampled rendered frame");
        Require(lightStartOrder.SequenceEqual(icons.Select(i => (Facility)Read(i, "facility"))
            .Where(f => HideOutModel.StoryUnlocked(view.TrainingPlayer.State, f))), "unlocked lights start in the same left-to-right order as their icons");
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
        float lightResumeStarted = Time.unscaledTime;
        while (Time.unscaledTime - lightResumeStarted < 1) yield return null;
        var lightChecks = VerifyAreaLights(view, lighting, lightBindings, globalLights);
        while (lightChecks.MoveNext()) yield return lightChecks.Current;
        Require(visibleSweep, "gray sweep visibly renders over the black background");
    }

    private static IEnumerator VerifyAreaLights(HideOutScene view, BaseAreaLighting lighting, object[] bindings,
        Dictionary<Light2D, float> globals)
    {
        var environment = view.spriteEnvironment;
        foreach (var binding in bindings)
        {
            var light = (Light2D)Read(binding, "light");
            var facility = (Facility)Read(binding, "facility");
            float expected = HideOutModel.StoryUnlocked(view.TrainingPlayer.State, facility) ? (float)Read(binding, "authoredIntensity") : 0;
            Require(Mathf.Abs(light.intensity - expected) < .0001f, facility + " respects initial story lock and authored intensity");
        }
        var clinic = (Light2D)Read(bindings.Single(b => (Facility)Read(b, "facility") == Facility.Clinic), "light");
        view.TrainingPlayer.State.baseTutorialComplete = true;
        Invoke(view, "Refresh");
        Require(clinic.intensity == 0, "unlock starts from zero without a flash");
        bool gradual = false;
        float started = Time.unscaledTime;
        while (clinic.intensity < .999f && Time.unscaledTime - started < 4)
        {
            Invoke(view, "Refresh"); // Normal UI refresh must not restart the fade.
            yield return null;
            gradual |= clinic.intensity > .01f && clinic.intensity < .99f;
        }
        Require(gradual && Mathf.Abs(clinic.intensity - 1) < .001f, "story unlock fades to authored intensity despite repeated UI refresh");
        typeof(HideOutScene).GetField("testFacilitiesUnlocked", Fields).SetValue(view, true);
        Invoke(view, "Refresh");
        started = Time.unscaledTime;
        while (bindings.Any(b => Mathf.Abs(((Light2D)Read(b, "light")).intensity - (float)Read(b, "authoredIntensity")) > .0001f)
            && Time.unscaledTime - started < 4) yield return null;
        Require(bindings.All(b => Mathf.Abs(((Light2D)Read(b, "light")).intensity - (float)Read(b, "authoredIntensity")) < .0001f),
            "all facilities restore their individual authored intensities when unlocked");
        var storage = bindings.Single(b => (Facility)Read(b, "facility") == Facility.Storage);
        Require(Mathf.Abs(((Light2D)Read(storage, "light")).intensity - 1.14f) < .0001f, "Storage preserves its authored intensity 1.14");
        typeof(HideOutScene).GetField("testFacilitiesUnlocked", Fields).SetValue(view, false);
        view.TrainingPlayer.State.baseTutorialComplete = false;
        Invoke(view, "Refresh");
        Require(bindings.Where(b => !HideOutModel.StoryUnlocked(view.TrainingPlayer.State, (Facility)Read(b, "facility")))
            .All(b => ((Light2D)Read(b, "light")).intensity == 0), "relocked areas immediately return to exact zero");
        environment.gameObject.SetActive(false);
        Require(bindings.All(b => ((Light2D)Read(b, "light")).intensity == 0), "leaving the base turns off separately rooted area lights");
        environment.gameObject.SetActive(true);
        yield return null;
        started = Time.unscaledTime;
        while (Time.unscaledTime - started < 1) yield return null;
        Require(bindings.Where(b => HideOutModel.StoryUnlocked(view.TrainingPlayer.State, (Facility)Read(b, "facility")))
            .All(b => Mathf.Abs(((Light2D)Read(b, "light")).intensity - (float)Read(b, "authoredIntensity")) < .0001f),
            "returning to the base restores unlocked lights");
        Require(globals.All(pair => pair.Key.intensity == pair.Value), "global lighting is unchanged throughout");
    }
}
