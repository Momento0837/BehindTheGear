using System;
using System.Collections;
using System.Globalization;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public sealed class QuestDefinition
{
    public string questName;
    public string objective;
}

/// <summary>Session quest state shared by the story flow and standalone quest givers.</summary>
public static class QuestTracker
{
    public static string CurrentQuestName { get; private set; } = string.Empty;
    public static string CurrentObjective { get; private set; } = string.Empty;
    public static bool IsCompletionChange { get; private set; }
    public static event Action Changed;
    private static object owner;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        owner = null;
        CurrentQuestName = string.Empty;
        CurrentObjective = string.Empty;
        IsCompletionChange = false;
        Changed?.Invoke();
    }

    public static void Accept(object source, TextAsset questData)
    {
        SetDefinition(source, questData, false);
    }

    /// <summary>Completes the current objective and replaces it with its successor.</summary>
    public static void Advance(object source, TextAsset nextQuest)
    {
        if (!ReferenceEquals(owner, source)) return;
        SetDefinition(source, nextQuest, true);
    }

    private static void SetDefinition(object source, TextAsset questData, bool completed)
    {
        if (questData == null)
        {
            Debug.LogError("[Quest] Missing quest definition JSON.");
            return;
        }
        var definition = JsonUtility.FromJson<QuestDefinition>(questData.text);
        if (definition == null || string.IsNullOrWhiteSpace(definition.questName))
        {
            Debug.LogError("[Quest] Quest definition needs a questName: " + questData.name);
            return;
        }
        SetQuest(source, definition.questName, definition.objective, completed);
    }

    public static void Accept(object source, string questName, string objective)
    {
        SetQuest(source, questName, objective, false);
    }

    private static void SetQuest(object source, string questName, string objective, bool completed)
    {
        owner = source;
        CurrentQuestName = questName ?? string.Empty;
        CurrentObjective = objective ?? string.Empty;
        IsCompletionChange = completed;
        Changed?.Invoke();
    }

    public static void Complete(object source)
    {
        if (!ReferenceEquals(owner, source)) return;
        owner = null;
        CurrentQuestName = string.Empty;
        CurrentObjective = string.Empty;
        IsCompletionChange = true;
        Changed?.Invoke();
    }
}

/// <summary>Animates quest changes per glyph, keeping the authored font and final layout.</summary>
[DisallowMultipleComponent]
public sealed class InGameQuestHUD : MonoBehaviour
{
    [SerializeField] private GameObject questRoot;
    [SerializeField] private TMP_Text questText;

    [Header("Quest text transition")]
    [SerializeField, Min(.001f)] private float characterDelay = .075f;
    [SerializeField, Min(.01f)] private float disappearDuration = .38f;
    [SerializeField, Min(.01f)] private float appearDuration = .68f;
    [SerializeField, Min(.01f)] private float glowDuration = 1.1f;
    [SerializeField, Range(1f, 1.5f)] private float appearanceOvershoot = 1.2f;
    [SerializeField, Range(0f, 1f)] private float softLightOpacity = .3f;
    [SerializeField, Min(1f)] private float softLightSpread = 3.5f;
    [SerializeField, Range(0f, 1f)] private float backgroundDimOpacity = .5f;
    [SerializeField, Min(.01f)] private float backgroundDimFadeInDuration = .3f;

    private enum Transition { Idle, Disappearing, Appearing }
    private Transition transition;
    private float elapsed;
    private string requestedContent = string.Empty;
    private TMP_MeshInfo[] originalMesh;
    private readonly List<int> glyphOrder = new();
    private int[] glyphRanks = Array.Empty<int>();
    private RectTransform lightRoot;
    private Texture2D lightTexture;
    private Sprite lightSprite;
    private readonly List<Image> lights = new();
    private Image backgroundDim;
    private Canvas questForegroundCanvas;
    private bool createdQuestCanvas;
    private bool raisedQuestSorting;
    private bool originalOverrideSorting;
    private int originalSortingOrder;
    private int originalSortingLayer;
    private float dimElapsed;
    private float dimAppearanceFadeDelay;
    private bool openingPresentation;

