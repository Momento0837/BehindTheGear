using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Allows the standalone Windows player to exit even while gameplay controls are locked.</summary>
public sealed class WindowsQuitOnEscape : MonoBehaviour
{
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        var root = new GameObject("Windows Exit Shortcut");
        DontDestroyOnLoad(root);
        root.AddComponent<WindowsQuitOnEscape>();
    }

    private float held;
    private bool waitForEscapeRelease;
    private void Update()
    {
        bool escapeHeld = Keyboard.current?.escapeKey.isPressed == true;
        // Escape dismisses the warning, and that same held press must not quit the build.
        if (ControlSchemeSelectScene.IsWarningVisible)
        {
            held = 0f;
            waitForEscapeRelease = escapeHeld;
            return;
        }
        if (waitForEscapeRelease)
        {
            held = 0f;
            if (!escapeHeld) waitForEscapeRelease = false;
            return;
        }
        held = escapeHeld ? held + Time.unscaledDeltaTime : 0;
        if (held >= 2f) Application.Quit(0);
    }
    private void OnApplicationFocus(bool focused) { if (!focused) held = 0; }
#endif
}
