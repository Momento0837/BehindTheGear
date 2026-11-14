#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
#if UNITY_EDITOR_WIN
using System.Diagnostics;
using System.Runtime.InteropServices;
#else
using UnityEngine.InputSystem;
#endif

/// <summary>Keyboard-only Play navigation, including when the Scene view has keyboard focus.</summary>
[InitializeOnLoad]
public static class DevelopmentSceneShortcutMenu
{
    private static int previousChord;
#if UNITY_EDITOR_WIN
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern System.IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(System.IntPtr window, out uint process);
    private static readonly uint EditorProcessId = (uint)Process.GetCurrentProcess().Id;
    private static bool Held(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
#endif

    static DevelopmentSceneShortcutMenu()
    {
        EditorApplication.update += Update;
        SceneView.duringSceneGui += SceneInput;
    }

    private static int ReadChord()
    {
#if UNITY_EDITOR_WIN
        // Ignore shortcuts typed into other applications.
        GetWindowThreadProcessId(GetForegroundWindow(), out uint foregroundProcess);
        if (foregroundProcess != EditorProcessId || !Held(0x11)) return 0;
        if (Held(0x31) || Held(0x61)) return 1;
        if (Held(0x32) || Held(0x62)) return 2;
        if (Held(0x33) || Held(0x63)) return 3;
#else
        var keyboard = Keyboard.current;
        if (DevelopmentSceneShortcuts.IsShortcutChord(keyboard))
        {
            if (keyboard.digit1Key.isPressed || keyboard.numpad1Key.isPressed) return 1;
            if (keyboard.digit2Key.isPressed || keyboard.numpad2Key.isPressed) return 2;
            if (keyboard.digit3Key.isPressed || keyboard.numpad3Key.isPressed) return 3;
        }
#endif
        return 0;
    }

    private static bool CanNavigate => EditorApplication.isPlaying && !EditorApplication.isCompiling
        && !EditorApplication.isUpdating && !EditorApplication.isPaused;

    private static void Update()
    {
        int chord = ReadChord();
        bool pressed = chord != 0 && chord != previousChord;
        previousChord = chord;
        if (!CanNavigate) return;
        DevelopmentSceneShortcuts.EnsureInitialized();
        if (pressed) Navigate(chord);
    }

    private static void SceneInput(SceneView view)
    {
        if (!CanNavigate) return;
        Event input = Event.current;
        if (input.type != EventType.KeyDown || !input.control) return;
        int chord = input.keyCode switch
        {
            KeyCode.Alpha1 or KeyCode.Keypad1 => 1,
            KeyCode.Alpha2 or KeyCode.Keypad2 => 2,
            KeyCode.Alpha3 or KeyCode.Keypad3 => 3,
            _ => 0
        };
        if (chord == 0) return;
        input.Use();
        if (chord == previousChord) return;
        previousChord = chord;
        Navigate(chord);
    }

    private static void Navigate(int chord)
    {
        string path = chord switch
        {
            1 => DevelopmentSceneShortcuts.ForestPath,
            2 => DevelopmentSceneShortcuts.MarketPath,
            3 => DevelopmentSceneShortcuts.HideOutPath,
            _ => null
        };
        if (path != null) DevelopmentSceneShortcuts.Travel(path);
    }
}
#endif
