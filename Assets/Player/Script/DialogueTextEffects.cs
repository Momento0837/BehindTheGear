using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Applies a per-character shake and falling dust to one conversation's typewriter text.</summary>
[RequireComponent(typeof(ConversationDialogue))]
public sealed class DialogueTextEffects : MonoBehaviour
{
    [Header("Character shake")]
    [SerializeField, Min(0f)] private float shakeAmplitude = 1.5f;
    [SerializeField, Min(0.1f)] private float shakeFrequency = 28f;
    [SerializeField, Min(0f)] private float settleWaveDuration = 0.7f;
    [SerializeField, Min(0.01f)] private float settleDuration = 0.4f;

    [Header("Falling dust")]
    [SerializeField, Range(1, 5)] private int dustPerCharacter = 3;
    [SerializeField] private Color dustColor = new Color(0.38f, 0.29f, 0.22f, 0.9f);
    [Tooltip("Smallest dust particle size in UI pixels.")]
    [SerializeField, Range(1f, 20f)] private float dustMinSize = 5f;
    [Tooltip("Largest dust particle size in UI pixels.")]
    [SerializeField, Range(1f, 20f)] private float dustMaxSize = 8f;
    [Tooltip("Shortest time a dust particle stays visible, in seconds.")]
    [SerializeField, Range(0.1f, 2f)] private float dustMinLifetime = 0.48f;
    [Tooltip("Longest time a dust particle stays visible, in seconds.")]
    [SerializeField, Range(0.1f, 2f)] private float dustMaxLifetime = 0.72f;
    [SerializeField, Min(0.01f)] private float dustGravity = 110f;
    private const int MaxDustMotes = 96;

    private sealed class DustMote
    {
        public RectTransform rect;
        public Image image;
        public Vector2 position;
        public Vector2 velocity;
        public float age;
        public float lifetime;
        public float rotationSpeed;
    }

    private ConversationDialogue conversation;
    private TMP_Text bodyText;
    private RectTransform dustRoot;
    private RectTransform dustParent;
    private Sprite dustSprite;
    private Texture2D dustTexture;
    private readonly List<DustMote> dustMotes = new List<DustMote>();
    private Vector3[][] originalVertices;
    private string cachedText;
    private float lineCompletedAt = -1f;
    private int settlingCharacterCount;
    private bool isEffectActive;

    private void OnValidate()
    {
        dustMaxSize = Mathf.Max(dustMinSize, dustMaxSize);
        dustMaxLifetime = Mathf.Max(dustMinLifetime, dustMaxLifetime);
    }

    private void Awake()
    {
        conversation = GetComponent<ConversationDialogue>();
        bodyText = conversation != null ? conversation.BodyText : null;
    }

    private void OnEnable()
    {
        if (conversation == null) conversation = GetComponent<ConversationDialogue>();
        if (bodyText == null && conversation != null) bodyText = conversation.BodyText;
        if (conversation == null) return;

        conversation.LineRevealStarted += HandleLineRevealStarted;
        conversation.CharacterRevealed += HandleCharacterRevealed;
        conversation.LineRevealCompleted += HandleLineRevealCompleted;
        conversation.Finished += HandleDialogueFinished;
    }

    private void OnDisable()
    {
        isEffectActive = false;
        lineCompletedAt = -1f;
        if (conversation != null)
        {
            conversation.LineRevealStarted -= HandleLineRevealStarted;
            conversation.CharacterRevealed -= HandleCharacterRevealed;
            conversation.LineRevealCompleted -= HandleLineRevealCompleted;
            conversation.Finished -= HandleDialogueFinished;
        }
        ClearDust();
    }

    private void OnDestroy()
    {
        if (dustSprite != null) Destroy(dustSprite);
        if (dustTexture != null) Destroy(dustTexture);
    }

    private void Update()
    {
        if (bodyText != null)
        {
            if (isEffectActive)
                UpdateCharacterShake();
        }
        UpdateDust(Time.unscaledDeltaTime);
    }

    private void HandleLineRevealStarted()
    {
        isEffectActive = true;
        lineCompletedAt = -1f;
        settlingCharacterCount = 0;
        ClearDust();
    }

    private void HandleCharacterRevealed(char character)
    {
        if (!char.IsWhiteSpace(character))
            EmitDustForLastCharacter();
    }

