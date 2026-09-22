using UnityEngine;

/// <summary>Assign this to the selection Canvas and connect its public methods to the two Inspector button events.</summary>
public sealed class PlayerControlSelector : MonoBehaviour
{
    [SerializeField] private PlayerController2D player;
    [SerializeField] private GameObject selectionPanel;

    private void Update()
    {
        // Keyboard selection keeps the start menu usable without relying on a legacy input module.
        if (player == null || player.enabled && !selectionPanel.activeSelf) return;
        if (UnityEngine.InputSystem.Keyboard.current?.digit1Key.wasPressedThisFrame == true) ChooseWASD();
        if (UnityEngine.InputSystem.Keyboard.current?.digit2Key.wasPressedThisFrame == true) ChooseArrowKeys();
    }

    public void ChooseWASD() => Choose(PlayerController2D.ControlScheme.WASD);
    public void ChooseArrowKeys() => Choose(PlayerController2D.ControlScheme.ArrowKeys);

    public void Configure(PlayerController2D targetPlayer, GameObject targetPanel)
    {
        player = targetPlayer;
        selectionPanel = targetPanel;
    }

    private void Choose(PlayerController2D.ControlScheme scheme)
    {
        if (player == null) return;
        player.SetControlScheme(scheme);
        if (selectionPanel != null) selectionPanel.SetActive(false);
    }
}
