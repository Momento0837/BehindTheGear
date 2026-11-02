using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class ControlSchemeStartupCheck
{
    private const string Active = "BTG.ControlStartupCheck";
    private const string Preference = "BehindTheGear.PlayerControlScheme";
    private const string Output = "C:/Users/user/Documents/ChatGPT/BehindTheGear/play-review/control-startup-validation";
    private static int phase;
    private static double started, since;
    private static float origin;

    static ControlSchemeStartupCheck()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Active, false))
                EditorApplication.delayCall += () =>
                {
                    phase = 0;
                    started = since = 0;
                    EditorApplication.isPlaying = true;
                };
        };
    }

    [MenuItem("Tools/Checks/Control Scheme Startup")]
    public static void Run()
    {
        if (Application.unityVersion != "6000.3.23f1" || EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
            throw new Exception("Use Unity 6000.3.23f1 edit mode with the current scene saved");
        if (EditorBuildSettings.scenes.First(s => s.enabled).path != "Assets/Scenes/Controls/ControlSchemeSelect.unity")
            throw new Exception("Control selection must be the first build scene");
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/result.txt", "RUNNING\n");
        SessionState.SetBool(Active + ".HadPreference", PlayerPrefs.HasKey(Preference));
        SessionState.SetInt(Active + ".Preference", PlayerPrefs.GetInt(Preference));
        SessionState.SetInt(Active + ".Mode", 0);
        SessionState.SetBool(Active, true);
        phase = 0;
        started = since = 0;
        EditorApplication.isPlaying = true;
    }

    private static void Finish(string message)
    {
        File.AppendAllText(Output + "/result.txt", message + "\n");
        if (SessionState.GetBool(Active + ".HadPreference", false)) PlayerPrefs.SetInt(Preference, SessionState.GetInt(Active + ".Preference", 0));
        else PlayerPrefs.DeleteKey(Preference);
        PlayerPrefs.Save();
        SessionState.SetBool(Active, false);
        PlayReviewKeyboard.Held = Array.Empty<Key>();
        EditorApplication.isPlaying = false;
        Debug.Log("CONTROL_STARTUP_CHECK " + message);
        if (Application.isBatchMode)
            EditorApplication.delayCall += () => EditorApplication.Exit(message.StartsWith("PASS") ? 0 : 1);
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Active, false) || !EditorApplication.isPlaying) return;
        try
        {
            double now = EditorApplication.timeSinceStartup;
            int mode = SessionState.GetInt(Active + ".Mode", 0);
            bool wasd = mode == 0;
            if (started == 0)
            {
                started = since = now;
                new GameObject("Control Check Keyboard").AddComponent<PlayReviewKeyboard>();
            }
            Require(now - started < 45, "Startup check timed out at phase " + phase);
            if (phase == 0)
            {
                Require(SceneManager.GetActiveScene().name == "ControlSchemeSelect", "First screen is not control selection");
                if (now - since < .5) return;
                var selection = UnityEngine.Object.FindFirstObjectByType<ControlSchemeSelectScene>();
                Require(selection != null, "Selection controller missing");
                string method = wasd ? "SelectWasdMouseQe" : "SelectArrowKeys";
                var button = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
                    .First(b => b.onClick.GetPersistentEventCount() > 0 && b.onClick.GetPersistentMethodName(0) == method);
                ScreenCapture.CaptureScreenshot(Path.GetFullPath(Output + "/selection-" + mode + ".png"));
                button.onClick.Invoke();
                // A second selection during the fade must not overwrite the first choice.
                if (wasd) selection.SelectArrowKeys(); else selection.SelectWasdMouseQe();
                Require(PlayerControlSchemeSettings.Current == (wasd ? PlayerControlScheme.WasdMouseQe : PlayerControlScheme.ArrowKeys), "Selected control scheme was overwritten");
                phase = 1;
            }
            else if (phase == 1)
            {
                if (SceneManager.GetActiveScene().name != "Forest") return;
                var video = UnityEngine.Object.FindFirstObjectByType<CutsceneVideoSequence>();
                var player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement2D>();
                if (player == null) return;
                var flow = UnityEngine.Object.FindFirstObjectByType<StorySceneFlow>();
                if (flow == null || typeof(StorySceneFlow).GetField("monsters", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(flow) == null) return;
                if (video != null && video.IsPlaying) { PlayReviewKeyboard.Held = new[] { Key.Space }; return; }
                if (!player.enabled) return;
                PlayReviewKeyboard.Held = Array.Empty<Key>();
                origin = player.transform.position.x;
                since = Time.unscaledTime;
                phase = 2;
            }
            else if (phase == 2)
            {
                var player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement2D>();
                PlayReviewKeyboard.Held = new[] { wasd ? Key.A : Key.LeftArrow };
                if (Time.unscaledTime - since < .4) return;
                Require(player.transform.position.x < origin - .5f,
                    $"Chosen movement keys do not move player: mode={PlayerControlSchemeSettings.Current}, origin={origin}, position={player.transform.position}, movement={player.enabled}, input={player.GetComponent<PlayerInputReader>().enabled}, velocity={player.GetComponent<Rigidbody2D>().linearVelocity}, a={Keyboard.current?.aKey.isPressed}, left={Keyboard.current?.leftArrowKey.isPressed}, scale={Time.timeScale}");
                PlayReviewKeyboard.Held = new[] { wasd ? Key.RightArrow : Key.D };
                since = Time.unscaledTime;
                phase = 3;
            }
            else if (phase == 3)
            {
                // Allow one input frame for the previous direction to be released.
                if (Time.unscaledTime - since < .15) return;
                origin = UnityEngine.Object.FindFirstObjectByType<PlayerMovement2D>().transform.position.x;
                since = Time.unscaledTime;
                phase = 4;
            }
            else if (phase == 4)
            {
                if (Time.unscaledTime - since < .35) return;
                var player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement2D>();
                Require(Mathf.Abs(player.transform.position.x - origin) < .1f, "Unselected movement keys moved player");
                File.AppendAllText(Output + "/result.txt", (wasd ? "WASD" : "Arrow") + ": selection screen -> Forest, choice retained, selected keys move, other keys ignored, duplicate click guarded.\n");
                PlayReviewKeyboard.Held = Array.Empty<Key>();
                if (wasd)
                {
                    SessionState.SetInt(Active + ".Mode", 1);
                    EditorApplication.isPlaying = false;
                }
                else Finish("PASS Unity " + Application.unityVersion + "; both control schemes checked in actual Play mode. Previous preferences restored. No scenes saved.");
            }
        }
        catch (Exception error) { Finish("FAIL: " + error); }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