    public bool CanPresentOpening => questText != null && questRoot != null;

    public IEnumerator PresentOpeningQuest(Action revealGameplay)
    {
        if (!CanPresentOpening) { revealGameplay(); yield break; }
        var overlay = new GameObject("Opening Quest Black Screen", typeof(Canvas), typeof(CanvasScaler));
        var overlayCanvas = overlay.GetComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.sortingOrder = 32500;
        var black = new GameObject("Black", typeof(RectTransform), typeof(Image));
        var blackRect = black.GetComponent<RectTransform>();
        blackRect.SetParent(overlay.transform, false);
        blackRect.anchorMin = Vector2.zero;
        blackRect.anchorMax = Vector2.one;
        blackRect.offsetMin = blackRect.offsetMax = Vector2.zero;
        var image = black.GetComponent<Image>();
        image.color = Color.clear;
        image.raycastTarget = true;
        RectTransform rect = questRoot.GetComponent<RectTransform>();
        Vector3 homePosition = rect.localPosition;
        Vector3 homeScale = rect.localScale;
        Canvas foreground = null;
        bool addedCanvas = false;
        bool savedOverride = false;
        int savedOrder = 0;
        int savedLayer = 0;
        openingPresentation = true;
        try
        {
            // Cover the held final video frame before restoring any gameplay UI.
            float time = 0f;
            while (time < .45f)
            {
                time += Time.unscaledDeltaTime;
                image.color = new Color(0, 0, 0, Mathf.SmoothStep(0, 1, Mathf.Clamp01(time / .45f)));
                yield return null;
            }
            image.color = Color.black;
            revealGameplay();
            // OnEnable can run when the cutscene restores the HUD; initialize after it.
            requestedContent = FormatObjective(QuestTracker.CurrentQuestName);
            foreground = questRoot.GetComponent<Canvas>();
            addedCanvas = foreground == null;
            if (addedCanvas)
            {
                foreground = questRoot.AddComponent<Canvas>();
                foreground.additionalShaderChannels = foreground.rootCanvas.additionalShaderChannels;
            }
            savedOverride = foreground.overrideSorting;
            savedOrder = foreground.sortingOrder;
            savedLayer = foreground.sortingLayerID;
            foreground.overrideSorting = true;
            foreground.sortingLayerID = overlayCanvas.sortingLayerID;
            foreground.sortingOrder = 32501;
            SetContent(requestedContent);
            Canvas.ForceUpdateCanvases();
            questText.ForceMeshUpdate();
            rect.localScale = homeScale * 2f;
            // Center the rendered glyph bounds, independent of the authored alignment and pivot.
            Canvas rootCanvas = foreground.rootCanvas;
            RectTransform canvasRect = rootCanvas.GetComponent<RectTransform>();
            Vector3 screenCenter = canvasRect.TransformPoint(canvasRect.rect.center);
            Vector3 glyphCenter = questText.transform.TransformPoint(questText.textBounds.center);
            rect.position += screenCenter - glyphCenter;
            Vector3 centerPosition = rect.localPosition;
            BeginAppearance();
            while (transition != Transition.Idle)
            {
                Tick(Time.unscaledDeltaTime);
                yield return null;
            }
            time = 0f;
            while (time < 1.5f)
            {
                time += Time.unscaledDeltaTime;
                float progress = Mathf.SmoothStep(0, 1, Mathf.Clamp01(time / 1.5f));
                rect.localPosition = Vector3.Lerp(centerPosition, homePosition, progress);
                rect.localScale = Vector3.Lerp(homeScale * 2f, homeScale, progress);
                image.color = new Color(0, 0, 0, 1f - progress);
                yield return null;
            }
        }
        finally
        {
            if (rect != null)
            {
                rect.localPosition = homePosition;
                rect.localScale = homeScale;
            }
            if (foreground != null)
            {
                foreground.overrideSorting = savedOverride;
                foreground.sortingOrder = savedOrder;
                foreground.sortingLayerID = savedLayer;
                if (addedCanvas) Destroy(foreground);
            }
            openingPresentation = false;
            transition = Transition.Idle;
            HideLight();
            if (questText != null) questText.ForceMeshUpdate();
            Destroy(overlay);
        }
    }

