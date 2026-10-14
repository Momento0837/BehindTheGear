using UnityEngine;

/// <summary>
/// Disables the explicitly assigned player-control components while a cinematic or dialogue is open.
/// Assign existing components in the Inspector; this class never creates scene objects.
/// </summary>
public sealed class PlayerControlLock : MonoBehaviour
{
    [SerializeField] private Behaviour[] controlsToDisable;
    [SerializeField] private Rigidbody2D playerBody;

    private int lockCount;
    private PlayerMovement2D playerMovement;

    private void Awake()
    {
        playerMovement = playerBody != null
            ? playerBody.GetComponent<PlayerMovement2D>()
            : GetComponent<PlayerMovement2D>();
    }

    public void Acquire()
    {
        lockCount++;
        if (lockCount != 1) return;

        if (playerBody != null)
            playerBody.linearVelocity = Vector2.zero;

        foreach (Behaviour control in controlsToDisable)
            if (control != null) control.enabled = false;

        playerMovement?.RefreshAnimationWhileControlLocked();
    }

    private void FixedUpdate()
    {
        if (lockCount > 0)
            playerMovement?.RefreshAnimationWhileControlLocked();
    }

    public void Release()
    {
        if (lockCount == 0 || --lockCount != 0) return;
        foreach (Behaviour control in controlsToDisable)
            if (control != null) control.enabled = true;
    }

    private void OnDisable()
    {
        lockCount = 0;
        foreach (Behaviour control in controlsToDisable)
            if (control != null) control.enabled = true;
    }
}
