using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Displays JSON dialogue using UI objects assigned in the Inspector.</summary>
[DefaultExecutionOrder(-100)]
public sealed class ConversationDialogue : MonoBehaviour
{
    [Serializable] private class DialogueFile { public DialogueLine[] lines = Array.Empty<DialogueLine>(); }
    [Serializable] private class DialogueLine
    {
        public string speaker = string.Empty;
        public string 표정 = "기본 표정";
        public string 연출 = string.Empty;
        public bool npcLaugh;
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
    [SerializeField] private Image portraitImage;
    private DialoguePortraitLibrary portraitLibrary;
    private Image animatedPortrait;
    private SpriteRenderer portraitSource;
    [Tooltip("Separate UI text for the speaker name.")]
    [SerializeField] private TMP_Text speakerText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private GameObject continueIndicator;
    [Tooltip("Inactive parent Canvas that contains the assigned dialogue UI objects.")]
    [SerializeField] private GameObject dialogueCanvasRoot;
    [Tooltip("Existing dialogue UI objects to enable only while dialogue is playing.")]
    [SerializeField] private GameObject[] dialogueUiObjects;

    private DialogueCanvasView canvasView;
    private GameObject ownedCanvas;

    private void BindCanvas(DialogueCanvasView view)
    {
        canvasView = view;
        dialogueCanvasRoot = view.gameObject;
        dialogueGroup = view.group;
        portraitImage = view.portrait;
        speakerText = view.speaker;
        bodyText = view.body;
        continueIndicator = view.continueIndicator;
        dialogueUiObjects = view.visibleObjects ?? Array.Empty<GameObject>();
        TalkingImage = view.talking;
        if (view.choiceTemplate != null) view.choiceTemplate.gameObject.SetActive(false);
    }

    private void UseSharedCanvas()
    {
        if (canvasView != null) return;
        var prefabs = Resources.Load<DialogueCanvasPrefabs>("Dialogue/CanvasPrefabs");
        if (prefabs == null || prefabs.storyCanvas == null) return;
        var view = prefabs.storyCanvas.GetComponent<DialogueCanvasView>();
        if (view == null || view.body == null || view.speaker == null) return;
        var previousRoot = dialogueCanvasRoot;
        ownedCanvas = Instantiate(prefabs.storyCanvas, previousRoot != null ? previousRoot.transform.parent : null);
        BindCanvas(ownedCanvas.GetComponent<DialogueCanvasView>());
        if (previousRoot != null) previousRoot.SetActive(false);
    }

    public void BindPlayer(PlayerControlLock value) => playerControlLock = value;
    public ConversationDialogue CreateTutorialPresentation(Transform parent)
    {
        var host = new GameObject("Base Tutorial Dialogue"); host.SetActive(false); host.transform.SetParent(parent, false);
        var prefabs = Resources.Load<DialogueCanvasPrefabs>("Dialogue/CanvasPrefabs");
        var source = prefabs != null && prefabs.storyCanvas != null ? prefabs.storyCanvas : dialogueCanvasRoot;
        var ui = Instantiate(source, host.transform);
        var copy = host.AddComponent<ConversationDialogue>();
        copy.characterInterval = characterInterval;
        var view = ui.GetComponent<DialogueCanvasView>();
        if (view != null)
            copy.BindCanvas(view);
        else
        {
            T Map<T>(T original) where T : Component
            {
                if (original == null) return null;
                int index = Array.IndexOf(dialogueCanvasRoot.GetComponentsInChildren<T>(true), original);
                return index < 0 ? null : ui.GetComponentsInChildren<T>(true)[index];
            }
            copy.dialogueCanvasRoot = ui; copy.dialogueGroup = Map(dialogueGroup); copy.portraitImage = Map(portraitImage);
            copy.bodyText = Map(bodyText); copy.speakerText = Map(speakerText); copy.continueIndicator = null;
            copy.dialogueUiObjects = new GameObject[dialogueUiObjects.Length];
            for (int i = 0; i < dialogueUiObjects.Length; i++) copy.dialogueUiObjects[i] = dialogueUiObjects[i] != null ? Map(dialogueUiObjects[i].transform)?.gameObject : null;
        }
        copy.ConfigureCanvasSorting();
        host.SetActive(true); copy.BindPlayer(null);
        return copy;
    }

    public event Action Finished;
    public Func<string, IEnumerator> BeforeLineEvent { get; set; }
    public event Action LineRevealStarted;
    public event Action<char> CharacterRevealed;
    public event Action LineRevealCompleted;
    public event Action<bool> ChoiceSelected;
    public bool IsPlaying { get; private set; }
    public string CurrentSpeaker { get; private set; } = string.Empty;
    public bool CurrentLineNpcLaugh { get; private set; }
    public TMP_Text BodyText => bodyText;
    public Image TalkingImage { get; private set; }
    public float CharacterInterval => characterInterval;

    private DialogueLine[] lines;
    private Coroutine routine;
    private RectTransform choiceRoot;
    private Action<bool> choiceCallback;
    private string finalFirstChoice;
    private string finalSecondChoice;
    private RectTransform nextTriangle;
    private Vector2 nextTrianglePosition;
    private int selectedChoice;
    public bool IsChoosing => choiceRoot != null;
    private bool advanceRequested;
    private int openedFrame;
    private PlayerCameraFollow2D dialogueCamera;
    private TMP_FontAsset mysterySpeakerFont;
    private TMP_FontAsset ethanSpeakerFont;
    private TMP_FontAsset selenaSpeakerFont;
    private TMP_FontAsset originalBodyFont;
    private TMP_FontAsset originalSpeakerFont;
    private Material originalBodyMaterial;
    private Material originalSpeakerMaterial;
    private Material selenaBodyMaterial, selenaSpeakerMaterial;
    private int startingLine;
    private RectTransform acceptRect, acceptFill;
    private float acceptHeld;
    private int holdSource;
    private void UpdateHoldChoice()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        if (keyboard?.escapeKey.wasPressedThisFrame == true) { SelectChoice(false); return; }
        if (holdSource == 0 && keyboard?.fKey.isPressed == true) { holdSource = 1; acceptHeld = 0; }
        bool overAccept = false;
        if (acceptRect != null && mouse != null)
        {
            var canvas = acceptRect.GetComponentInParent<Canvas>();
            overAccept = RectTransformUtility.RectangleContainsScreenPoint(acceptRect, mouse.position.ReadValue(),
                canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera);
        }
        if (mouse?.leftButton.wasPressedThisFrame == true && overAccept) { SelectChoice(true); return; }
        bool held = holdSource == 1 ? keyboard?.fKey.isPressed == true : holdSource == 2 && overAccept && mouse?.leftButton.isPressed == true;
        if (!held) { acceptHeld = 0; holdSource = 0; }
        else acceptHeld += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(acceptHeld / .5f);
        if (acceptFill != null) acceptFill.anchorMax = new Vector2(progress, 1);
        if (progress >= 1) SelectChoice(true);
    }

