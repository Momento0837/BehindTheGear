using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>Reads only the selected control layout and publishes movement and action input.</summary>
public sealed class PlayerInputReader : MonoBehaviour
{
    public event Action<float> MoveChanged;
    public event Action<bool> CrouchChanged;
    public event Action JumpPressed;
    public event Action BasicAttackPressed;
    public event Action SecondaryAttackPressed;
    public event Action SkillOnePressed;
    public event Action SkillTwoPressed;
    // Kept for callers that still use the original button-specific events.
    public event Action AttackZPressed;
    public event Action AttackXPressed;
    public event Action AttackCPressed;
    public event Action AttackVPressed;
    public event Action PrimaryClickPressed;
    public event Action SecondaryClickPressed;
    public event Action InteractPressed;

    private float horizontal;
    private bool crouching;
    private PlayerHealth playerHealth;
    private readonly List<RaycastResult> uiHits = new();
    private static int pointerSuppressedFrame = -1;

    public static void SuppressPointerForCurrentFrame() => pointerSuppressedFrame = Time.frameCount;

    private void Awake() => playerHealth = GetComponent<PlayerHealth>();

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            SetHorizontal(0f);
            SetCrouching(false);
            return;
        }

        bool wasdScheme = PlayerControlSchemeSettings.Current == PlayerControlScheme.WasdMouseQe;
        float nextHorizontal = wasdScheme
            ? (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f)
            : (keyboard.rightArrowKey.isPressed ? 1f : 0f) - (keyboard.leftArrowKey.isPressed ? 1f : 0f);
        SetHorizontal(nextHorizontal);

        bool nextCrouching = wasdScheme ? keyboard.sKey.isPressed : keyboard.downArrowKey.isPressed;
        SetCrouching(nextCrouching);

        if (keyboard.kKey.wasPressedThisFrame) playerHealth?.TakeDamage(1);

        bool jumpPressed = (wasdScheme ? keyboard.wKey : keyboard.upArrowKey).wasPressedThisFrame
            || keyboard.spaceKey.wasPressedThisFrame;
        if (jumpPressed) JumpPressed?.Invoke();

        if (wasdScheme)
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
            if (keyboard.eKey.wasPressedThisFrame) SkillTwoPressed?.Invoke();
        }
        else
        {
            if (keyboard.zKey.wasPressedThisFrame)
            {
                BasicAttackPressed?.Invoke();
                AttackZPressed?.Invoke();
            }
            if (keyboard.xKey.wasPressedThisFrame)
            {
                SecondaryAttackPressed?.Invoke();
                AttackXPressed?.Invoke();
            }
            if (keyboard.cKey.wasPressedThisFrame)
            {
                SkillOnePressed?.Invoke();
                AttackCPressed?.Invoke();
            }
            if (keyboard.vKey.wasPressedThisFrame)
            {
                SkillTwoPressed?.Invoke();
                AttackVPressed?.Invoke();
            }
        }

        if (keyboard.fKey.wasPressedThisFrame) InteractPressed?.Invoke();
    }

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
