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
    [SerializeField, Min(.005f)] private float characterInterval = .05f;
    [SerializeField] private PlayerControlLock playerControlLock;

    [Header("Existing UI references")]
    [SerializeField] private CanvasGroup dialogueGroup;
    [SerializeField] private Image dialoguePanelImage;
    [SerializeField] private Image portraitImage;
    [Tooltip("Separate UI text for the speaker name.")]
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
    private RectTransform choiceRoot;
    private Action<bool> choiceCallback;

    private void Awake()
    {
        ConfigureImage(dialoguePanelImage, new Color(.48f, .16f, .035f, 1f));
        ConfigureImage(portraitImage, Color.white);
        if (playerControlLock == null)
            playerControlLock = FindFirstObjectByType<PlayerControlLock>();
        SetVisible(false);
        LoadJson();
    }

    private void Update()
    {
        if (!IsPlaying || choiceRoot == null || Mouse.current?.leftButton.wasPressedThisFrame != true)
            return;

        Vector2 pointerPosition = Mouse.current.position.ReadValue();
        Button[] buttons = choiceRoot.GetComponentsInChildren<Button>(true);
        foreach (Button button in buttons)
        {
            Canvas canvas = button.GetComponentInParent<Canvas>();
            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            if (!RectTransformUtility.RectangleContainsScreenPoint(
                    button.transform as RectTransform,
                    pointerPosition,
                    eventCamera))
                continue;

            button.onClick.Invoke();
            return;
        }
    }

    private static void ConfigureImage(Image image, Color color)
    {
        if (image == null) return;
        image.color = color;
    }

    public void Play()
    {
        Play(null);
    }

    public void Play(TextAsset dialogueOverride)
    {
        if (IsPlaying) return;
        LoadJson(dialogueOverride != null ? dialogueOverride : dialogueJson);
        if (lines == null || lines.Length == 0)
        {
            Debug.LogWarning("[ConversationDialogue] No dialogue lines were found in the assigned JSON file.", this);
            Finished?.Invoke();
            return;
        }
        playerControlLock?.Acquire();
        routine = StartCoroutine(PlayRoutine());
    }

    /// <summary>Shows a dialogue prompt with two mouse-selectable choices.</summary>
    public void ShowChoices(string speaker, string prompt, string firstChoice, string secondChoice, Action<bool> onChoice)
    {
        if (IsPlaying || onChoice == null || dialogueCanvasRoot == null || bodyText == null)
        {
            Debug.LogError("[ConversationDialogue] Choice UI needs an idle dialogue UI, a canvas root, and body text.", this);
            return;
        }

        IsPlaying = true;
        choiceCallback = onChoice;
        playerControlLock?.Acquire();
        if (speakerText != null) speakerText.text = speaker ?? string.Empty;
        bodyText.text = string.Empty;
        if (continueIndicator != null) continueIndicator.SetActive(false);
        SetVisible(true);
        routine = StartCoroutine(ShowChoicesRoutine(speaker, prompt, firstChoice, secondChoice));
    }

    private IEnumerator ShowChoicesRoutine(string speaker, string prompt, string firstChoice, string secondChoice)
    {
        string fullText = prompt ?? string.Empty;
        yield return RevealText(fullText);
        routine = null;

        // Keep the choices hidden until the NPC has finished speaking.
        if (IsPlaying)
            CreateChoiceButtons(firstChoice, secondChoice);
    }

    private void CreateChoiceButtons(string firstChoice, string secondChoice)
    {
        GameObject panel = new GameObject("Quest Choices", typeof(RectTransform));
        choiceRoot = panel.GetComponent<RectTransform>();
        choiceRoot.SetParent(dialogueCanvasRoot.transform, false);
        choiceRoot.anchorMin = new Vector2(.5f, 0f);
        choiceRoot.anchorMax = new Vector2(.5f, 0f);
        choiceRoot.pivot = new Vector2(.5f, .5f);
        choiceRoot.anchoredPosition = new Vector2(140f, 75f);
        choiceRoot.sizeDelta = new Vector2(500f, 70f);

        CreateChoiceButton(choiceRoot, "Accept", firstChoice, new Vector2(-125f, 0f), true);
        CreateChoiceButton(choiceRoot, "Reject", secondChoice, new Vector2(125f, 0f), false);
    }

    private void CreateChoiceButton(RectTransform parent, string objectName, string label, Vector2 position, bool accepted)
    {
        GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(220f, 58f);

        Image background = buttonObject.GetComponent<Image>();
        background.color = new Color(.35f, .22f, .14f, 1f);
        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = background;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() => SelectChoice(accepted));

        GameObject textObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(rect, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8f, 4f);
        textRect.offsetMax = new Vector2(-8f, -4f);

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = bodyText.font;
        text.fontSize = 28f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.text = label ?? string.Empty;
    }

    private void SelectChoice(bool accepted)
    {
        Action<bool> callback = choiceCallback;
        choiceCallback = null;
        if (choiceRoot != null)
        {
            Destroy(choiceRoot.gameObject);
            choiceRoot = null;
        }

        SetVisible(false);
        IsPlaying = false;
        playerControlLock?.Release();
        callback?.Invoke(accepted);
    }

    private void LoadJson()
    {
        LoadJson(dialogueJson);
    }

    private void LoadJson(TextAsset source)
    {
        if (source == null)
        {
            lines = null;
            return;
        }
        DialogueFile file = JsonUtility.FromJson<DialogueFile>(source.text);
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

            string fullText = line.text ?? string.Empty;
            yield return RevealText(fullText);
            if (continueIndicator != null) continueIndicator.SetActive(true);

            while (!AdvancePressed()) yield return null;

            // Consume the advance input before the next line begins. Without this frame
            // boundary, the same key press can immediately reveal the following line.
            yield return null;
        }

        FinishDialogue();
    }

    private IEnumerator RevealText(string fullText)
    {
        int character = 0;
        float characterTimer = 0f;
        while (character < fullText.Length)
        {
            // Check every frame so a short press can reveal the rest of the line immediately.
            if (AdvancePressed())
                break;

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
        // A reveal press should not also advance the next dialogue line.
        if (character < fullText.Length)
            yield return null;
    }

    private static bool AdvancePressed()
    {
        bool keyboardAdvance = Keyboard.current != null &&
            (Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame);
        return keyboardAdvance || Mouse.current?.leftButton.wasPressedThisFrame == true;
    }

    private void FinishDialogue()
    {
        if (choiceRoot != null)
        {
            Destroy(choiceRoot.gameObject);
            choiceRoot = null;
        }
        choiceCallback = null;
        SetVisible(false);
        IsPlaying = false;
        routine = null;
        playerControlLock?.Release();
        Finished?.Invoke();
    }

    private void OnDisable()
    {
        if (!IsPlaying) return;
        if (routine != null) StopCoroutine(routine);
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
