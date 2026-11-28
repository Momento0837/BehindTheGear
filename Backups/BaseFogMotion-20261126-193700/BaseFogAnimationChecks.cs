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
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class BaseFogAnimationChecks
{
    private const string Folder = "Temp/BaseFogAnimation/";
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly StringBuilder Report = new();
    private static bool running;
    private static int errors;
    private sealed class MotionSample
    {
        public object cloud;
        public float min = float.MaxValue, max = float.MinValue, firstTime = -1, firstX;
        public bool completedPeriod;
    }
    static BaseFogAnimationChecks() => EditorApplication.update += Poll;
    private static void Poll()
    {
        if (!File.Exists(Folder + "check.request") || !EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (Object.FindFirstObjectByType<HideOutScene>()?.TrainingPlayer?.Initialized != true || SceneFadeTransition.IsRunning) return;
        File.Delete(Folder + "check.request");
        RunChecks();
    }
    [MenuItem("Tools/HideOut/Check fog animations")]
    public static void RunChecks()
    {
        if (running || !EditorApplication.isPlaying || Application.unityVersion != "6000.3.23f1") return;
        var view = Object.FindFirstObjectByType<HideOutScene>();
        if (view?.TrainingPlayer?.Sandbox != true) throw new InvalidOperationException("Use direct Base_Test sandbox play.");
        running = true; errors = 0; Report.Clear(); Directory.CreateDirectory(Folder);
        Application.logMessageReceived += Observe;
        view.TrainingPlayer.StartCoroutine(Run(view));
    }
    private static object Read(object value, string name) => value.GetType().GetField(name, Fields).GetValue(value);
    private static SpriteRenderer Renderer(object cloud) => (SpriteRenderer)Read(cloud, "renderer");
    private static IList Clouds(object region) => (IList)Read(region, "clouds");
    private static GameObject Root(object region) => (GameObject)Read(region, "root");
    private static void Observe(string message, string trace, LogType type)
    {
        if (type is LogType.Error or LogType.Exception) { errors++; Report.AppendLine("ERROR " + message); }
    }
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
    private static void Pass(string message) => Report.AppendLine("PASS " + message);
    private static IEnumerator Run(HideOutScene view)
    {
        bool wasEnabled = view.enabled;
        var controller = Object.FindFirstObjectByType<HideOutCameraController>();
        bool cameraEnabled = controller.enabled;
        Vector3 cameraPosition = controller.transform.position;
        float cameraSize = controller.GetComponent<Camera>().orthographicSize;
        float previousTimeScale = Time.timeScale;
        view.enabled = false; controller.enabled = false;
        var steps = Verify(view, controller.GetComponent<Camera>());
        bool success = true;
        while (true)
        {
            object step;
            try { if (!steps.MoveNext()) break; step = steps.Current; }
            catch (Exception e) { Report.AppendLine("FAIL " + e); success = false; break; }
            yield return step;
        }
        Time.timeScale = previousTimeScale;
        controller.transform.position = cameraPosition; controller.enabled = cameraEnabled;
        controller.GetComponent<Camera>().orthographicSize = cameraSize;
        view.spriteEnvironment.gameObject.SetActive(true); view.enabled = wasEnabled;
        Application.logMessageReceived -= Observe;
        Report.AppendLine("Runtime errors: " + errors);
        Report.AppendLine(success && errors == 0 ? "PASS fog animation checks" : "FAIL fog animation checks");
        File.WriteAllText(Folder + "check-result.txt", Report.ToString());
        running = false; EditorApplication.isPlaying = false;
    }
    private static IEnumerator Verify(HideOutScene view, Camera camera)
    {
        var fog = view.spriteEnvironment.GetComponent<HideOutLockedFog>();
        var regions = (IDictionary)Read(fog, "regions");
        var region = regions[Facility.Clinic];
        Func<Facility, bool> initial = f => f != Facility.Bed && f != Facility.Dining;
        fog.ApplyLocks(_ => false);
        fog.ApplyLocks(initial);
        Require(region != null, "Clinic starts locked");
        var allRegions = regions.Values.Cast<object>().ToArray();
        var initialCounts = allRegions.ToDictionary(r => r, r => Clouds(r).Count);
        foreach (var state in allRegions)
        {
            var interior = Clouds(state).Cast<object>().Where(c => Renderer(c).name.StartsWith("BigFog ")).ToArray();
            float diameter = interior.SelectMany(a => interior.Select(b => Vector2.Distance(
                (Vector3)Read(a, "restPosition"), (Vector3)Read(b, "restPosition")))).Max();
            Require(diameter > 1, Root(state).name + " spreads its interior clouds instead of stacking them at the icon");
            Require(interior.Length is >= 5 and <= 7 && interior.All(c => !(bool)Read(c, "small")),
                Root(state).name + " has two additional large clouds, without substituting small fog");
            var large = interior;
            for (int a = 0; a < large.Length; a++)
                for (int b = a + 1; b < large.Length; b++)
                {
                    Vector2 size = (Renderer(large[a]).bounds.size + Renderer(large[b]).bounds.size) * .5f;
                    Vector2 delta = (Vector3)Read(large[a], "restPosition") - (Vector3)Read(large[b], "restPosition");
                    float separation = new Vector2(delta.x / size.x, delta.y / size.y).magnitude;
                    Require(separation >= .1f, Root(state).name + " allows overlap without stacking large cloud cores");
                }
        }
        Pass("all locked regions distribute interior clouds and keep large cloud cores separated");
        var originalClouds = Clouds(region).Cast<object>().ToArray();
        var ordered = originalClouds.OrderBy(c => (float)Read(c, "depth")).ToArray();
        var far = Renderer(ordered[0]); var near = Renderer(ordered[^1]);
        yield return null;
        Vector3 farBefore = far.transform.position, nearBefore = near.transform.position;
        camera.transform.position += Vector3.right * 4;
        yield return null;
        float farMotion = farBefore.x - far.transform.position.x, nearMotion = nearBefore.x - near.transform.position.x;
        Require(farMotion > 0 && nearMotion > farMotion * 1.5f, "near clouds move more than far clouds, against camera travel");
        Pass("visible depth-dependent parallax: far=" + farMotion.ToString("F3") + ", near=" + nearMotion.ToString("F3") + " world units");
        camera.transform.position -= Vector3.right * 4;
        yield return null;

        var motion = originalClouds.Select(c => new MotionSample { cloud = c }).ToArray();
        var startTimes = new List<float>();
        object previousIncoming = null;
        int fadeInSamples = 0, fadeOutSamples = 0, pulseMinimumSamples = 0, pulseChanges = 0;
        object previousPulse = Read(region, "pulse");
        float begin = Time.unscaledTime;
        while (Time.unscaledTime - begin < 11.3f)
        {
            float now = Time.unscaledTime;
            foreach (var state in allRegions)
            {
                object incoming = Read(state, "incoming"), outgoing = Read(state, "outgoing"), pulse = Read(state, "pulse");
                Require(Clouds(state).Count == initialCounts[state] + (incoming != null ? 1 : 0), "replacement keeps population bounded to one extra cloud");
                int pulsing = 0;
                foreach (var cloud in Clouds(state))
                {
                    if (cloud == incoming || cloud == outgoing) continue;
                    float alpha = Renderer(cloud).color.a;
                    Require(alpha >= .949f && alpha <= 1.001f, "ambient opacity remains within 95-100 percent");
                    if (alpha < .9999f) pulsing++;
                }
                Require(pulsing <= 1, "only one cloud breathes in each region");
                if (pulse != null && Renderer(pulse).color.a < .952f) pulseMinimumSamples++;
            }
            object currentPulse = Read(region, "pulse");
            if (currentPulse != previousPulse) { pulseChanges++; previousPulse = currentPulse; }
            object currentIncoming = Read(region, "incoming");
            if (currentIncoming != null && currentIncoming != previousIncoming)
            {
                startTimes.Add((float)Read(region, "replacementStart"));
                previousIncoming = currentIncoming;
            }
            if (currentIncoming != null)
            {
                float age = now - (float)Read(region, "replacementStart");
                float incomingAlpha = Renderer(currentIncoming).color.a;
                float outgoingAlpha = Renderer(Read(region, "outgoing")).color.a;
                if (age > .45f && age < .6f)
                {
                    Require(incomingAlpha > .25f && incomingAlpha < .75f && outgoingAlpha > .99f, "new cloud fades in before old cloud fades out");
                    fadeInSamples++;
                }
                if (age > 1.45f && age < 1.6f)
                {
                    Require(incomingAlpha > .99f && outgoingAlpha > .25f && outgoingAlpha < .75f, "old cloud fades out after the new cloud is visible");
                    fadeOutSamples++;
                }
            }
            foreach (var sample in motion)
            {
                var renderer = Renderer(sample.cloud);
                if (renderer == null) continue;
                Vector3 rest = (Vector3)Read(sample.cloud, "restPosition");
                float x = camera.WorldToScreenPoint(renderer.transform.position).x - camera.WorldToScreenPoint(rest).x;
                Require(Mathf.Abs(x) <= 2.1f, "sway stays within two screen pixels");
                sample.min = Mathf.Min(sample.min, x); sample.max = Mathf.Max(sample.max, x);
                if (now - (float)Read(sample.cloud, "swayStart") < .6f) continue;
                if (sample.firstTime < 0) { sample.firstTime = now; sample.firstX = x; }
                else if (!sample.completedPeriod && now - sample.firstTime >= 5)
                {
                    Require(Mathf.Abs(x - sample.firstX) < .25f, "horizontal motion returns after a five-second round trip");
                    sample.completedPeriod = true;
                }
            }
            // The effects must continue while gameplay time is paused by UI.
            Time.timeScale = now - begin is > 5 and < 6 ? 0 : 1;
            yield return null;
        }
        Require(fadeInSamples > 0 && fadeOutSamples > 0, "both one-second replacement fade phases were sampled");
        Require(startTimes.Count >= 4 && startTimes.Zip(startTimes.Skip(1), (a, b) => b - a).All(d => d >= 2.29f && d < 2.5f), "replacement begins every 2.3 seconds");
        Require(pulseMinimumSamples > 0 && pulseChanges >= 10, "one-second pulses reach 95 percent and immediately choose another cloud");
        Require(motion.Any(s => s.completedPeriod && s.min < -1.8f && s.max > 1.8f), "slow sway reaches both two-pixel extrema");
        Pass("2.3-second replacement cadence, sequential 1-second fades, and bounded cloud count");
        Pass("one 100-95-100 percent pulse per region, one-second cycles with no idle gap");
        Pass("random sway starts, +/-2 screen pixels, five-second round trip; effects run at timeScale zero");
        ScreenCapture.CaptureScreenshot(Path.GetFullPath(Folder + "ambient.png"));
        yield return null;
        // Match the reported problem area at close range as well as the overview.
        Vector3 overview = camera.transform.position;
        float overviewSize = camera.orthographicSize;
        var workshop = regions[Facility.Workshop];
        Vector3 workshopCenter = (Vector3)Read(workshop, "center");
        camera.transform.position = new Vector3(workshopCenter.x, workshopCenter.y, overview.z);
        camera.orthographicSize = 2.8f;
        yield return null;
        ScreenCapture.CaptureScreenshot(Path.GetFullPath(Folder + "workshop-spacing.png"));
        yield return null;
        camera.transform.position = overview; camera.orthographicSize = overviewSize;
        yield return null;
        // Unlock during an in-progress replacement, including all visible incoming/outgoing clouds.
        float deadline = Time.unscaledTime + 4;
        while (Read(region, "incoming") == null && Time.unscaledTime < deadline) yield return null;
        Require(Read(region, "incoming") != null, "replacement available for unlock interruption");
        var survivors = Clouds(region).Cast<object>().Where(c => c != Read(region, "outgoing")).ToArray();
        var before = survivors.ToDictionary(c => c, c => camera.WorldToScreenPoint(Renderer(c).transform.position));
        var alphas = survivors.ToDictionary(c => c, c => Renderer(c).color.a);
        fog.ApplyLocks(f => initial(f) && f != Facility.Clinic);
        Require(Root(region).activeSelf, "unlock starts with fog still active");
        yield return new WaitForSecondsRealtime(.25f);
        var opaque = survivors.First(c => alphas[c] > .9f);
        float halfAlpha = Renderer(opaque).color.a / alphas[opaque];
        Require(Root(region).activeSelf && halfAlpha > .3f && halfAlpha < .7f, "unlock fades continuously at the halfway point");
        ScreenCapture.CaptureScreenshot(Path.GetFullPath(Folder + "unlock-half.png"));
        yield return new WaitForSecondsRealtime(.32f);
        Require(!Root(region).activeSelf && Clouds(region).Count == initialCounts[region], "unlock completes after half a second and cleans up replacement");
        Vector3 center = camera.WorldToScreenPoint((Vector3)Read(region, "center"));
        foreach (var cloud in survivors)
        {
            var renderer = Renderer(cloud);
            Vector2 movement = camera.WorldToScreenPoint(renderer.transform.position) - before[cloud];
            Vector2 radial = before[cloud] - center;
            Require(Mathf.Abs(movement.magnitude - 5) < .15f && Vector2.Dot(movement.normalized, radial.normalized) > .99f,
                "unlock moves each cloud five screen pixels away from the region center");
            Require(renderer.color.a <= .001f, "unlock ends at zero opacity");
        }
        Pass("unlock during replacement: five-pixel outward motion, half-second smooth fade, no leftover clouds");
        // Re-lock interrupts an exit, without leaving duplicates or a faded-out region.
        fog.ApplyLocks(initial);
        Require(Root(region).activeSelf && Clouds(region).Count == initialCounts[region], "re-lock restores the original population");
        fog.ApplyLocks(f => initial(f) && f != Facility.Clinic);
        yield return new WaitForSecondsRealtime(.15f);
        fog.ApplyLocks(initial);
        Require(Clouds(region).Cast<object>().All(c => Renderer(c).color.a > .999f), "re-lock cancels an unfinished exit cleanly");
        Pass("rapid unlock/re-lock restores full opacity and original cloud count");
        // Treatment rooms suspend the map while changing progression.
        view.spriteEnvironment.gameObject.SetActive(false);
        fog.ApplyLocks(f => initial(f) && f != Facility.Clinic);
        yield return new WaitForSecondsRealtime(.65f);
        view.spriteEnvironment.gameObject.SetActive(true);
        yield return null;
        Require(Root(region).activeSelf, "offscreen unlock waits until the map is visible");
        yield return new WaitForSecondsRealtime(.6f);
        Require(!Root(region).activeSelf, "deferred unlock finishes after returning to the map");
        Pass("unlock while the map is suspended plays on return");
        fog.ApplyLocks(initial);
    }
}
