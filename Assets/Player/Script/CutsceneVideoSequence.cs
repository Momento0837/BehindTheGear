using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Video;

/// <summary>
/// Plays an assigned VideoPlayer from an existing CutScen object's interaction event.
/// Hold Space for the configured duration to skip. No scene objects are created in code.
/// </summary>
public sealed class CutsceneVideoSequence : MonoBehaviour
{
    [Header("Existing scene references")]
    [SerializeField] private VideoPlayer videoPlayer;
    [SerializeField] private GameObject[] uiRootsToHide;
    [SerializeField] private PlayerControlLock playerControlLock;
    [SerializeField] private ConversationDialogue dialogueAfterVideo;

    [Header("Timing")]
    [SerializeField, Min(.1f)] private float skipHoldSeconds = 3f;

    private bool playing;
    private bool ending;
    private float heldSeconds;
    private bool[] uiWasActive;

    private void Awake()
    {
        if (playerControlLock == null)
            playerControlLock = FindFirstObjectByType<PlayerControlLock>();
        if (videoPlayer != null)
        {
            videoPlayer.renderMode = VideoRenderMode.CameraNearPlane;
            videoPlayer.aspectRatio = VideoAspectRatio.FitInside;
            videoPlayer.loopPointReached += OnVideoFinished;
        }
    }

    private void OnDestroy()
    {
        if (videoPlayer != null) videoPlayer.loopPointReached -= OnVideoFinished;
        RestoreUiAfterVideo();
    }

    // Assign this method to Interactable2D > On Interact on the existing CutScen object.
    public void PlayFromInteraction()
    {
        if (playing || videoPlayer == null) return;
        if (videoPlayer.clip == null && string.IsNullOrWhiteSpace(videoPlayer.url))
        {
            Debug.LogWarning("[CutsceneVideoSequence] Assign a VideoClip or URL to the VideoPlayer before playing.", this);
            return;
        }
        videoPlayer.enabled = true;
        StartCoroutine(PlayRoutine());
    }

    private IEnumerator PlayRoutine()
    {
        playing = true;
        ending = false;
        heldSeconds = 0f;
        playerControlLock?.Acquire();
        HideUiForVideo();

        videoPlayer.Prepare();
        float prepareTimeout = 10f;
        while (!videoPlayer.isPrepared && prepareTimeout > 0f)
        {
            prepareTimeout -= Time.unscaledDeltaTime;
            yield return null;
        }
        if (!videoPlayer.isPrepared)
        {
            Debug.LogError("[CutsceneVideoSequence] Video preparation timed out.", this);
            playing = false;
            RestoreUiAfterVideo();
            ReleasePlayer();
            yield break;
        }
        if (ending) yield break;
        videoPlayer.Play();

        while (playing && !ending)
        {
            bool spaceHeld = Keyboard.current?.spaceKey.isPressed == true;
            heldSeconds = spaceHeld ? heldSeconds + Time.unscaledDeltaTime : 0f;
            if (heldSeconds >= skipHoldSeconds) StartCoroutine(EndVideoRoutine());
            yield return null;
        }
    }

    private void OnVideoFinished(VideoPlayer _) { if (!ending) StartCoroutine(EndVideoRoutine()); }

    private IEnumerator EndVideoRoutine()
    {
        if (ending) yield break;
        ending = true;
        videoPlayer.Stop();
        videoPlayer.enabled = false;
        RestoreUiAfterVideo();

        playing = false;
        ending = false;
        heldSeconds = 0f;
        if (dialogueAfterVideo != null)
        {
            dialogueAfterVideo.Play();
            // Dialogue owns its own lock while it is visible. Release the cutscene lock now.
            ReleasePlayer();
        }
        else ReleasePlayer();
    }

    private void ReleasePlayer()
    {
        playerControlLock?.Release();
    }

    private void HideUiForVideo()
    {
        if (uiRootsToHide == null) return;
        uiWasActive = new bool[uiRootsToHide.Length];
        for (int i = 0; i < uiRootsToHide.Length; i++)
        {
            GameObject uiRoot = uiRootsToHide[i];
            if (uiRoot == null) continue;
            uiWasActive[i] = uiRoot.activeSelf;
            uiRoot.SetActive(false);
        }
    }

    private void RestoreUiAfterVideo()
    {
        if (uiRootsToHide == null || uiWasActive == null) return;
        for (int i = 0; i < uiRootsToHide.Length; i++)
        {
            if (uiRootsToHide[i] != null && uiWasActive[i]) uiRootsToHide[i].SetActive(true);
        }
        uiWasActive = null;
    }

}