    public static string GetOpeningText(TextAsset source)
    {
        var file = source != null ? JsonUtility.FromJson<DialogueFile>(source.text) : null;
        return file?.lines != null && file.lines.Length > 0 ? file.lines[0].text : string.Empty;
    }

    public static string[] GetGreetingLines(TextAsset source, int count)
    {
        var file = source != null ? JsonUtility.FromJson<DialogueFile>(source.text) : null;
        if (file?.lines == null) return Array.Empty<string>();
        var result = new string[Mathf.Min(count, file.lines.Length)];
        for (int i = 0; i < result.Length; i++) result[i] = file.lines[i].text ?? string.Empty;
        return result;
    }

    private void SetSpeaker(string speaker)
    {
        CurrentSpeaker = speaker ?? string.Empty;
        string name = CurrentSpeaker.Trim();
        TMP_FontAsset font = null;
        if (name == "???")
        {
            if (mysterySpeakerFont == null) mysterySpeakerFont = Resources.Load<TMP_FontAsset>("Dialogue/Fonts/SuhgungDialogue");
            font = mysterySpeakerFont;
        }
        else if (string.Equals(name, "Ethan", StringComparison.OrdinalIgnoreCase))
        {
            if (ethanSpeakerFont == null) ethanSpeakerFont = Resources.Load<TMP_FontAsset>("Dialogue/Fonts/TangbaDialogue");
            font = ethanSpeakerFont;
        }
        bool isSelena = string.Equals(name, "Selena", StringComparison.OrdinalIgnoreCase) || name == "셀레나";
        // Same authored Tangba face, with padded glyphs and a build-referenced transparent TMP material.
        if (isSelena && selenaSpeakerFont == null)
            selenaSpeakerFont = Resources.Load<TMP_FontAsset>("Dialogue/Fonts/SelenaDialogue");
        var bodyFont = isSelena && selenaSpeakerFont != null ? selenaSpeakerFont : font;
        ApplyFont(bodyText, bodyFont != null ? bodyFont : originalBodyFont, bodyFont != null ? bodyFont.material : originalBodyMaterial);
        ApplyFont(speakerText, font != null ? font : originalSpeakerFont, font != null ? font.material : originalSpeakerMaterial);
        if (isSelena)
        {
            ApplyCleanSelenaMaterial(bodyText, ref selenaBodyMaterial);
            ApplyCleanSelenaMaterial(speakerText, ref selenaSpeakerMaterial);
        }
        SetPortrait(name, null);
    }

