using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BehindTheGear.HideOut;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class BaseEntranceChecks
{
    private const string Folder = "Temp/BaseEntrance/";
    private const string OwnPlayKey = "BaseEntranceChecks.OwnPlay";
    private const string PreviousSceneKey = "BaseEntranceChecks.PreviousStartScene";
    private const string PlayDeadlineKey = "BaseEntranceChecks.PlayDeadline";
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly StringBuilder Report = new();
    private static bool running;
    private static int errors;
    static BaseEntranceChecks()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(OwnPlayKey, false)) return;
            if (File.Exists(Folder + "check.request"))
            {
                File.Delete(Folder + "check.request");
                File.WriteAllText(Folder + "check-result.txt", "FAIL entry click: requested Play ended before the sandbox check started.");
            }
            RestorePlayStartScene();
        };
    }
    private static void RestorePlayStartScene()
    {
        string previous = SessionState.GetString(PreviousSceneKey, "");
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previous) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
        SessionState.SetBool(OwnPlayKey, false);
        SessionState.SetString(PreviousSceneKey, "");
        SessionState.SetFloat(PlayDeadlineKey, 0);
    }
    private static void FailRequest(string message)
    {
        if (File.Exists(Folder + "check.request")) File.Delete(Folder + "check.request");
        File.WriteAllText(Folder + "check-result.txt", "FAIL entry click: " + message);
        if (!SessionState.GetBool(OwnPlayKey, false)) return;
        SessionState.SetFloat(PlayDeadlineKey, 0);
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
        else RestorePlayStartScene();
    }
    private static void Poll()
    {
        bool ownsPlay = SessionState.GetBool(OwnPlayKey, false);
        float deadline = SessionState.GetFloat(PlayDeadlineKey, 0);
        if (ownsPlay && deadline > 0 && EditorApplication.timeSinceStartup >= deadline)
        {
            FailRequest("Base_Test sandbox did not become ready within 30 seconds; only the check's own Play was stopped.");
            return;
        }
        if (!File.Exists(Folder + "check.request") || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string mode = File.ReadAllText(Folder + "check.request").Trim();
        if (!EditorApplication.isPlaying)
        {
            if (ownsPlay || EditorApplication.isPlayingOrWillChangePlaymode || !mode.Equals("click", StringComparison.OrdinalIgnoreCase)) return;
            var startScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/!UI/Base/Base_Test.unity");
            if (Application.unityVersion != "6000.3.23f1" || startScene == null)
            {
                FailRequest("automatic Play requires Unity 6000.3.23f1 and the existing Base_Test scene asset. No editing scene was opened or saved.");
                return;
            }
            SessionState.SetString(PreviousSceneKey, AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetBool(OwnPlayKey, true);
            SessionState.SetFloat(PlayDeadlineKey, (float)EditorApplication.timeSinceStartup + 30f);
            EditorSceneManager.playModeStartScene = startScene;
            EditorApplication.isPlaying = true;
            return;
        }
        if (!EditorApplication.isPlayingOrWillChangePlaymode) return;
        var view = Object.FindFirstObjectByType<HideOutScene>();
        if (view?.TrainingPlayer?.Initialized != true)
        {
            if (!ownsPlay) FailRequest("existing user Play is not a ready base sandbox; it was left running without loading any scene.");
            return;
        }
        if (running) { File.Delete(Folder + "check.request"); return; }
        if (Application.unityVersion != "6000.3.23f1" || !view.TrainingPlayer.Sandbox || view.gameObject.scene.name != "Base_Test")
        {
            FailRequest("run entrance checks in Unity 6000.3.23f1 direct Base_Test sandbox only; user-owned Play is never stopped.");
            return;
        }
        File.Delete(Folder + "check.request");
        SessionState.SetFloat(PlayDeadlineKey, 0);
        running = true; errors = 0; Report.Clear();
        Report.AppendLine("Mode: " + (mode.Equals("click", StringComparison.OrdinalIgnoreCase) ? "entry click" : "full entrance"));
        Application.logMessageReceived += Observe;
        view.TrainingPlayer.StartCoroutine(Run(view, mode));
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
    private static IEnumerator Run(HideOutScene view, string mode)
    {
        var steps = mode.Equals("click", StringComparison.OrdinalIgnoreCase) ? VerifyEntryClick(view) : Verify(view);
        bool success = true;
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
        if (SessionState.GetBool(OwnPlayKey, false)) EditorApplication.isPlaying = false;
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
    private static bool RaycastButton(Button button, out PointerEventData pointer)
    {
        pointer = null;
        if (button == null || EventSystem.current == null || !button.gameObject.activeInHierarchy) return false;
        var rect = (RectTransform)button.transform;
        var rootCanvas = button.GetComponentInParent<Canvas>()?.rootCanvas;
        Camera eventCamera = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? rootCanvas.worldCamera : null;
        Vector2 position = RectTransformUtility.WorldToScreenPoint(eventCamera, rect.TransformPoint(rect.rect.center));
        if (position.x < 0 || position.y < 0 || position.x >= Screen.width || position.y >= Screen.height) return false;
        pointer = new PointerEventData(EventSystem.current) { position = position, button = PointerEventData.InputButton.Left };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        if (hits.Count == 0 || hits[0].gameObject.GetComponentInParent<Button>() != button) return false;
        pointer.pointerCurrentRaycast = pointer.pointerPressRaycast = hits[0];
        pointer.pressPosition = position;
        return true;
    }

    // Separate from visual timing checks: actual camera focus would invalidate their fixed-view measurements.
    // Write "click" to the existing check.request in a fresh direct Base_Test sandbox run.
    private static IEnumerator VerifyEntryClick(HideOutScene view)
    {
        var entry = view.GetComponent<BaseEntrancePresentation>();
        var canvas = view.GetComponentInChildren<HideOutCanvasView>(true);
        var controller = Object.FindFirstObjectByType<HideOutCameraController>();
        Require(entry != null && entry.IsPlaying && canvas != null && controller != null, "click check starts during the base entrance");
        var dining = canvas.facilities.Single(f => f.facility == Facility.Dining);
        var clinic = canvas.facilities.Single(f => f.facility == Facility.Clinic).button;
        var hud = ((IList)Read(entry, "hudElements")).Cast<object>().ToArray();
        var hudButtons = hud.SelectMany(h => ((RectTransform)Read(h, "rect")).GetComponentsInChildren<Button>()).Distinct().ToArray();
        Require(!view.TrainingPlayer.State.baseTutorialComplete && !clinic.interactable, "clinic remains story-locked before the entry click");
        PointerEventData pointer = null;
        bool ready = false;
        float started = Time.unscaledTime;
        while (entry.IsPlaying && Time.unscaledTime - started < 15f)
        {
            var group = dining.worldRoot.GetComponent<CanvasGroup>();
            if (!SceneFadeTransition.IsRunning && !BaseArrivalTutorial.IsShowing && !SelenaBaseDialogue.IsShowing
                && dining.button.IsInteractable() && group != null && group.alpha > .05f
                && hud.Any(h => Vector2.Distance(((RectTransform)Read(h, "rect")).anchoredPosition, (Vector2)Read(h, "position")) > 1f)
                && hudButtons.Any(button => button.IsInteractable() && RaycastButton(button, out _))
                && RaycastButton(dining.button, out pointer))
            { ready = true; break; }
            yield return null;
        }
        Require(ready && entry.IsPlaying, "HUD buttons and Dining receive top UI raycasts while HUD elements are still sliding into place");
        Require(controller.PresentationLocked, "manual camera input remains locked before the explicit facility click");
        ExecuteEvents.Execute(dining.button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(dining.button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(dining.button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        Require((bool)Read(view, "hasSelection") && (Facility)Read(view, "selected") == Facility.Dining,
            "pointer down/up/click on the moving icon selects its real facility");
        Require(!controller.PresentationLocked && controller.IsMoving && entry.IsPlaying,
            "facility click releases the camera for focus while the entrance continues");
        ExecuteEvents.Execute(clinic.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        Require(!clinic.interactable && (Facility)Read(view, "selected") == Facility.Dining,
            "clicking the locked Clinic cannot replace the active facility selection");
        yield return null;
        Require(entry.IsPlaying && !controller.PresentationLocked, "entry animation does not stop or re-lock the camera on the next frame");
        float focusStarted = Time.unscaledTime;
        while (controller.IsMoving && Time.unscaledTime - focusStarted < 5f) yield return null;
        Require(!controller.IsMoving, "focus started by the entry click finishes normally");
        Require(!view.TrainingPlayer.State.baseTutorialComplete && !clinic.interactable, "entry clicks preserve tutorial progression and Clinic lock");
        Require(!SceneFadeTransition.IsRunning, "arrival-delay measurement starts with the black fade fully cleared");
        double delayStarted = Time.realtimeSinceStartupAsDouble;
        var arrivalDelay = BaseArrivalTutorial.WaitForArrivalDialogue();
        while (arrivalDelay.MoveNext()) yield return arrivalDelay.Current;
        double delayElapsed = Time.realtimeSinceStartupAsDouble - delayStarted;
        double frameTolerance = Math.Max(.002, Time.unscaledDeltaTime) + .002;
        Require(!SceneFadeTransition.IsRunning && delayElapsed >= .498 && delayElapsed <= .5 + frameTolerance,
            $"shared arrival dialogue wait lasts 0.5 real seconds after fade completion ({delayElapsed:F4}s, one-frame tolerance {frameTolerance:F4}s)");
    }

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
        var centerFog = ((IList)Read(entry, "centerFog")).Cast<SpriteRenderer>().ToArray();
        Require(centerFog.Length == 2 && centerFog.All(r => Mathf.Approximately(r.color.a, environment.entrance.sweepOpacity)),
            "two additional BigFog sprites are already visible when the entrance starts");
        Require(environment.entrance.sweepSequenceDuration == 3 && environment.entrance.centerFogStartDelay == 1f
            && environment.entrance.centerFogDuration == 2, "foreground lasts 3 seconds; center pair starts at 1 second and lasts 2 seconds");
        // Inspect actual animation output at fixed times before ordinary playback resumes.
        var animateCenter = typeof(BaseEntrancePresentation).GetMethod("AnimateCenterFog", Fields);
        Vector3[] pairStart = centerFog.Select(r => r.bounds.center).ToArray();
        float[] pairSize = centerFog.Select(r => r.bounds.size.x).ToArray();
        animateCenter.Invoke(entry, new object[] { .99f });
        Require(centerFog.Select((r, i) => r.bounds.center == pairStart[i] && Mathf.Approximately(r.bounds.size.x, pairSize[i])
            && Mathf.Approximately(r.color.a, environment.entrance.sweepOpacity)).All(v => v),
            "center pair remains visible and stationary until 1 second");
        Require(centerFog.All(r => r.color.a > .6f && Mathf.Abs(camera.WorldToViewportPoint(r.bounds.center).x - .5f) < .05f),
            "BigFog pair covers the center before separating");
        Require(centerFog.All(r => r.bounds.size.x <= 2 * camera.orthographicSize * camera.aspect * .381f
            && r.bounds.size.y <= 2 * camera.orthographicSize * .601f), "initial BigFog size fits within 38 percent screen width and 60 percent screen height");
        Require(centerFog.Max(r => r.sortingOrder) < sweeps.Min(r => r.sortingOrder), "approaching fog passes in front of the waiting center pair");
        animateCenter.Invoke(entry, new object[] { 2.2f });
        Require(centerFog[0].bounds.center.x < pairStart[0].x && centerFog[1].bounds.center.x > pairStart[1].x
            && centerFog.Select((r, i) => r.bounds.size.x > pairSize[i] && Vector3.Distance(r.bounds.center, camera.transform.position)
                > 0 && Vector3.Dot(r.bounds.center - pairStart[i], camera.transform.forward) < 0).All(v => v),
            "BigFog grows, moves toward the camera and separates left/right");
        animateCenter.Invoke(entry, new object[] { 3f });
        Require(centerFog.All(r => r.color.a == 0), "center pair fades completely at 3 seconds");
        Require(centerFog.All(r => r.bounds.size.x <= 2 * camera.orthographicSize * camera.aspect * .457f
            && r.bounds.size.y <= 2 * camera.orthographicSize * .721f), "maximum BigFog enlargement also remains smaller than the screen");
        animateCenter.Invoke(entry, new object[] { 0f });
        Require(maps.Length >= 10 && icons.Length == 8 && fog.Length > 0 && sweeps.Length == 3, "captures objects, eight icons, locked fog and exactly three temporary sweeps");
        foreach (var group in new[] { maps, icons, fog })
        {
            var distances = group.Select(v => (float)Read(v, "distance")).ToArray();
            Require(distances.SequenceEqual(distances.OrderBy(d => d)), group == icons ? "icons sorted by screen X, left to right" : "world elements sorted nearest screen center first");
        }
        Require((float)Read(icons[^1], "distance") - (float)Read(icons[0], "distance") > 50, "icon ordering uses positioned icons rather than prefab origin");
        Require(controller.PresentationLocked && !controller.TryFocus(environment.areas[0].focusAnchor, 2, null), "manual camera input stays locked until a facility interaction releases it");
        Invoke(view, "SelectFacility", Facility.Clinic);
        Require(!(bool)Read(view, "hasSelection") && !canvas.facilities.Single(f => f.facility == Facility.Clinic).button.interactable,
            "story-locked Clinic remains unavailable during entry");
        Require(view.GetComponentsInChildren<GraphicRaycaster>(true).Where(r => r.gameObject.activeInHierarchy).All(r => r.enabled),
            "active base UI raycasters remain enabled during entry");
        Require(icons.All(icon => ((Transform)Read(icon, "target")).GetComponent<GraphicRaycaster>()?.enabled == true),
            "each temporary icon canvas has an enabled raycaster during entry");
        var shader = Resources.Load<Shader>("Effects/BaseEntranceFog");
        Require(shader != null && shader.isSupported && !ShaderUtil.ShaderHasError(shader), "temporary fog shader compiles");
        Vector4 tint = sweeps[0].sharedMaterial.GetVector("_Tint");
        Require(Mathf.Abs(tint.x - 31f / 255 * .3f) < .0001f && tint.x == tint.y && tint.y == tint.z, "temporary fog tint is 70 percent darker than #1f1f1f");
        var fogCanvas = ((RectTransform)Read(entry, "fogCanvasRect")).GetComponent<Canvas>();
        var hud = ((IList)Read(entry, "hudElements")).Cast<object>().ToArray();
        Require(fogCanvas.sortingOrder < canvas.GetComponent<Canvas>().sortingOrder
            && icons.All(icon => ((Transform)Read(icon, "target")).GetComponent<Canvas>().sortingOrder < fogCanvas.sortingOrder),
            "temporary fog renders above facility icons and below all other HUD elements");
        Require(fogCanvas.GetComponent<GraphicRaycaster>() == null && fogCanvas.GetComponentsInChildren<Graphic>().All(g => !g.raycastTarget)
            && sweeps.Concat(centerFog).All(r => r.forceRenderingOff), "fog has no input targets and is not rendered twice");
        Require(hud.Length >= 14 && hud.All(h => ((CanvasGroup)Read(h, "group")).alpha == 0), "upper and lower HUD begin hidden outside the screen");
        Require(hud.Any(h => ((Vector2)Read(h, "offset")).y > 0) && hud.Any(h => ((Vector2)Read(h, "offset")).y < 0),
            "top HUD enters downward and bottom HUD enters upward");
        var updateUI = typeof(BaseEntrancePresentation).GetMethod("UpdateEntranceUI", Fields);
        updateUI.Invoke(entry, new object[] { .4f });
        Require(hud.All(h => ((CanvasGroup)Read(h, "group")).alpha > 0 && ((CanvasGroup)Read(h, "group")).alpha < 1
            && ((RectTransform)Read(h, "rect")).anchoredPosition != (Vector2)Read(h, "position")), "HUD slides and fades gradually midway through its entrance");
        updateUI.Invoke(entry, new object[] { -.1f });
        var states = new List<string>();
        var sweepFirst = new float?[3]; var sweepLast = new float?[3];
        var sizeFirst = new float?[3]; var sizeLast = new float?[3];
        var pictured = new HashSet<string>();
        bool visibleSweep = false, iconFogOverlap = false, fogMapOverlap = false, fogLayersStaggered = false, mapContinuesAfterFog = false;
        bool entryIconRaycast = false;
        var diningDuringEntry = canvas.facilities.Single(f => f.facility == Facility.Dining);
        var lightStartOrder = new List<Facility>();
        float maximumLightIconDifference = 0;
        float iconArrivalExpansion = -1;
        float fogTailStart = -1, fogTailEnd = -1;
        Vector3[] fogTailPositions = null;
        bool fogTailMovesPosition = false;
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
            var diningGroup = diningDuringEntry.worldRoot.GetComponent<CanvasGroup>();
            if (!entryIconRaycast && !SceneFadeTransition.IsRunning && diningGroup != null
                && diningGroup.alpha > .05f && diningGroup.alpha < .99f && diningDuringEntry.button.IsInteractable()
                && RaycastButton(diningDuringEntry.button, out _))
            {
                entryIconRaycast = true;
                Require(true, "partially revealed facility icon receives the top UI raycast while entry is still playing");
            }
            if (stage is "Icons" or "IconsAndWorld")
            {
                if (iconArrivalExpansion < 0 && ((CanvasGroup)Read(icons[0], "group")).alpha > 0)
                    iconArrivalExpansion = (sweeps[2].bounds.size.x / (2 * camera.orthographicSize * camera.aspect) - .18f) / 1.17f;
                float near = ((Transform)Read(icons[0], "target")).localScale.x / ((Vector3)Read(icons[0], "scale")).x;
                float far = ((Transform)Read(icons[^1], "target")).localScale.x / ((Vector3)Read(icons[^1], "scale")).x;
                if (near + .001f < far) throw new InvalidOperationException("Right icon overtook the left icon.");
            }
            bool fogVisible = fog.Any(f => Expansion(f, initialScale) > .0001f);
            bool mapVisible = maps.Any(m => Expansion(m, initialScale) > .0001f);
            if (stage == "FogAndMap" && fog.Any(f => Expansion(f, initialScale) > .9001f))
                throw new InvalidOperationException("Fog used its reserved final travel before the map's settling phase.");
            if (stage == "Map")
            {
                float mean = fog.Average(f => Expansion(f, initialScale));
                if (fogTailStart < 0)
                {
                    fogTailStart = mean;
                    fogTailPositions = fog.Select(f => ((Transform)Read(f, "target")).localPosition).ToArray();
                    Require(fog.All(f => Expansion(f, initialScale) >= .8999f && Expansion(f, initialScale) < .905f),
                        "every fog layer reaches 90 percent at the start of the final map phase");
                }
                if (fogTailEnd >= 0 && mean < fogTailEnd - .0001f) throw new InvalidOperationException("Fog tail moved backwards.");
                fogTailEnd = mean;
                fogTailMovesPosition |= fog.Select((f, i) => Vector3.Distance(((Transform)Read(f, "target")).localPosition, fogTailPositions[i])).Any(d => d > .0001f);
            }
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
            bool captureSweep = !pictured.Contains("Foreground");
            if (captureSweep)
            {
                Vector3 center = sweeps[0].bounds.center;
                float x = camera.WorldToViewportPoint(center).x;
                captureSweep = Mathf.Abs(x - .5f) > .12f && Mathf.Abs(x - .5f) < .2f && sweeps[0].color.a > .4f;
            }
            string pictureName = captureSweep ? "Foreground" : stage;
            if ((pictureReady || captureSweep) && pictured.Add(pictureName))
            {
                var hidden = captureSweep ? maps.SelectMany(m => (SpriteRenderer[])Read(m, "sprites")).Where(r => !r.forceRenderingOff).ToArray() : Array.Empty<SpriteRenderer>();
                foreach (var renderer in hidden) renderer.forceRenderingOff = true;
                yield return new WaitForEndOfFrame();
                Color pixel = Capture(pictureName, captureSweep ? camera.WorldToScreenPoint(sweeps[0].bounds.center) : (Vector3?)null);
                if (captureSweep)
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
        Require(!entry.IsPlaying && states.Where(s => s != "WaitingForFade").SequenceEqual(new[] { "Sweeps", "IconsAndWorld", "FogAndMap", "Map" }), "radial fog leads into the overlapping icon/world reveal and a shared final settling phase");
        Require(entryIconRaycast, "UI raycast reaches an unlocked icon before its entrance fade completes");
        Report.AppendLine("Fog expansion when first icon appears=" + iconArrivalExpansion.ToString("F3"));
        Require(iconArrivalExpansion >= 0 && iconArrivalExpansion < .08f && environment.entrance.sweepSequenceDuration == 3f,
            "delayed icons still appear early in the three-second foreground fly-through");
        Report.AppendLine($"Mean icon expansion at world arrival={iconsAtFogArrival:F3}; fog/map start difference={mapFirstTime - fogFirstTime:F3}s; observed fog duration={fogFullTime - fogFirstTime:F3}s; map duration={mapFullTime - mapFirstTime:F3}s");
        Require(iconsAtFogArrival >= 0 && iconsAtFogArrival < .001f && environment.entrance.revealTimingMultiplier == .5f,
            "world reveal keeps its schedule, just before the delayed icon/HUD entrance");
        Require(Mathf.Abs(mapFirstTime - fogFirstTime) < .05f, "fog and map start enlarging together on the same sampled frame");
        Require(fogFullTime - fogFirstTime >= 2.3f && fogFullTime - fogFirstTime < 4f && environment.entrance.fogZoomDuration == 2.5f,
            "layered fog reaches 90 percent travel and full opacity on the extended 2.5-second schedule");
        Require(mapFullTime - mapFirstTime >= 3.8f && mapFullTime - mapFirstTime < 5.5f && environment.entrance.mapZoomDuration == 4,
            "map completes its extended four-second sequence including stagger");
        Require(mapContinuesAfterFog && mapFullTime - fogFullTime > 1.3f,
            "map and fog share the final 1.5-second settling phase");
        Report.AppendLine($"Fog travel during final map phase: {fogTailStart:F4} -> {fogTailEnd:F4}");
        Require(fogTailStart >= .8999f && fogTailStart < .905f && fogTailEnd > .995f && fogTailMovesPosition,
            "fog scale and position continue from 90 to nearly 100 percent during the map's final phase without snapping");
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
        Require(centerFog.All(s => s == null), "both additional BigFog sprites are removed after their entrance");
        Require(fogCanvas == null && icons.All(icon => ((Transform)Read(icon, "target")).GetComponent<Canvas>() == null
            && ((Transform)Read(icon, "target")).GetComponent<GraphicRaycaster>() == null),
            "temporary icon canvases, their raycasters and the fog canvas are removed after the entrance");
        foreach (var element in hud)
        {
            var rect = (RectTransform)Read(element, "rect");
            Report.AppendLine($"HUD restore {rect.name}: position={rect.anchoredPosition:F5}, expected={(Vector2)Read(element, "position"):F5}, group={rect.GetComponent<CanvasGroup>() != null}, added={Read(element, "addedGroup")}");
        }
        Require(hud.All(h => Vector2.Distance(((RectTransform)Read(h, "rect")).anchoredPosition, (Vector2)Read(h, "position")) < .001f
            && (!(bool)Read(h, "addedGroup") || ((RectTransform)Read(h, "rect")).GetComponent<CanvasGroup>() == null)),
            "HUD returns to its exact authored position and temporary fade groups are removed");
        var dining = canvas.facilities.Single(f => f.facility == Facility.Dining).button;
        var pointer = new PointerEventData(EventSystem.current) {
            position = RectTransformUtility.WorldToScreenPoint(null, dining.transform.TransformPoint(((RectTransform)dining.transform).rect.center)) };
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
        Require(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == dining,
            "the original facility button receives the top pointer raycast after cleanup");
        var lockedFog = environment.GetComponent<HideOutLockedFog>();
        var regions = ((IDictionary)Read(lockedFog, "regions")).Values.Cast<object>().ToArray();
        foreach (var region in regions)
        {
            float[] depths = ((IList)Read(region, "clouds")).Cast<object>().Select(c => (float)Read(c, "depth")).ToArray();
            Require(depths.Min() <= .08f && depths.Max() >= 1.4f && depths.All(d => d >= .04f && d <= 1.45f),
                "each region spans separated depth bands including a layer close to the map");
        }
        var cloudStates = regions.SelectMany(r => ((IList)Read(r, "clouds")).Cast<object>()).ToArray();
        var parallax = typeof(HideOutLockedFog).GetMethod("Parallax", Fields);
        Vector3 oldCameraPosition = camera.transform.position;
        Vector3[] offsets = cloudStates.Select(c => (Vector3)parallax.Invoke(lockedFog, new[] { c })).ToArray();
        camera.transform.position += Vector3.right;
        float[] travel = cloudStates.Select((c, i) => Vector3.Distance(offsets[i],
            (Vector3)parallax.Invoke(lockedFog, new[] { c }))).ToArray();
        camera.transform.position = oldCameraPosition;
        Report.AppendLine($"One-unit camera pan: cloud parallax {travel.Min():F4} to {travel.Max():F4} world units");
        Require(travel.Min() < .012f && travel.Max() > .19f, "pan separates cloud layers while the back layer stays close to map motion");
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
