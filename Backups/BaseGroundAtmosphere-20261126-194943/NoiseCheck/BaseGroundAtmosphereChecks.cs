using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BehindTheGear.HideOut;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Explicitly requested sandbox check; never runs during ordinary play or changes scene assets.
[InitializeOnLoad]
public static class BaseGroundAtmosphereChecks
{
    private const string Folder = "Temp/BaseGroundAtmosphere/";
    private static readonly StringBuilder Report = new();
    private static bool running;
    private static int errors;
    private struct Sample { public Vector3 world; public bool floor; }
    static BaseGroundAtmosphereChecks() => EditorApplication.update += Poll;
    private static void Poll()
    {
        if (!File.Exists(Folder + "check.request") || !EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        var view = Object.FindFirstObjectByType<HideOutScene>();
        if (view?.TrainingPlayer?.Initialized != true || SceneFadeTransition.IsRunning) return;
        File.Delete(Folder + "check.request");
        if (running || Application.unityVersion != "6000.3.23f1" || !view.TrainingPlayer.Sandbox)
            throw new InvalidOperationException("Run the atmosphere check only in Unity 6000.3.23f1 direct Base_Test sandbox.");
        running = true; errors = 0; Report.Clear();
        Application.logMessageReceived += Observe;
        view.TrainingPlayer.StartCoroutine(Run(view));
    }
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
        var atmosphere = view.spriteEnvironment.GetComponent<BaseGroundAtmosphere>();
        var camera = Object.FindFirstObjectByType<HideOutCameraController>();
        bool oldView = view.enabled, oldCamera = camera.enabled;
        Vector3 oldPosition = camera.transform.position;
        var settings = JsonUtility.ToJson(atmosphere.Configuration);
        float oldSeparation = PrototypeScreenEffects.OverridePixels;
        PrototypeScreenEffects.OverridePixels = 1.5f; // Freeze existing chromatic animation for image comparisons.
        var fog = view.spriteEnvironment.GetComponent<HideOutLockedFog>();
        bool oldFog = fog.enabled;
        var clouds = view.spriteEnvironment.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name.EndsWith(" Locked Fog", StringComparison.Ordinal) && t.gameObject.activeSelf).ToArray();
        view.enabled = camera.enabled = fog.enabled = false;
        foreach (var root in clouds) root.gameObject.SetActive(false);
        var steps = Verify(atmosphere, camera.GetComponent<Camera>());
        bool success = true;
        while (true)
        {
            object step;
            try { if (!steps.MoveNext()) break; step = steps.Current; }
            catch (Exception e) { Report.AppendLine("FAIL " + e); success = false; break; }
            yield return step;
        }
        JsonUtility.FromJsonOverwrite(settings, atmosphere.Configuration);
        PrototypeScreenEffects.OverridePixels = oldSeparation;
        view.spriteEnvironment.gameObject.SetActive(true);
        camera.transform.position = oldPosition;
        foreach (var root in clouds) if (root != null) root.gameObject.SetActive(true);
        view.enabled = oldView; camera.enabled = oldCamera; fog.enabled = oldFog;
        yield return null; yield return new WaitForEndOfFrame();
        Capture("final");
        Application.logMessageReceived -= Observe;
        Report.AppendLine("Runtime errors: " + errors);
        Report.AppendLine(success && errors == 0 ? "PASS atmosphere rendering checks" : "FAIL atmosphere rendering checks");
        File.WriteAllText(Folder + "check-result.txt", Report.ToString());
        running = false; EditorApplication.isPlaying = false;
    }
    private static Texture2D Capture(string name)
    {
        var frame = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Folder + name + ".png", frame.EncodeToPNG());
        return frame;
    }
    private static List<Sample> Samples(BaseGroundAtmosphere atmosphere)
    {
        var points = new List<Sample>();
        foreach (var entry in atmosphere.MaskSprites.Where(s => s.mode == 2 && s.renderer.gameObject.activeInHierarchy))
        {
            var texture = new Texture2D(2, 2);
            texture.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(entry.sprite.texture)));
            for (int y = 6; y < texture.height - 6; y += 10)
                for (int x = 6; x < texture.width - 6; x += 10)
                {
                    var color = texture.GetPixel(x, y);
                    if (color.a < .99f) continue;
                    bool floor = color.maxColorComponent < atmosphere.Configuration.roomFloorBrightness;
                    bool stable = true;
                    foreach (var d in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down })
                    {
                        var nearby = texture.GetPixel(x + d.x * 5, y + d.y * 5);
                        if (nearby.a < .99f || (nearby.maxColorComponent < atmosphere.Configuration.roomFloorBrightness) != floor) stable = false;
                    }
                    if (!stable) continue;
                    Vector2 local = (new Vector2(x + .5f, y + .5f) - entry.sprite.rect.position - entry.sprite.pivot) / entry.sprite.pixelsPerUnit;
                    points.Add(new Sample { world = entry.renderer.transform.TransformPoint(local), floor = floor });
                }
            Object.Destroy(texture);
        }
        return points;
    }
    private static float Delta(Texture2D a, Texture2D b, Camera camera, Sample point)
    {
        Vector3 screen = camera.WorldToScreenPoint(point.world);
        int x = Mathf.RoundToInt(screen.x), y = Mathf.RoundToInt(screen.y);
        if (x < 5 || y < 5 || x >= a.width - 5 || y >= a.height - 5) return float.NaN;
        var before = a.GetPixel(x, y); var after = b.GetPixel(x, y);
        return (before.r + before.g + before.b - after.r - after.g - after.b) / 3;
    }
    private static IEnumerator Verify(BaseGroundAtmosphere atmosphere, Camera camera)
    {
        foreach (string name in new[] { "BaseGroundMask", "BaseGroundAtmosphere", "BaseScreenNoise" })
        {
            var shader = Resources.Load<Shader>("Effects/" + name);
            Require(shader != null && shader.isSupported && !ShaderUtil.ShaderHasError(shader), name + " compiles on the current graphics device");
        }
        Require(BaseGroundAtmosphere.TryGet(camera, out var active) && active == atmosphere, "effect registered for the base camera");
        var points = Samples(atmosphere);
        Require(points.Count(p => p.floor) > 50 && points.Count(p => !p.floor) > 50, "sample authored wall and floor interiors");
        var config = atmosphere.Configuration;
        config.screenNoiseOpacity = config.groundFogSpeed = config.blackSpreadOpacity = config.groundFogOpacity = 0;
        yield return null; yield return new WaitForEndOfFrame();
        var before = Capture("baseline");
        config.blackSpreadOpacity = .65f; config.groundFogOpacity = .09f;
        yield return null; yield return new WaitForEndOfFrame();
        var after = Capture("floor-effects");
        float[] wall = points.Where(p => !p.floor).Select(p => Delta(before, after, camera, p)).Where(v => !float.IsNaN(v)).ToArray();
        float[] floor = points.Where(p => p.floor).Select(p => Delta(before, after, camera, p)).Where(v => !float.IsNaN(v)).ToArray();
        Report.AppendLine($"Samples wall={wall.Length}, max wall change={wall.Max(v => Mathf.Abs(v)):F5}, floor={floor.Length}, darkened={floor.Count(v => v > .002f)}");
        foreach (var p in points.Where(p => !p.floor).OrderByDescending(p => Mathf.Abs(Delta(before, after, camera, p))).Take(8))
            Report.AppendLine($"Wall sample world={p.world}, screen={camera.WorldToScreenPoint(p.world)}, change={Delta(before, after, camera, p):F5}");
        Require(wall.Max(v => Mathf.Abs(v)) < .005f, "floor effects do not darken authored walls");
        Require(floor.Count(v => v > .002f) > 30, "black atmosphere visibly renders on the floor");

        config.blackSpreadOpacity = 0; config.groundFogOpacity = .09f;
        yield return null; yield return new WaitForEndOfFrame();
        var fogBeforePan = Capture("fog-before-pan");
        var attenuation = points.Select(p => Delta(before, fogBeforePan, camera, p)).ToArray();
        camera.transform.position += new Vector3(.8f, .4f, 0);
        config.groundFogOpacity = 0;
        yield return null; yield return new WaitForEndOfFrame();
        var movedBaseline = Capture("pan-baseline");
        config.groundFogOpacity = .09f;
        yield return null; yield return new WaitForEndOfFrame();
        var movedFog = Capture("fog-after-pan");
        var differences = new List<float>();
        for (int i = 0; i < points.Count; i++)
        {
            float next = Delta(movedBaseline, movedFog, camera, points[i]);
            if (points[i].floor && attenuation[i] > .002f && next > .002f) differences.Add(Mathf.Abs(next - attenuation[i]));
        }
        Report.AppendLine($"Ground anchoring samples={differences.Count}, mean attenuation difference={differences.DefaultIfEmpty(1).Average():F5}");
        Require(differences.Count > 30 && differences.Average() < .003f, "ground fog stays at the same world points when the camera pans");
        config.screenNoiseOpacity = .03f;
        yield return null; yield return new WaitForEndOfFrame();
        var noise = atmosphere.GetComponentsInChildren<RawImage>().Single(i => i.name == "Noise 3 percent");
        Require(Mathf.Approximately(noise.material.GetFloat("_Opacity"), .03f) && !noise.raycastTarget, "screen noise uses 3 percent opacity and cannot intercept input");
        var noisy = Capture("noise");
        var corner = noisy.GetPixels(20, 20, 64, 64);
        Require(corner.All(c => Mathf.Abs(c.r - c.g) < .005f && Mathf.Abs(c.g - c.b) < .005f)
            && corner.Max(c => c.r) - corner.Min(c => c.r) > .01f, "noise renders achromatic variation across the screen");
        atmosphere.gameObject.SetActive(false);
        Require(!BaseGroundAtmosphere.TryGet(camera, out _) && !noise.gameObject.activeInHierarchy, "effects turn off when leaving the base environment");
        atmosphere.gameObject.SetActive(true);
        Require(BaseGroundAtmosphere.TryGet(camera, out _), "effects restore when returning to the base");
        foreach (var texture in new[] { before, after, fogBeforePan, movedBaseline, movedFog, noisy }) Object.Destroy(texture);
    }
}