    private static void ApplyCleanSelenaMaterial(TMP_Text label, ref Material clean)
    {
        if (label == null || label.font == null) return;
        if (clean == null)
        {
            clean = new Material(label.font.material);
            clean.DisableKeyword("UNDERLAY_ON"); clean.DisableKeyword("UNDERLAY_INNER"); clean.DisableKeyword("GLOW_ON");
            if (clean.HasProperty("_UnderlayColor")) clean.SetColor("_UnderlayColor", Color.clear);
        }
        // Dynamic atlas contents can change between speakers; never retain a stale texture binding.
        clean.CopyPropertiesFromMaterial(label.font.material);
        clean.DisableKeyword("UNDERLAY_ON"); clean.DisableKeyword("UNDERLAY_INNER"); clean.DisableKeyword("GLOW_ON");
        if (clean.HasProperty("_UnderlayColor")) clean.SetColor("_UnderlayColor", Color.clear);
        clean.mainTexture = label.font.atlasTexture;
        label.fontSharedMaterial = clean;
    }
    private void OnDestroy()
    {
        if (ownedCanvas != null) Destroy(ownedCanvas);
        if (selenaBodyMaterial != null) Destroy(selenaBodyMaterial);
        if (selenaSpeakerMaterial != null) Destroy(selenaSpeakerMaterial);
    }

