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
    private void Update()
    {
        held = Keyboard.current?.escapeKey.isPressed == true ? held + Time.unscaledDeltaTime : 0;
        if (held >= 2f) Application.Quit(0);
    }
    private void OnApplicationFocus(bool focused) { if (!focused) held = 0; }
#endif
}
