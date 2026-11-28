using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Plays menu feedback when input begins, independently of the button's action.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(Button)), DefaultExecutionOrder(-1000)]
public sealed class MenuButtonSound : MonoBehaviour, IPointerDownHandler, ISubmitHandler
{
    private Button button;
    private int eligibleFrame = -1;
    private int playedFrame = -1;

    public static void Attach(Button target)
    {
        if (target != null && target.GetComponent<MenuButtonSound>() == null)
            target.gameObject.AddComponent<MenuButtonSound>();
    }

    private void Awake() => button = GetComponent<Button>();

    private void OnEnable() => eligibleFrame = CanReceiveInput ? Time.frameCount : -1;

    private bool CanReceiveInput => isActiveAndEnabled && button != null
        && button.IsActive() && button.IsInteractable();

    private void Update()
    {
        // Button.OnSubmit may hide/disable its own UI before this handler is dispatched.
        // Preserve eligibility from before EventSystem processing for that same frame.
        eligibleFrame = CanReceiveInput ? Time.frameCount : -1;
    }

    public void OnPointerDown(PointerEventData data)
    {
        if (data.button == PointerEventData.InputButton.Left && CanReceiveInput) PlayOnce();
    }

    public void OnSubmit(BaseEventData data)
    {
        if (CanReceiveInput || eligibleFrame == Time.frameCount) PlayOnce();
    }

    private void PlayOnce()
    {
        if (playedFrame == Time.frameCount) return;
        playedFrame = Time.frameCount;
        GameSoundService.PlayMenuButton();
    }
}