    private void HandleLineRevealCompleted()
    {
        if (bodyText == null) return;

        bodyText.ForceMeshUpdate();
        cachedText = null;
        settlingCharacterCount = bodyText.textInfo.characterCount;
        lineCompletedAt = Time.unscaledTime;
    }

    private void HandleDialogueFinished()
    {
        isEffectActive = false;
        lineCompletedAt = -1f;
        ClearDust();
    }

    private void UpdateCharacterShake()
    {
        string currentText = bodyText.text;
        if (cachedText != currentText || originalVertices == null)
            CaptureOriginalVertices();

        TMP_TextInfo textInfo = bodyText.textInfo;
        if (textInfo == null || originalVertices == null) return;

        for (int meshIndex = 0; meshIndex < originalVertices.Length && meshIndex < textInfo.meshInfo.Length; meshIndex++)
        {
            Vector3[] source = originalVertices[meshIndex];
            Vector3[] target = textInfo.meshInfo[meshIndex].vertices;
            if (source == null || target == null) continue;
            int copyLength = Mathf.Min(source.Length, target.Length);
            System.Array.Copy(source, target, copyLength);
        }

        float time = Time.unscaledTime;
        int characterCount = textInfo.characterCount;
        for (int i = 0; i < characterCount; i++)
        {
            TMP_CharacterInfo characterInfo = textInfo.characterInfo[i];
            if (!characterInfo.isVisible) continue;

            int materialIndex = characterInfo.materialReferenceIndex;
            int vertexIndex = characterInfo.vertexIndex;
            if (materialIndex < 0 || materialIndex >= originalVertices.Length) continue;
            Vector3[] baseVertices = originalVertices[materialIndex];
            Vector3[] meshVertices = textInfo.meshInfo[materialIndex].vertices;
            if (baseVertices == null || vertexIndex + 3 >= baseVertices.Length || vertexIndex + 3 >= meshVertices.Length) continue;

            float amplitude = shakeAmplitude;
            if (lineCompletedAt >= 0f)
            {
                float characterProgress = settlingCharacterCount <= 1
                    ? 0f
                    : (float)i / (settlingCharacterCount - 1);
                float settleStart = lineCompletedAt + settleWaveDuration * characterProgress;
                float settleProgress = Mathf.Clamp01((time - settleStart) / settleDuration);
                amplitude *= 1f - settleProgress;
            }

            if (amplitude <= 0f) continue;
            float phase = i * 2.399963f;
            Vector3 offset = new Vector3(
                Mathf.Sin(time * shakeFrequency + phase) * amplitude,
                Mathf.Cos(time * shakeFrequency * 0.87f + phase * 1.31f) * amplitude,
                0f);

            for (int corner = 0; corner < 4; corner++)
                meshVertices[vertexIndex + corner] = baseVertices[vertexIndex + corner] + offset;
        }

        bodyText.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
    }

    private void CaptureOriginalVertices()
    {
        bodyText.ForceMeshUpdate();
        TMP_TextInfo textInfo = bodyText.textInfo;
        originalVertices = new Vector3[textInfo.meshInfo.Length][];
        for (int i = 0; i < textInfo.meshInfo.Length; i++)
        {
            Vector3[] vertices = textInfo.meshInfo[i].vertices;
            originalVertices[i] = vertices != null ? (Vector3[])vertices.Clone() : null;
        }
        cachedText = bodyText.text;
    }

    private void EmitDustForLastCharacter()
    {
        if (bodyText == null) return;

        bodyText.ForceMeshUpdate();
        TMP_TextInfo textInfo = bodyText.textInfo;
        int characterIndex = textInfo.characterCount - 1;
        if (characterIndex < 0 || !textInfo.characterInfo[characterIndex].isVisible) return;
        if (!EnsureDustRoot()) return;

        TMP_CharacterInfo characterInfo = textInfo.characterInfo[characterIndex];
        Vector3 glyphBottomLeft = characterInfo.bottomLeft;
        float glyphWidth = characterInfo.bottomRight.x - glyphBottomLeft.x;
        for (int i = 0; i < dustPerCharacter; i++)
        {
            Vector3 localPoint = glyphBottomLeft + new Vector3(
                Random.Range(0f, Mathf.Max(1f, glyphWidth)), Random.Range(-2f, 2f), 0f);
            Vector3 worldPoint = bodyText.transform.TransformPoint(localPoint);
            Vector3 parentPoint = dustRoot.InverseTransformPoint(worldPoint);
            CreateDustMote(parentPoint);
        }
    }

