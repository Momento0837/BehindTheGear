using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>New Input System keyboard/mouse reader. It has no movement or attack implementation.</summary>
public sealed class PlayerInputReader : MonoBehaviour
{
    public event Action<float> MoveChanged;
    public event Action<bool> CrouchChanged;
    public event Action JumpPressed;
    public event Action AttackZPressed;
    public event Action AttackXPressed;
    public event Action AttackCPressed;
    public event Action AttackVPressed;
    public event Action PrimaryClickPressed;
    public event Action SecondaryClickPressed;
    public event Action InteractPressed;

    private float horizontal;
    private bool crouching;
    private readonly List<RaycastResult> uiHits = new();
    private static int pointerSuppressedFrame = -1;

    public static void SuppressPointerForCurrentFrame() => pointerSuppressedFrame = Time.frameCount;

    private void Update()
    {
        if (Keyboard.current == null) return;

        float nextHorizontal = (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed ? 1f : 0f)
            - (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed ? 1f : 0f);
        if (!Mathf.Approximately(horizontal, nextHorizontal))
        {
            horizontal = nextHorizontal;
            MoveChanged?.Invoke(horizontal);
        }

        bool nextCrouching = Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed;
        if (crouching != nextCrouching)
        {
            crouching = nextCrouching;
            CrouchChanged?.Invoke(crouching);
        }

        if (Keyboard.current.wKey.wasPressedThisFrame || Keyboard.current.upArrowKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame)
            JumpPressed?.Invoke();
        if (Keyboard.current.zKey.wasPressedThisFrame) AttackZPressed?.Invoke();
        if (Keyboard.current.xKey.wasPressedThisFrame) AttackXPressed?.Invoke();
        if (Keyboard.current.cKey.wasPressedThisFrame) AttackCPressed?.Invoke();
        if (Keyboard.current.vKey.wasPressedThisFrame) AttackVPressed?.Invoke();
        bool primaryClick = Mouse.current?.leftButton.wasPressedThisFrame == true;
        bool secondaryClick = Mouse.current?.rightButton.wasPressedThisFrame == true;
        if ((primaryClick || secondaryClick) && !IsPointerOverUI())
        {
            if (primaryClick) PrimaryClickPressed?.Invoke();
            if (secondaryClick) SecondaryClickPressed?.Invoke();
        }
        if (Keyboard.current.fKey.wasPressedThisFrame) InteractPressed?.Invoke();
    }

    private bool IsPointerOverUI()
    {
        if (pointerSuppressedFrame == Time.frameCount) return true;
        if (EventSystem.current == null || Mouse.current == null) return false;
        // Raycast the current position; cached pointer state can be one frame behind input.
        PointerEventData pointer = new(EventSystem.current) { position = Mouse.current.position.ReadValue() };
        uiHits.Clear();
        EventSystem.current.RaycastAll(pointer, uiHits);
        return uiHits.Exists(hit => hit.module is UnityEngine.UI.GraphicRaycaster);
    }
}
