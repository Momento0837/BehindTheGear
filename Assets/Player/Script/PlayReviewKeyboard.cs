#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>Temporary virtual keyboard used only by the editor play review.</summary>
public sealed class PlayReviewKeyboard : MonoBehaviour
{
    public static Key[] Held = Array.Empty<Key>();
    private Keyboard keyboard;
    private InputSettings.BackgroundBehavior previousBackground;
    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        previousBackground = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        keyboard = InputSystem.AddDevice<Keyboard>("PlayReviewKeyboard");
        InputSystem.onBeforeUpdate += Feed;
    }
    private void Feed()
    {
        if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
        keyboard.MakeCurrent();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Held));
    }
    private void OnDestroy()
    {
        InputSystem.onBeforeUpdate -= Feed;
        Held = Array.Empty<Key>();
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        InputSystem.settings.backgroundBehavior = previousBackground;
    }
}
#endif