    private bool EnsureDustRoot()
    {
        RectTransform textParent = bodyText.transform.parent as RectTransform;
        if (textParent == null) return false;

        if (dustRoot != null && dustParent == textParent)
        {
            if (!dustRoot.gameObject.activeSelf) dustRoot.gameObject.SetActive(true);
            return true;
        }

        if (dustRoot != null) Destroy(dustRoot.gameObject);
        dustParent = textParent;
        GameObject root = new GameObject("Dialogue Dust Effects", typeof(RectTransform));
        dustRoot = root.GetComponent<RectTransform>();
        dustRoot.SetParent(dustParent, false);
        dustRoot.anchorMin = Vector2.zero;
        dustRoot.anchorMax = Vector2.one;
        dustRoot.offsetMin = Vector2.zero;
        dustRoot.offsetMax = Vector2.zero;
        dustRoot.SetAsLastSibling();
        return true;
    }

    private void CreateDustMote(Vector3 localPosition)
    {
        EnsureDustSprite();
        if (dustMotes.Count >= MaxDustMotes)
        {
            if (dustMotes[0].rect != null) Destroy(dustMotes[0].rect.gameObject);
            dustMotes.RemoveAt(0);
        }
        GameObject moteObject = new GameObject("Dialogue Dust Mote", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = moteObject.GetComponent<RectTransform>();
        rect.SetParent(dustRoot, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = localPosition;
        float size = Random.Range(dustMinSize, dustMaxSize);
        rect.sizeDelta = new Vector2(size, size);

        Image image = moteObject.GetComponent<Image>();
        image.sprite = dustSprite;
        image.color = dustColor;
        image.raycastTarget = false;

        dustMotes.Add(new DustMote
        {
            rect = rect,
            image = image,
            position = localPosition,
            velocity = new Vector2(Random.Range(-10f, 10f), Random.Range(-18f, -8f)),
            lifetime = Random.Range(dustMinLifetime, dustMaxLifetime),
            rotationSpeed = Random.Range(-80f, 80f)
        });
    }

    private void UpdateDust(float deltaTime)
    {
        for (int i = dustMotes.Count - 1; i >= 0; i--)
        {
            DustMote mote = dustMotes[i];
            mote.age += deltaTime;
            if (mote.age >= mote.lifetime || mote.rect == null || mote.image == null)
            {
                if (mote.rect != null) Destroy(mote.rect.gameObject);
                dustMotes.RemoveAt(i);
                continue;
            }

            mote.velocity.y -= dustGravity * deltaTime;
            mote.position += mote.velocity * deltaTime;
            mote.rect.anchoredPosition = mote.position;
            mote.rect.Rotate(0f, 0f, mote.rotationSpeed * deltaTime);
            Color color = dustColor;
            color.a *= 1f - Mathf.Clamp01(mote.age / mote.lifetime);
            mote.image.color = color;
        }

        if (dustMotes.Count == 0 && dustRoot != null && dustRoot.gameObject.activeSelf)
            dustRoot.gameObject.SetActive(false);
    }

    private void EnsureDustSprite()
    {
        if (dustSprite != null) return;

        const int textureSize = 8;
        dustTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        Color[] pixels = new Color[textureSize * textureSize];
        Vector2 center = new Vector2((textureSize - 1) * 0.5f, (textureSize - 1) * 0.5f);
        float radius = textureSize * 0.5f;
        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center) / radius;
                float alpha = 1f - Mathf.SmoothStep(0.35f, 1f, distance);
                pixels[y * textureSize + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        dustTexture.SetPixels(pixels);
        dustTexture.Apply();
        dustSprite = Sprite.Create(dustTexture, new Rect(0f, 0f, textureSize, textureSize), new Vector2(0.5f, 0.5f), 100f);
    }

    private void ClearDust()
    {
        for (int i = 0; i < dustMotes.Count; i++)
            if (dustMotes[i].rect != null)
                Destroy(dustMotes[i].rect.gameObject);
        dustMotes.Clear();
        if (dustRoot != null) dustRoot.gameObject.SetActive(false);
    }
}
