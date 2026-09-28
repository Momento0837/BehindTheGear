using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Displays JSON dialogue using UI objects assigned in the Inspector.</summary>
public sealed class ConversationDialogue : MonoBehaviour
{
    [Serializable] private class DialogueFile { public DialogueLine[] lines = Array.Empty<DialogueLine>(); }
    [Serializable] private class DialogueLine
    {
        public string speaker = string.Empty;
        [TextArea] public string text = string.Empty;
        // Kept for a future portrait-address lookup. The portrait UI is safely hidden when null.
        public Sprite portrait = null;
    }

    [Header("Content")]
    [SerializeField] private TextAsset dialogueJson;
    [SerializeField, Min(.005f)] private float characterInterval = .03f;
    [SerializeField] private PlayerControlLock playerControlLock;

    [Header("Existing UI references")]
    [SerializeField] private CanvasGroup dialogueGroup;
    [SerializeField] private Image dialoguePanelImage;
    [SerializeField] private Image portraitImage;
    [SerializeField] private TMP_Text speakerText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private GameObject continueIndicator;
    [Tooltip("Inactive parent Canvas that contains the assigned dialogue UI objects.")]
    [SerializeField] private GameObject dialogueCanvasRoot;
    [Tooltip("Existing dialogue UI objects to enable only while dialogue is playing.")]
    [SerializeField] private GameObject[] dialogueUiObjects;

    public event Action Finished;
    public bool IsPlaying { get; private set; }

    private DialogueLine[] lines;
    private Coroutine routine;

    private void Awake()
    {
        ConfigureImage(dialoguePanelImage, new Color(.48f, .16f, .035f, 1f));
        ConfigureImage(portraitImage, Color.white);
        if (playerControlLock == null)
            playerControlLock = FindFirstObjectByType<PlayerControlLock>();
        SetVisible(false);
        LoadJson();
    }

    private static void ConfigureImage(Image image, Color color)
    {
        if (image == null) return;
        image.color = color;
    }

    public void Play()
    {
        if (IsPlaying) return;
        LoadJson();
        if (lines == null || lines.Length == 0)
        {
            Debug.LogWarning("[ConversationDialogue] No dialogue lines were found in the assigned JSON file.", this);
            Finished?.Invoke();
            return;
        }
        playerControlLock?.Acquire();
        routine = StartCoroutine(PlayRoutine());
    }

    private void LoadJson()
    {
        if (dialogueJson == null) return;
        DialogueFile file = JsonUtility.FromJson<DialogueFile>(dialogueJson.text);
        lines = file?.lines;
    }

    private IEnumerator PlayRoutine()
    {
        IsPlaying = true;
        SetVisible(true);

        foreach (DialogueLine line in lines)
        {
            if (speakerText != null) speakerText.text = line.speaker ?? string.Empty;
            if (portraitImage != null)
            {
                portraitImage.sprite = line.portrait;
                // When no sprite is assigned, retain the Image's inspector color/placeholder
                // so the portrait panel keeps the reference layout shown in the design.
                portraitImage.enabled = true;
            }
            if (bodyText != null) bodyText.text = string.Empty;
            if (continueIndicator != null) continueIndicator.SetActive(false);

            // The layout can omit a separate speaker label; in that case keep the name
            // in the body so the supplied demo canvas remains a complete dialogue UI.
            string speakerPrefix = speakerText == null && !string.IsNullOrWhiteSpace(line.speaker)
                ? $"{line.speaker}\n"
                : string.Empty;
            string fullText = speakerPrefix + (line.text ?? string.Empty);
            bool revealed = false;
            int character = 0;
            float characterTimer = 0f;
            while (character < fullText.Length)
            {
                // Check every frame. WaitForSecondsRealtime would miss a short click/key
                // press that happens between two character intervals.
                if (AdvancePressed())
                {
                    revealed = true;
                    break;
                }

                characterTimer += Time.unscaledDeltaTime;
                while (characterTimer >= characterInterval && character < fullText.Length)
                {
                    if (bodyText != null) bodyText.text += fullText[character];
                    character++;
                    characterTimer -= characterInterval;
                }
                yield return null;
            }
            if (bodyText != null) bodyText.text = fullText;
            if (continueIndicator != null) continueIndicator.SetActive(true);

            // The press that reveals text does not also advance to the following line.
            if (revealed) yield return null;
            while (!AdvancePressed()) yield return null;

            // Consume the advance input before the next line begins. Without this frame
            // boundary, the same key press can immediately reveal the following line.
            yield return null;
        }

        FinishDialogue();
    }

    private static bool AdvancePressed()
    {
        bool keyboardAdvance = Keyboard.current != null &&
            (Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame);
        return keyboardAdvance || Mouse.current?.leftButton.wasPressedThisFrame == true;
    }

    private void FinishDialogue()
    {
        SetVisible(false);
        IsPlaying = false;
        routine = null;
        playerControlLock?.Release();
        Finished?.Invoke();
    }

    private void OnDisable()
    {
        if (!IsPlaying) return;
        StopCoroutine(routine);
        FinishDialogue();
    }

    private void SetVisible(bool visible)
    {
        // Children cannot become visible while their Canvas parent is inactive.
        if (visible && dialogueCanvasRoot != null)
            dialogueCanvasRoot.SetActive(true);

        if (dialogueGroup != null)
        {
            dialogueGroup.alpha = visible ? 1f : 0f;
            dialogueGroup.interactable = visible;
            dialogueGroup.blocksRaycasts = visible;
        }
        foreach (GameObject uiObject in dialogueUiObjects)
            if (uiObject != null && uiObject.activeSelf != visible)
                uiObject.SetActive(visible);

        // Make the first visible dialogue frame render immediately after inactive UI is enabled.
        if (visible) Canvas.ForceUpdateCanvases();
        else if (dialogueCanvasRoot != null)
            dialogueCanvasRoot.SetActive(false);
    }
}