    private void OnEnable()
    {
        QuestTracker.Changed += Refresh;
        if (questText != null) questText.OnPreRenderText += HandleTextRebuilt;
        requestedContent = FormatObjective(QuestTracker.CurrentQuestName);
        ShowWithoutAnimation();
    }

    private void OnDisable()
    {
        QuestTracker.Changed -= Refresh;
        transition = Transition.Idle;
        if (questText != null)
        {
            questText.OnPreRenderText -= HandleTextRebuilt;
            questText.ForceMeshUpdate(true);
        }
        HideLight();
    }

    private void OnDestroy()
    {
        if (lightRoot != null) Destroy(lightRoot.gameObject);
        if (lightSprite != null) Destroy(lightSprite);
        if (lightTexture != null) Destroy(lightTexture);
        HideDim();
        if (backgroundDim != null) Destroy(backgroundDim.gameObject);
        if (createdQuestCanvas && questForegroundCanvas != null) Destroy(questForegroundCanvas);
    }

    public void Refresh()
    {
        requestedContent = FormatObjective(QuestTracker.CurrentQuestName);
        // Prefab inspection and edit-mode validation should still show the exact stored text.
        if (!Application.isPlaying || !isActiveAndEnabled || questText == null || !QuestTracker.IsCompletionChange)
        {
            ShowWithoutAnimation();
            return;
        }
        if (transition == Transition.Idle && questText.text != requestedContent)
            BeginTransition();
    }

    private void ShowWithoutAnimation()
    {
        transition = Transition.Idle;
        SetContent(requestedContent);
        if (questText != null) questText.ForceMeshUpdate(true);
        HideLight();
    }

    private void SetContent(string content)
    {
        if (questText != null)
        {
            questText.richText = false;
            questText.textWrappingMode = TextWrappingModes.NoWrap;
            questText.text = content;
        }
        if (questRoot != null) questRoot.SetActive(content.Length > 0);
    }

    private void BeginTransition()
    {
        elapsed = 0f;
        HideLight();
        dimElapsed = 0f;
        if (string.IsNullOrEmpty(questText.text)) BeginAppearance();
        else
        {
            transition = Transition.Disappearing;
            questText.ForceMeshUpdate();
            ShowDim();
            UpdateDim();
        }
    }

    private void BeginAppearance()
    {
        elapsed = 0f;
        transition = requestedContent.Length > 0 ? Transition.Appearing : Transition.Idle;
        SetContent(requestedContent);
        if (transition == Transition.Idle)
        {
            HideLight();
            return;
        }
        dimAppearanceFadeDelay = Mathf.Max(0f, backgroundDimFadeInDuration - dimElapsed);
        // Generate the whole right-aligned string before hiding/scaling individual glyphs.
        questText.ForceMeshUpdate();
        EnsureLight();
        UpdateLight();
        // Keep the completion fade-in running across the old/new text phase boundary.
        if (backgroundDim == null || !backgroundDim.gameObject.activeSelf) ShowDim();
        UpdateDim();
    }

    private void HideLight()
    {
        if (lightRoot != null) lightRoot.gameObject.SetActive(false);
        HideDim();
    }

