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

    private void Update()
    {
        if (Keyboard.current?.escapeKey.wasPressedThisFrame == true && !InventoryUI.EscapeHandledThisFrame)
            Application.Quit(0);
    }
#endif
}
