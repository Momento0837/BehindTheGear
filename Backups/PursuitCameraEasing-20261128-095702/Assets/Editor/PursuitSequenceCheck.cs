#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class PursuitSequenceCheck
{
    private const string Folder = "Logs/Validation/PursuitSequence";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static PursuitSequenceCheck() => EditorApplication.update += Poll;
    private static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode
            || !File.Exists(Folder + "/smooth-and-market.request")) return;
        File.Delete(Folder + "/smooth-and-market.request");
        Run();
    }
    [MenuItem("Tools/Checks/Pursuit Sequence")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Directory.CreateDirectory(Folder);
        var report = new StringBuilder();
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            foreach (string path in new[] { "Dialogue/Garbage/garbage_pursuit_shout",
                "Dialogue/Market/market_pursuit_shout", "Dialogue/Market/market_pursuit_shout_left" })
            {
                var data = Resources.Load<TextAsset>(path);
                Require(data != null && ConversationDialogue.GetGreetingLines(data, int.MaxValue).Length == 1,
                    "Exactly one shout in " + path);
            }
            Require(Resources.Load<TextAsset>("Dialogue/Forest/forest_combat_monologue") != null, "Post-combat monologue exists");
            report.AppendLine("PASS three pursuit shouts and separate post-combat dialogue");

            var visual = New(preview, "Bubble alpha check", typeof(RectTransform), typeof(CanvasGroup));
            var bubble = visual.AddComponent<AutomaticNpcBubble>();
            Set(bubble, "bubble", visual.GetComponent<RectTransform>());
            Set(bubble, "group", visual.GetComponent<CanvasGroup>());
            bubble.ScreenRight = true;
            bubble.Opacity = .5f;
            foreach (float t in new[] { 0f, .1f, .22f, .3f, .48f, 1f })
            {
                Call(bubble, "ApplyEntrance", t);
                float alpha = visual.GetComponent<CanvasGroup>().alpha;
                Require(alpha >= 0f && alpha <= .50001f, "Right bubble opacity capped throughout its entrance");
            }
            Require(Mathf.Abs(visual.GetComponent<CanvasGroup>().alpha - .5f) < .00001f, "Right bubble holds 50 percent opacity");
            bubble.ScreenRight = false; bubble.ScreenLeft = true; bubble.Opacity = 1f;
            Call(bubble, "ApplyEntrance", .48f);
            Require(Mathf.Approximately(visual.GetComponent<CanvasGroup>().alpha, 1f), "Left bubble reaches full opacity");
            report.AppendLine("PASS right 50 percent / left 100 percent entrance opacity");

            var cameraObject = New(preview, "Pursuit camera check", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 5; camera.pixelRect = new Rect(0, 0, 1920, 1080); camera.aspect = 16f / 9f;
            cameraObject.transform.position = new Vector3(55, 0, -10);
            var follow = cameraObject.AddComponent<PlayerCameraFollow2D>();
            var player = New(preview, "Pursuit player check");
            player.transform.position = new Vector3(49, -2.5f, 0);
            Set(follow, "attachedCamera", camera);
            Set(follow, "cameraZ", -10f);
            Set(follow, "target", player.transform);
            Set(follow, "clampToHorizontalBounds", true);
            Set(follow, "horizontalWorldBounds", new Vector2(44.92f, 97.28f));
            follow.BeginPursuitFocus();
            follow.AimPursuitView(true);
            Require(Mathf.Approximately((float)Read(follow, "pursuitWorldX"), 97.28f), "Right view targets map's right side");
            camera.orthographicSize = 4.5f;
            float right = (float)Call(follow, "ClampHorizontalPosition", 97.28f);
            cameraObject.transform.position = new Vector3(right, 0, -10);
            Require(follow.PursuitViewSettled, "Camera arrival respects zoomed right boundary");
            follow.AimPursuitView(false, duration: .5f, motionBlur: true);
            float left = (float)Call(follow, "ClampHorizontalPosition", (float)Read(follow, "pursuitWorldX"));
            Require(left < right && left >= 44.92f, "Return view moves left toward the player inside map bounds");
            Require(!follow.PursuitViewSettled, "Timed return cannot finish before its half-second motion");
            float previousX = right, endpoint = right, peakBlur = 0f;
            for (int frame = 0; frame < 30; frame++)
            {
                endpoint = (float)Call(follow, "StepPursuitPan", left, 1f / 60f);
                Call(follow, "UpdatePursuitBlur", endpoint - previousX, 1f / 60f);
                cameraObject.transform.position = new Vector3(endpoint, 0, -10);
                previousX = endpoint;
                peakBlur = Mathf.Max(peakBlur, PrototypeScreenEffects.StoryPanBlurPixels);
            }
            float remainingBlur = PrototypeScreenEffects.StoryPanBlurPixels;
            Require(Mathf.Abs(endpoint - left) < .001f && !(bool)Read(follow, "pursuitTimedPan"),
                "Camera movement ends in exactly half a second");
            Require(peakBlur > 30f && remainingBlur > 0f && remainingBlur < peakBlur,
                "Blur follows movement speed and decelerates without snapping to zero");
            Call(follow, "UpdatePursuitBlur", 0f, 1f / 60f);
            Require(PrototypeScreenEffects.StoryPanBlurPixels > 0f && PrototypeScreenEffects.StoryPanBlurPixels < remainingBlur,
                "Residual blur fades smoothly after the camera stops");
            for (int frame = 0; frame < 90; frame++) Call(follow, "UpdatePursuitBlur", 0f, 1f / 60f);
            Require(PrototypeScreenEffects.StoryPanBlurPixels == 0f, "Residual blur fully clears");
            Require(follow.PursuitViewSettled, "Player-side arrival is detected after the blur settles");
            follow.EndPursuitFocus();
            Require(Read(follow, "pursuitWorldX") == null && !(bool)Read(follow, "pursuitFocus"), "Pursuit camera target is released");
            report.AppendLine("PASS bounded right-to-player targets, half-second strong blur, arrival, and cleanup");

            Require(follow.TryGetRightMapEdge(out Vector3 flatEdge) && Mathf.Abs(flatEdge.x - 97.28f) < .001f
                && flatEdge.z == 0f, "Orthographic pursuit uses the configured map edge");
            camera.orthographic = false;
            camera.fieldOfView = 20f;
            cameraObject.transform.position = new Vector3(71.1f, 0f, -27.24f);
            camera.ResetWorldToCameraMatrix();
            var artwork = (System.Collections.Generic.List<Bounds>)Read(follow, "mapArtwork");
            artwork.Add(new Bounds(new Vector3(71.1f, 0f, 5.77f), new Vector3(52.36f, 20f, .01f)));
            Require(follow.TryGetRightMapEdge(out Vector3 artEdge) && Mathf.Abs(artEdge.z - 5.77f) < .001f,
                "Perspective pursuit uses the artwork plane that actually limits the camera");
            float edgeCameraX = (float)Call(follow, "ClampHorizontalPosition", 97.28f);
            cameraObject.transform.position = new Vector3(edgeCameraX, 0f, -27.24f);
            camera.ResetWorldToCameraMatrix();
            Require(Mathf.Abs(camera.WorldToViewportPoint(artEdge).x - 1f) < .001f,
                "Pursuit source reaches the visible rightmost map edge");

            var overlay = New(preview, "Map-edge bubble layout check", typeof(RectTransform), typeof(Canvas));
            var overlayCanvas = overlay.GetComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            visual.transform.SetParent(overlay.transform, false);
            var bubbleRect = visual.GetComponent<RectTransform>();
            bubbleRect.anchorMin = bubbleRect.anchorMax = new Vector2(.5f, .5f);
            bubbleRect.sizeDelta = new Vector2(200f, 80f);
            bubbleRect.pivot = new Vector2(.9f, .05f);
            var background = New(preview, "Map-edge background", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            background.transform.SetParent(visual.transform, false);
            Set(bubble, "canvas", overlayCanvas);
            Set(bubble, "image", background.GetComponent<UnityEngine.UI.Image>());
            bubble.ScreenLeft = false; bubble.ScreenRight = true; bubble.RightMapEdge = artEdge;
            Call(bubble, "ApplyEntrance", 1f);
            Canvas.ForceUpdateCanvases();
            Call(bubble, "PositionBubble", camera);
            float anchoredX = bubbleRect.anchoredPosition.x;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)overlay.transform,
                camera.WorldToScreenPoint(artEdge), null, out Vector2 edgeLocal);
            Require(Mathf.Abs(anchoredX + bubbleRect.rect.width * (1f - bubbleRect.pivot.x) - edgeLocal.x) < .01f,
                "Bubble right border aligns with the world map edge");
            cameraObject.transform.position += Vector3.left;
            camera.ResetWorldToCameraMatrix();
            Call(bubble, "PositionBubble", camera);
            Require(bubbleRect.anchoredPosition.x > anchoredX + 1f,
                "Right bubble follows the world edge instead of staying at a viewport percentage");
            report.AppendLine("PASS world-anchored right bubble, perspective map edge, and camera-relative projection");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Settings/WorldMotionEffects.shader");
            Require(shader != null && !ShaderUtil.ShaderHasError(shader), "World motion shader imported without errors");
            report.AppendLine("PASS transient world-blur shader import");

            var flowObject = New(preview, "Completed combat check");
            flowObject.SetActive(false);
            var flow = flowObject.AddComponent<StorySceneFlow>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            cameraObject.transform.position = new Vector3(55f, 0f, -10f);
            camera.ResetWorldToCameraMatrix();
            artwork.Clear();
            Set(follow, "cameraZ", -10f);
            Set(follow, "hasDialogueLens", false);
            follow.StoryLookOffset = .7f;
            var rightShout = (IEnumerator)Call(flow, "PanToRightPursuitShout",
                "Dialogue/Garbage/garbage_pursuit_shout", follow, 1f, true);
            Require(rightShout.MoveNext() && rightShout.Current is IEnumerator,
                "Right shout begins with a camera arrival wait");
            Require((bool)Read(follow, "pursuitFocus") && Mathf.Approximately((float)Read(follow, "pursuitWorldX"), 97.28f)
                && follow.StoryLookOffset == 0f, "Shared shout flow targets the full map edge, not a short look offset");
            var arrival = (IEnumerator)rightShout.Current;
            Require(arrival.MoveNext() && arrival.MoveNext() && !follow.PursuitViewSettled,
                "Bubble flow waits while the camera is still on the player side");
            Require(flowObject.GetComponent<AutomaticNpcBubble>() == null, "No bubble is spawned before camera arrival");
            for (int frame = 0; frame < 30; frame++)
            {
                Call(follow, "UpdateFocusLens", 4.5f, 1f / 60f);
                float destination = (float)Call(follow, "ClampHorizontalPosition", 97.28f);
                float panX = (float)Call(follow, "StepPursuitPan", destination, 1f / 60f);
                cameraObject.transform.position = new Vector3(panX, 0f, -10f);
                camera.ResetWorldToCameraMatrix();
            }
            Require(follow.PursuitPositionSettled && !follow.PursuitViewSettled && !arrival.MoveNext(),
                "First bubble starts after half-second pan while its gentle zoom continues");
            Require(camera.orthographicSize > 4.8f, "First shout retains the original gradual zoom rather than snapping in");
            Require(rightShout.MoveNext() && rightShout.Current is IEnumerator,
                "Bubble playback follows the completed camera movement");
            (rightShout as IDisposable)?.Dispose();
            follow.EndPursuitFocus();
            report.AppendLine("PASS shared Garbage/Market shout camera pan and bubble-after-arrival sequencing");

            foreach (int fps in new[] { 30, 60, 144 })
            {
                camera.orthographic = false;
                camera.fieldOfView = 20f;
                cameraObject.transform.position = new Vector3(55f, 0f, -27.24f);
                camera.ResetWorldToCameraMatrix();
                Set(follow, "cameraZ", -27.24f);
                Set(follow, "hasDialogueLens", false);
                artwork.Clear();
                artwork.Add(new Bounds(new Vector3(71.1f, 0f, 5.77f), new Vector3(52.36f, 20f, .01f)));
                follow.BeginPursuitFocus();
                follow.AimPursuitView(true, duration: .5f);
                int frames = Mathf.CeilToInt(.5f * fps) + 1;
                for (int frame = 0; frame < frames; frame++)
                {
                    Call(follow, "UpdateFocusLens", 18f, 1f / fps);
                    float destination = (float)Call(follow, "ClampHorizontalPosition", 97.28f);
                    float panX = (float)Call(follow, "StepPursuitPan", destination, 1f / fps);
                    cameraObject.transform.position = new Vector3(panX, 0f, -27.24f);
                    camera.ResetWorldToCameraMatrix();
                }
                Require(follow.PursuitViewSettled && Mathf.Abs(camera.fieldOfView - 18f) < .03f,
                    "Perspective map-edge movement and zoom settle in half a second at " + fps + " FPS");
                follow.EndPursuitFocus();
            }
            report.AppendLine("PASS half-second pursuit move and zoom at 30 / 60 / 144 FPS");

            var actor = player.AddComponent<PlayerMovement2D>();
            Set(actor, "body", player.GetComponent<Rigidbody2D>());
            Set(actor, "box", player.GetComponent<BoxCollider2D>());
            var suspensionType = typeof(PortalDeparture).GetNestedType("CameraSuspension", BindingFlags.NonPublic);
            object outgoing = Activator.CreateInstance(suspensionType, new object[] { actor, follow });
            PortalArrival incoming = null;
            try
            {
                Require(!follow.enabled, "Departure suspends camera tracking");
                PortalDeparture.ResumeCameraForArrival(null);
                Require(!follow.enabled, "Only the travelling player can hand over its camera");
                PortalDeparture.ResumeCameraForArrival(actor);
                Require(follow.enabled, "Black-screen handoff restores the original camera state before arrival");
                incoming = (PortalArrival)Activator.CreateInstance(typeof(PortalArrival), Private,
                    null, new object[] { actor, camera }, null);
                Require(!follow.enabled && (bool)Read(incoming, "followEnabled"),
                    "Arrival temporarily suspends an originally enabled camera");
                player.transform.position = (Vector3)Read(incoming, "destination");
                player.GetComponent<Rigidbody2D>().position = player.transform.position;
                Require(!incoming.Enter().MoveNext() && follow.enabled,
                    "Arrival completion restores camera tracking before Market dialogue");
                var marketShout = (IEnumerator)Call(flow, "PanToRightPursuitShout",
                    "Dialogue/Market/market_pursuit_shout", follow, .5f, false);
                Require(marketShout.MoveNext() && (bool)Read(follow, "pursuitFocus")
                    && Read(follow, "pursuitWorldX") != null,
                    "Market pursuit acquires its camera after the actual portal arrival lifecycle");
                (marketShout as IDisposable)?.Dispose();
                follow.EndPursuitFocus();
                follow.enabled = false;
                suspensionType.GetMethod("Release").Invoke(outgoing, null);
                Require(!follow.enabled, "Completed departure cannot overwrite a subsequent camera owner");
                object originallyDisabled = Activator.CreateInstance(suspensionType, new object[] { actor, follow });
                PortalDeparture.ResumeCameraForArrival(actor);
                Require(!follow.enabled, "Originally disabled cameras remain disabled after handoff");
                suspensionType.GetMethod("Release").Invoke(originallyDisabled, null);
            }
            finally
            {
                incoming?.Dispose();
                suspensionType.GetMethod("Release").Invoke(outgoing, null);
                follow.enabled = true;
            }
            report.AppendLine("PASS portal departure-to-arrival camera handoff and Market pursuit activation");
            Set(flow, "ready", true);
            var completed = (IEnumerator)Call(flow, "FinishForestCombat");
            Require(!completed.MoveNext(), "Completed encounter does not replay its monologue");
            report.AppendLine("PASS completed-combat replay guard");
            report.AppendLine("RESULT: PASS");
        }
        catch (Exception error) { report.AppendLine("RESULT: FAIL\n" + error); Debug.LogException(error); }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
            File.WriteAllText(Folder + "/result.txt", report.ToString());
        }
    }
    private static GameObject New(Scene scene, string name, params Type[] components)
    {
        var go = new GameObject(name, components);
        SceneManager.MoveGameObjectToScene(go, scene);
        return go;
    }
    private static object Read(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
    private static void Require(bool success, string message) { if (!success) throw new Exception(message); }
}
#endif