    private void ShowDim()
    {
        if (openingPresentation) return;
        Canvas parentCanvas = questText.GetComponentInParent<Canvas>();
        if (parentCanvas == null || questRoot == null) return;
        Canvas rootCanvas = parentCanvas.rootCanvas;
        if (backgroundDim == null)
        {
            var root = new GameObject("Quest Background Dim (Runtime)", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasGroup), typeof(CanvasRenderer), typeof(Image));
            root.hideFlags = HideFlags.DontSave;
            root.layer = rootCanvas.gameObject.layer;
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(rootCanvas.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            backgroundDim = root.GetComponent<Image>();
            backgroundDim.raycastTarget = false;
            var group = root.GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
        }
        if (questForegroundCanvas == null)
        {
            questForegroundCanvas = questRoot.GetComponent<Canvas>();
            createdQuestCanvas = questForegroundCanvas == null;
            if (createdQuestCanvas)
            {
                questForegroundCanvas = questRoot.AddComponent<Canvas>();
                questForegroundCanvas.additionalShaderChannels = rootCanvas.additionalShaderChannels;
            }
        }
        if (!raisedQuestSorting)
        {
            originalOverrideSorting = questForegroundCanvas.overrideSorting;
            originalSortingOrder = questForegroundCanvas.sortingOrder;
            originalSortingLayer = questForegroundCanvas.sortingLayerID;
            raisedQuestSorting = true;
        }
        int order = rootCanvas.sortingOrder;
        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            if (canvas.isRootCanvas && canvas.sortingLayerID == rootCanvas.sortingLayerID)
                order = Mathf.Max(order, canvas.sortingOrder);
        order = Mathf.Min(order, 32765);
        var dimCanvas = backgroundDim.GetComponent<Canvas>();
        dimCanvas.overrideSorting = true;
        dimCanvas.sortingLayerID = rootCanvas.sortingLayerID;
        dimCanvas.sortingOrder = order + 1;
        // Lift the authored quest group without reparenting or changing its layout.
        questForegroundCanvas.overrideSorting = true;
        questForegroundCanvas.sortingLayerID = rootCanvas.sortingLayerID;
        questForegroundCanvas.sortingOrder = order + 2;
        backgroundDim.gameObject.SetActive(true);
    }