    private void SetPortrait(string speaker, Sprite overrideSprite, string expression = null)
    {
        if (portraitImage == null) return;
        if (portraitLibrary == null) portraitLibrary = Resources.Load<DialoguePortraitLibrary>("Dialogue/Portraits");
        if (animatedPortrait == null)
        {
            if (portraitImage.GetComponent<RectMask2D>() == null) portraitImage.gameObject.AddComponent<RectMask2D>();
            var child = new GameObject("Animated NPC Portrait", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            child.transform.SetParent(portraitImage.transform, false);
            animatedPortrait = child.GetComponent<Image>();
            animatedPortrait.raycastTarget = false;
            var rect = animatedPortrait.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            portraitSource = GetComponent<SpriteRenderer>();
        }
        bool mystery = speaker.Trim() == "???" && overrideSprite == null;
        animatedPortrait.gameObject.SetActive(mystery);
        portraitImage.sprite = overrideSprite != null ? overrideSprite
            : portraitLibrary != null ? portraitLibrary.Resolve(speaker, expression) : null;
        portraitImage.preserveAspect = true;
        portraitImage.enabled = portraitImage.sprite != null;
        DialoguePortraitOutline.Apply(portraitImage, mystery ? animatedPortrait : portraitImage);
        UpdatePortraitFrame();
    }

    private void LateUpdate()
    {
        UpdatePortraitFrame();
        if (nextTriangle != null && nextTriangle.gameObject.activeSelf)
            nextTriangle.anchoredPosition = nextTrianglePosition + Vector2.up * (Mathf.Sin(Time.unscaledTime * (canvasView != null ? canvasView.continueBobSpeed : 3f)) * (canvasView != null ? canvasView.continueBobHeight : 5f));
    }

    private void UpdatePortraitFrame()
    {
        if (animatedPortrait == null || !animatedPortrait.gameObject.activeInHierarchy || portraitSource == null) return;
        Sprite sprite = portraitSource.sprite;
        animatedPortrait.sprite = sprite;
        animatedPortrait.enabled = sprite != null;
        if (sprite == null) return;
        // The existing placeholder clips a top-aligned, enlarged upper-body view.
        float frameHeight = portraitImage.rectTransform.rect.height;
        float height = frameHeight * 1.32f;
        animatedPortrait.rectTransform.anchoredPosition = new Vector2(0f, frameHeight * .08f);
        animatedPortrait.rectTransform.sizeDelta = new Vector2(height * sprite.rect.width / sprite.rect.height, height);
    }

    private static void ApplyFont(TMP_Text text, TMP_FontAsset font, Material material)
    {
        if (text == null || font == null) return;
        text.font = font;
        // A material from the previous speaker can point at an entirely different atlas.
        text.fontSharedMaterial = material != null ? material : font.material;
        text.fontStyle &= ~FontStyles.Bold;
        text.fontWeight = FontWeight.Regular;
    }

    private static bool IsNpcSpeaker(string speaker) =>
        !string.IsNullOrWhiteSpace(speaker) &&
        !string.Equals(speaker.Trim(), "Ethan", StringComparison.OrdinalIgnoreCase) &&
        speaker.Trim() != "안내";

    private static bool IsMysterySpeaker(string speaker) => speaker?.Trim() == "???"
        || string.Equals(speaker?.Trim(), "BlackMarker", StringComparison.OrdinalIgnoreCase);

    private void BeginCameraFocus(bool hasNpcSpeaker, bool isMysteryConversation)
    {
        if (!hasNpcSpeaker) return;
        Camera mainCamera = Camera.main;
        dialogueCamera = mainCamera != null ? mainCamera.GetComponent<PlayerCameraFollow2D>() : null;
        if (dialogueCamera != null)
            dialogueCamera.BeginDialogueFocus(this, playerControlLock != null ? playerControlLock.transform : null, isMysteryConversation);
    }

    private void EndCameraFocus()
    {
        if (dialogueCamera != null) dialogueCamera.EndDialogueFocus(this);
        dialogueCamera = null;
    }

    private void Awake()
    {
        UseSharedCanvas();
        if (dialogueCanvasRoot != null)
        {
            if (TalkingImage == null) TalkingImage = Array.Find(dialogueCanvasRoot.GetComponentsInChildren<Image>(true), image => image.name == "Talking");
            if (TalkingImage != null) TalkingImage.gameObject.SetActive(false);
        }
        if (bodyText != null) { originalBodyFont = bodyText.font; originalBodyMaterial = bodyText.fontSharedMaterial; }
        if (speakerText != null) { originalSpeakerFont = speakerText.font; originalSpeakerMaterial = speakerText.fontSharedMaterial; }
        if (playerControlLock == null)
            playerControlLock = FindFirstObjectByType<PlayerControlLock>();
        CreateContinueIndicator();
        SetVisible(false);
        LoadJson();
    }

    private void Update()
    {
        if (!IsPlaying) return;

        if (choiceRoot != null) { UpdateHoldChoice(); return; }

        var keyboard = Keyboard.current;
        if (choiceRoot != null && keyboard != null)
        {
            if (keyboard.aKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame) selectedChoice = 0;
            if (keyboard.dKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame) selectedChoice = 1;
            if (keyboard.tabKey.wasPressedThisFrame) selectedChoice = 1 - selectedChoice;
            HighlightChoice();
            if (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            { SelectChoice(selectedChoice == 0); return; }
        }
        else if (keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame
            || (keyboard.fKey.wasPressedThisFrame && Time.frameCount > openedFrame)))
            advanceRequested = true;

        if (Mouse.current?.leftButton.wasPressedThisFrame != true) return;

        Vector2 pointerPosition = Mouse.current.position.ReadValue();
        Button[] buttons = choiceRoot != null
            ? choiceRoot.GetComponentsInChildren<Button>(true)
            : Array.Empty<Button>();
        foreach (Button button in buttons)
        {
            if (button == null || !button.isActiveAndEnabled || !button.interactable) continue;
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

        // Preserve click-to-advance for dialogue lines. Choice prompts require an explicit choice.
        if (choiceRoot == null) advanceRequested = true;
    }

    private void CreateContinueIndicator()
    {
        if (dialogueCanvasRoot == null || bodyText == null) return;
        if (canvasView != null && canvasView.continueIndicator != null)
        {
            continueIndicator = canvasView.continueIndicator;
            nextTriangle = continueIndicator.GetComponent<RectTransform>();
            if (canvasView.continueGlowShader != null)
            {
                var glow = continueIndicator.GetComponent<MenuImageSoftEffect>() ?? continueIndicator.AddComponent<MenuImageSoftEffect>();
                glow.Configure(canvasView.continueGlowShader, 0f, canvasView.continueGlowRadius, canvasView.continueGlowOpacity);
            }
            nextTrianglePosition = nextTriangle.anchoredPosition;
            continueIndicator.SetActive(false);
            return;
        }
        if (continueIndicator != null) continueIndicator.SetActive(false);
        var style = Resources.Load<DialogueVisualStyle>("Dialogue/VisualStyle");
        var marker = new GameObject("Dialogue Next Triangle", typeof(RectTransform), typeof(Image));
        nextTriangle = marker.GetComponent<RectTransform>();
        nextTriangle.SetParent(dialogueCanvasRoot.transform, false);
        nextTriangle.anchorMin = nextTriangle.anchorMax = new Vector2(.5f, 0f);
        nextTriangle.pivot = new Vector2(.5f, .5f);
        nextTriangle.anchoredPosition = new Vector2(520f, 75f);
        nextTrianglePosition = nextTriangle.anchoredPosition;
        nextTriangle.sizeDelta = new Vector2(28f, 28f);
        var image = marker.GetComponent<Image>();
        image.sprite = style != null ? style.triangle : null;
        image.preserveAspect = true;
        image.raycastTarget = false;
        if (style != null && style.glowShader != null)
            marker.AddComponent<MenuImageSoftEffect>().Configure(style.glowShader, 0f, 15f, .3f);
        continueIndicator = marker;
        marker.SetActive(false);
    }

    private void HighlightChoice()
    {
        var buttons = choiceRoot.GetComponentsInChildren<Button>();
        for (int i = 0; i < buttons.Length; i++)
            buttons[i].GetComponent<Image>().color = i == selectedChoice
                ? new Color(.65f, .45f, .25f, 1f) : new Color(.35f, .22f, .14f, 1f);
    }

    private void RequestAdvance()
    {
        if (IsPlaying && choiceRoot == null)
            advanceRequested = true;
    }

    public void Play()
    {
        Play(null);
    }

    public void Play(TextAsset dialogueOverride)
    {
        PlayDialogue(dialogueOverride, null, null, null);
    }

    public void PlayFromLine(TextAsset content, int startLine) =>
        PlayDialogue(content, null, null, null, startLine);

    /// <summary>Shows choices on the final line without closing or revealing that line again.</summary>
    public void PlayWithChoices(TextAsset dialogueOverride, string firstChoice, string secondChoice, Action<bool> onChoice, int startLine = 0)
    {
        if (onChoice == null || dialogueCanvasRoot == null || bodyText == null)
        {
            Debug.LogError("[ConversationDialogue] Final-line choices need a callback, canvas root, and body text.", this);
            return;
        }
        PlayDialogue(dialogueOverride, firstChoice, secondChoice, onChoice, startLine);
    }

    private void PlayDialogue(TextAsset dialogueOverride, string firstChoice, string secondChoice, Action<bool> onChoice, int startLine = 0)
    {
        if (IsPlaying) return;
        advanceRequested = false;
        LoadJson(dialogueOverride != null ? dialogueOverride : dialogueJson);
        if (lines == null || lines.Length == 0)
        {
            Debug.LogWarning("[ConversationDialogue] No dialogue lines were found in the assigned JSON file.", this);
            Finished?.Invoke();
            return;
        }
        playerControlLock?.Acquire();
        startingLine = Mathf.Clamp(startLine, 0, lines.Length - 1);
        choiceCallback = onChoice;
        finalFirstChoice = firstChoice;
        finalSecondChoice = secondChoice;
        BeginCameraFocus(Array.Exists(lines, line => IsNpcSpeaker(line.speaker)),
            Array.Exists(lines, line => IsMysterySpeaker(line.speaker)));
        routine = StartCoroutine(PlayRoutine());
    }

    /// <summary>Shows a dialogue prompt with two mouse-selectable choices.</summary>
    public void ShowChoicesForCurrentLine(string firstChoice, string secondChoice, Action<bool> onChoice)
    {
        ShowChoices(CurrentSpeaker,
            bodyText != null ? bodyText.text : string.Empty, firstChoice, secondChoice, onChoice);
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
        openedFrame = Time.frameCount;
        DialoguePresentation.Set(this, true);
        choiceCallback = onChoice;
        advanceRequested = false;
        playerControlLock?.Acquire();
        SetSpeaker(speaker);
        BeginCameraFocus(IsNpcSpeaker(speaker), IsMysterySpeaker(speaker));
        if (speakerText != null) speakerText.text = speaker ?? string.Empty;
        bodyText.text = string.Empty;
        if (continueIndicator != null) continueIndicator.SetActive(false);

        SetVisible(true);
        routine = StartCoroutine(ShowChoicesRoutine(speaker, prompt, firstChoice, secondChoice));
    }

    private IEnumerator ShowChoicesRoutine(string speaker, string prompt, string firstChoice, string secondChoice)
    {
        CurrentLineNpcLaugh = false;
        string fullText = prompt ?? string.Empty;
        yield return RevealText(fullText);
        advanceRequested = false;
        routine = null;

        // Keep the choices hidden until the NPC has finished speaking.
        if (IsPlaying)
            CreateChoiceButtons(firstChoice, secondChoice);
    }

    private void CreateChoiceButtons(string firstChoice, string secondChoice)
    {
        if (canvasView != null && canvasView.choiceTemplate != null)
        {
            var choices = Instantiate(canvasView.choiceTemplate, dialogueCanvasRoot.transform);
            choiceRoot = (RectTransform)choices.transform;
            acceptRect = (RectTransform)choices.accept.transform;
            acceptFill = choices.holdProgress;
            acceptHeld = 0; holdSource = 0; selectedChoice = 0;
            choices.acceptLabel.text = firstChoice ?? string.Empty;
            choices.rejectLabel.text = secondChoice ?? string.Empty;
            choices.accept.onClick.AddListener(() => SelectChoice(true));
            choices.reject.onClick.AddListener(() => SelectChoice(false));
            if (acceptFill != null) acceptFill.anchorMax = new Vector2(0, 1);
            choices.gameObject.SetActive(true);
            return;
        }
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
        selectedChoice = 0;
        HighlightChoice();
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
        if (!accepted) button.onClick.AddListener(() => SelectChoice(false));
        else
        {
            acceptRect = rect; acceptHeld = 0; holdSource = 0;
            var fill = new GameObject("Accept Hold Progress", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            fill.transform.SetParent(rect, false); fill.color = new Color(1f, .8f, .45f, .4f); fill.raycastTarget = false;
            acceptFill = fill.rectTransform; acceptFill.anchorMin = Vector2.zero; acceptFill.anchorMax = new Vector2(0, 1);
            acceptFill.offsetMin = acceptFill.offsetMax = Vector2.zero;
        }

        GameObject textObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(rect, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8f, 4f);
        textRect.offsetMax = new Vector2(-8f, -4f);

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        UIFontTheme.Apply(text, UIFontTheme.Bold);
        text.fontSize = 28f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.text = label ?? string.Empty;
    }

    private void SelectChoice(bool accepted)
    {
        if (!IsPlaying || choiceRoot == null || choiceCallback == null) return;
        Action<bool> callback = choiceCallback;
        choiceCallback = null;
        finalFirstChoice = finalSecondChoice = null;
        if (choiceRoot != null)
        {
            choiceRoot.gameObject.SetActive(false);
            Destroy(choiceRoot.gameObject);
            choiceRoot = null;
        }

        SetVisible(false);
        IsPlaying = false;
        DialoguePresentation.Set(this, false);
        advanceRequested = false;
        playerControlLock?.Release();
        EndCameraFocus();
        ChoiceSelected?.Invoke(accepted);
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
        openedFrame = Time.frameCount;
        DialoguePresentation.Set(this, true);
        SetVisible(true);

        for (int i = startingLine; i < lines.Length; i++)
        {
            DialogueLine line = lines[i];
            if (!string.IsNullOrWhiteSpace(line.연출) && BeforeLineEvent != null)
            {
                SetVisible(false);
                yield return BeforeLineEvent(line.연출);
                SetVisible(true);
            }
            CurrentLineNpcLaugh = line.npcLaugh && line.speaker?.Trim() == "???";
            SetSpeaker(line.speaker);

            if (speakerText != null) speakerText.text = line.speaker ?? string.Empty;
            SetPortrait(line.speaker ?? string.Empty, line.portrait, line.표정);
            if (bodyText != null) bodyText.text = string.Empty;
            if (continueIndicator != null) continueIndicator.SetActive(false);

            string fullText = line.text ?? string.Empty;
            yield return RevealText(fullText);
            if (i == lines.Length - 1 && choiceCallback != null)
            {
                advanceRequested = false;
                routine = null;
                CreateChoiceButtons(finalFirstChoice, finalSecondChoice);
                yield break;
            }
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
        LineRevealStarted?.Invoke();
        while (character < fullText.Length)
        {
            // Check every frame so a short press can reveal the rest of the line immediately.
            if (AdvancePressed())
                break;

            characterTimer += Time.unscaledDeltaTime;
            while (characterTimer >= characterInterval && character < fullText.Length)
            {
                int spaceEnd = character;
                while (spaceEnd < fullText.Length && fullText[spaceEnd] == ' ') spaceEnd++;
                if (spaceEnd - character >= 5)
                {
                    // Preserve authored spacing, but spend no typing time or sound on it.
                    if (bodyText != null) bodyText.text += fullText.Substring(character, spaceEnd - character);
                    character = spaceEnd;
                    continue;
                }
                char revealedCharacter = fullText[character];
                if (bodyText != null) bodyText.text += revealedCharacter;
                CharacterRevealed?.Invoke(revealedCharacter);
                character++;
                characterTimer -= characterInterval;
            }
            yield return null;
        }

        if (bodyText != null) bodyText.text = fullText;
        LineRevealCompleted?.Invoke();
        // A reveal press should not also advance the next dialogue line.
        if (character < fullText.Length)
            yield return null;
    }

    private bool AdvancePressed()
    {
        bool pressed = advanceRequested;
        advanceRequested = false;

        return pressed;
    }

    private void FinishDialogue()
    {
        CurrentLineNpcLaugh = false;
        if (choiceRoot != null)
        {
            Destroy(choiceRoot.gameObject);
            choiceRoot = null;
        }
        choiceCallback = null;
        finalFirstChoice = finalSecondChoice = null;
        advanceRequested = false;

        SetVisible(false);
        IsPlaying = false;
        DialoguePresentation.Set(this, false);
        routine = null;
        playerControlLock?.Release();
        EndCameraFocus();
        Finished?.Invoke();
    }

    private void OnDisable()
    {
        if (!IsPlaying) return;
        if (routine != null) StopCoroutine(routine);
        FinishDialogue();
    }

    private void ConfigureCanvasSorting()
    {
        if (dialogueCanvasRoot == null) return;
        // Shared story prefabs also need the established dialogue layer: above the HUD,
        // inventory and base UI, but below scene fades (32767).
        foreach (var canvas in dialogueCanvasRoot.GetComponentsInChildren<Canvas>(true))
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingLayerID = 0;
            canvas.sortingOrder = 32050;
        }
    }

    private void SetVisible(bool visible)
    {
        // Children cannot become visible while their Canvas parent is inactive.
        if (visible && dialogueCanvasRoot != null)
        {
            ConfigureCanvasSorting();
            dialogueCanvasRoot.SetActive(true);
        }

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
        else
        {

            if (dialogueCanvasRoot != null)
                dialogueCanvasRoot.SetActive(false);
        }
    }
}
