using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>Reads the WASD, mouse and Q/E controls.</summary>
public sealed class PlayerInputReader : MonoBehaviour
{
    public event Action<float> MoveChanged;
    public event Action<bool> CrouchChanged;
    public event Action DashPressed;
    public bool WalkHeld { get; private set; }
    public Vector2 AimDirection { get; private set; } = Vector2.right;
    public event Action JumpPressed;
    public event Action BasicAttackPressed;
    public event Action SecondaryAttackPressed;
    public event Action SkillOnePressed;
    public event Action SkillTwoPressed;
    public event Action SkillTwoReleased;
    public event Action PrimaryClickPressed;
    public event Action SecondaryClickPressed;
    public event Action InteractPressed;

    private float horizontal;
    private bool crouching;
    private bool dashHeld;
    private readonly List<RaycastResult> uiHits = new();
    private static int pointerSuppressedFrame = -1;

    public static void SuppressPointerForCurrentFrame() => pointerSuppressedFrame = Time.frameCount;

    private void OnEnable()
    {
        // A key pressed while controls were disabled must be released before it can dash.
        var keyboard = Keyboard.current;
        dashHeld = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
        throwWasHeld = IsThrowHeld(keyboard);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        bool shiftHeld = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
        bool dashPressed = shiftHeld && !dashHeld;
        // Consume the edge even while UI suppresses gameplay; never queue a dash.
        dashHeld = shiftHeld;
        if (SceneFadeTransition.IsRunning || InventoryUI.GameplayInputSuppressedThisFrame || PlayerSkillDevelopmentMenu.IsOpen)
        {
            throwWasHeld = IsThrowHeld(keyboard);
            WalkHeld = false;
            SetHorizontal(0f);
            SetCrouching(false);
            return;
        }
        if (keyboard == null)
        {
            throwWasHeld = false;
            WalkHeld = false;
            SetHorizontal(0f);
            SetCrouching(false);
            return;
        }

        WalkHeld = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;

        float nextHorizontal = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
        SetHorizontal(nextHorizontal);
        float verticalAim = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
        var aim = new Vector2(nextHorizontal, verticalAim);
        if (aim.sqrMagnitude > 0) AimDirection = aim.normalized;

        bool nextCrouching = keyboard.sKey.isPressed;
        SetCrouching(nextCrouching);

        if (dashPressed) DashPressed?.Invoke();

        bool jumpPressed = keyboard.wKey.wasPressedThisFrame
            || keyboard.spaceKey.wasPressedThisFrame;
        if (jumpPressed) JumpPressed?.Invoke();

        {
            bool primaryClick = Mouse.current?.leftButton.wasPressedThisFrame == true;
            bool secondaryClick = Mouse.current?.rightButton.wasPressedThisFrame == true;
            if ((primaryClick || secondaryClick) && !IsPointerOverUI())
            {
                if (primaryClick)
                {
                    BasicAttackPressed?.Invoke();
                    PrimaryClickPressed?.Invoke();
                }
                if (secondaryClick)
                {
                    SecondaryAttackPressed?.Invoke();
                    SecondaryClickPressed?.Invoke();
                }
            }

            if (keyboard.qKey.wasPressedThisFrame) SkillOnePressed?.Invoke();
        }
        bool throwHeld = keyboard.eKey.isPressed;
        if (throwHeld && !throwWasHeld) SkillTwoPressed?.Invoke();
        if (!throwHeld && throwWasHeld) SkillTwoReleased?.Invoke();
        throwWasHeld = throwHeld;

        if (keyboard.fKey.wasPressedThisFrame) InteractPressed?.Invoke();
    }

    private void OnDisable()
    {
        WalkHeld = false;
        var keyboard = Keyboard.current;
        dashHeld = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
        throwWasHeld = IsThrowHeld(keyboard);
        SetHorizontal(0f);
        SetCrouching(false);
    }

    private bool throwWasHeld;
    private static bool IsThrowHeld(Keyboard keyboard) => keyboard != null && keyboard.eKey.isPressed;

    private void SetHorizontal(float value)
    {
        if (!Mathf.Approximately(horizontal, value))
        {
            horizontal = value;
            MoveChanged?.Invoke(horizontal);
        }
    }

    private void SetCrouching(bool value)
    {
        if (crouching != value)
        {
            crouching = value;
            CrouchChanged?.Invoke(crouching);
        }
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