    private void UpdateDim()
    {
        if (openingPresentation) return;
        if (backgroundDim == null || !backgroundDim.gameObject.activeSelf) return;
        float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(dimElapsed / backgroundDimFadeInDuration));
        float fadeOut = 0f;
        if (transition == Transition.Appearing || requestedContent.Length == 0)
        {
            float duration = Mathf.Max(0, glyphOrder.Count - 1) * characterDelay +
                (transition == Transition.Appearing ? appearDuration : disappearDuration);
            float delay = transition == Transition.Appearing ? dimAppearanceFadeDelay : backgroundDimFadeInDuration;
            float progress = Mathf.Clamp01((elapsed - delay) / Mathf.Max(.01f, duration - delay));
            fadeOut = Mathf.SmoothStep(0f, 1f, progress);
            if (progress >= 1f) { HideDim(); return; }
        }
        backgroundDim.color = new Color(0f, 0f, 0f, backgroundDimOpacity * fadeIn * (1f - fadeOut));
    }

    private void HideDim()
    {
        if (backgroundDim != null) backgroundDim.gameObject.SetActive(false);
        if (!raisedQuestSorting) return;
        if (questForegroundCanvas != null)
        {
            questForegroundCanvas.overrideSorting = originalOverrideSorting;
            questForegroundCanvas.sortingOrder = originalSortingOrder;
            questForegroundCanvas.sortingLayerID = originalSortingLayer;
        }
        raisedQuestSorting = false;
    }

    private void EnsureLight()
    {
        if (lightRoot == null)
        {
            // A diffuse light spot, independent of the font atlas and glyph outlines.
            const int size = 128;
            lightTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Quest Soft Light (Runtime)", hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[size * size];
            float edge = Mathf.Exp(-5f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Vector2 point = new Vector2(x, y) / (size - 1f) * 2f - Vector2.one;
                    float alpha = Mathf.Clamp01((Mathf.Exp(-5f * point.sqrMagnitude) - edge) / (1f - edge));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            lightTexture.SetPixels32(pixels);
            lightTexture.Apply(false, true);
            lightSprite = Sprite.Create(lightTexture, new Rect(0, 0, size, size), Vector2.one * .5f, 100f, 0, SpriteMeshType.FullRect);
            lightSprite.hideFlags = HideFlags.DontSave;
            var root = new GameObject("Quest Soft Light (Runtime)", typeof(RectTransform));
            root.hideFlags = HideFlags.DontSave;
            root.layer = questText.gameObject.layer;
            lightRoot = root.GetComponent<RectTransform>();
            lightRoot.SetParent(questText.transform.parent, false);
            lightRoot.SetSiblingIndex(questText.transform.GetSiblingIndex());
        }
        while (lights.Count < glyphOrder.Count)
        {
            var spot = new GameObject("Soft Light", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            spot.layer = questText.gameObject.layer;
            var image = spot.GetComponent<Image>();
            image.rectTransform.SetParent(lightRoot, false);
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = questText.rectTransform.pivot;
            image.sprite = lightSprite;
            image.raycastTarget = false;
            lights.Add(image);
        }
        lightRoot.gameObject.SetActive(true);
    }

    private void UpdateLight()
    {
        if (lightRoot == null || originalMesh == null) return;
        RectTransform sourceRect = questText.rectTransform;
        lightRoot.anchorMin = sourceRect.anchorMin;
        lightRoot.anchorMax = sourceRect.anchorMax;
        lightRoot.pivot = sourceRect.pivot;
        lightRoot.sizeDelta = sourceRect.sizeDelta;
        lightRoot.anchoredPosition3D = sourceRect.anchoredPosition3D;
        lightRoot.localRotation = sourceRect.localRotation;
        lightRoot.localScale = sourceRect.localScale;
        for (int rank = 0; rank < lights.Count; rank++)
        {
            Image light = lights[rank];
            bool active = transition == Transition.Appearing && rank < glyphOrder.Count;
            light.gameObject.SetActive(active);
            if (!active) continue;
            var character = questText.textInfo.characterInfo[glyphOrder[rank]];
            Vector3[] vertices = originalMesh[character.materialReferenceIndex].vertices;
            int vertex = character.vertexIndex;
            Vector3 center = (vertices[vertex] + vertices[vertex + 2]) * .5f;
            Vector2 glyphSize = vertices[vertex + 2] - vertices[vertex];
            float localTime = elapsed - rank * characterDelay;
            float fadeStart = appearDuration * .55f;
            float intensity = Mathf.SmoothStep(0f, 1f, localTime / .12f) *
                (1f - Mathf.SmoothStep(0f, 1f, (localTime - fadeStart) / Mathf.Max(.01f, glowDuration - fadeStart)));
            float spread = softLightSpread * Mathf.Lerp(.8f, 1.15f, Mathf.Clamp01(localTime / glowDuration));
            light.rectTransform.anchorMin = light.rectTransform.anchorMax = sourceRect.pivot;
            light.rectTransform.anchoredPosition3D = center;
            light.rectTransform.sizeDelta = new Vector2(Mathf.Max(Mathf.Abs(glyphSize.x), Mathf.Abs(glyphSize.y)) * spread,
                Mathf.Abs(glyphSize.y) * spread);
            light.color = new Color(1f, 1f, 1f, softLightOpacity * intensity);
        }
    }

    private void HandleTextRebuilt(TMP_TextInfo info)
    {
        originalMesh = info.CopyMeshInfoVertexData();
        glyphOrder.Clear();
        if (glyphRanks.Length != info.characterCount) glyphRanks = new int[info.characterCount];
        for (int i = 0; i < info.characterCount; i++)
        {
            glyphRanks[i] = -1;
            if (info.characterInfo[i].isVisible) glyphOrder.Add(i);
        }
        // Use final visual positions, not the text box pivot or the string's reveal length.
        glyphOrder.Sort((left, right) =>
        {
            int order = info.characterInfo[left].bottomLeft.x.CompareTo(info.characterInfo[right].bottomLeft.x);
            return order != 0 ? order : left.CompareTo(right);
        });
        for (int rank = 0; rank < glyphOrder.Count; rank++) glyphRanks[glyphOrder[rank]] = rank;
        ApplyGlyphs(info, originalMesh);
        if (transition == Transition.Appearing)
        {
            UpdateLight();
            UpdateDim();
        }
    }

    private void LateUpdate()
    {
        if (!openingPresentation) Tick(Time.unscaledDeltaTime);
    }

    private void Tick(float deltaTime)
    {
        if (transition == Transition.Idle || questText == null) return;
        elapsed += Mathf.Max(0f, deltaTime);
        dimElapsed += Mathf.Max(0f, deltaTime);
        float tail = transition == Transition.Disappearing ? disappearDuration : Mathf.Max(appearDuration, glowDuration);
        float total = Mathf.Max(0, glyphOrder.Count - 1) * characterDelay + tail;
        if (elapsed >= total)
        {
            if (transition == Transition.Disappearing) BeginAppearance();
            else
            {
                transition = Transition.Idle;
                HideLight();
                questText.ForceMeshUpdate();
                if (questText.text != requestedContent) BeginTransition();
            }
        }
        if (transition == Transition.Idle) return;
        ApplyGlyphs(questText.textInfo, originalMesh);
        questText.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices | TMP_VertexDataUpdateFlags.Colors32);
        if (transition == Transition.Appearing)
        {
            UpdateLight();
        }
        UpdateDim();
    }

    private void ApplyGlyphs(TMP_TextInfo info, TMP_MeshInfo[] source)
    {
        if (source == null || transition == Transition.Idle) return;
        for (int i = 0; i < info.characterCount && i < glyphRanks.Length; i++)
        {
            TMP_CharacterInfo character = info.characterInfo[i];
            if (!character.isVisible || glyphRanks[i] < 0) continue;
            int material = character.materialReferenceIndex;
            int vertex = character.vertexIndex;
            if (material >= source.Length || vertex + 3 >= source[material].vertices.Length) continue;
            float localTime = elapsed - glyphRanks[i] * characterDelay;
            float progress = Mathf.Clamp01(localTime / (transition == Transition.Disappearing ? disappearDuration : appearDuration));
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            float scale;
            Color color = source[material].colors32[vertex];
            if (transition == Transition.Disappearing)
            {
                scale = 1f - eased;
                color = Color.Lerp(color, new Color(0f, 0f, 0f, 0f), eased);
            }
            else
            {
                scale = progress < .55f
                    ? appearanceOvershoot * Mathf.SmoothStep(0f, 1f, progress / .55f)
                    : Mathf.Lerp(appearanceOvershoot, 1f, Mathf.SmoothStep(0f, 1f, (progress - .55f) / .45f));
                color = Color.Lerp(Color.white, color, Mathf.SmoothStep(0f, 1f, (progress - .7f) / .3f));
                color.a *= Mathf.SmoothStep(0f, 1f, progress / .35f);
            }
            Vector3 center = (source[material].vertices[vertex] + source[material].vertices[vertex + 2]) * .5f;
            for (int corner = 0; corner < 4; corner++)
            {
                info.meshInfo[material].vertices[vertex + corner] = center + (source[material].vertices[vertex + corner] - center) * scale;
                info.meshInfo[material].colors32[vertex + corner] = color;
            }
        }
    }

    public static string FormatObjective(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return string.Empty;
        content = content.Replace('\r', ' ').Replace('\n', ' ').Trim();
        int[] characters = StringInfo.ParseCombiningCharacters(content);
        return characters.Length > 16 ? content.Substring(0, characters[16]) + "..." : content;
    }
}
