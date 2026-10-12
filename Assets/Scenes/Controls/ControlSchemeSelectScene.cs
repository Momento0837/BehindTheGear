using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using TMPro;
using UnityEngine.UI;

/// <summary>Builds the control selection screen and starts the game with the chosen layout.</summary>
public sealed class ControlSchemeSelectScene : MonoBehaviour
{
    [SerializeField] private string gameSceneName = "PlayerMovementDemo";
    [SerializeField, Min(.01f)] private float fadeDuration = .8f;
    [SerializeField] private TMP_FontAsset uiFont;

    private void Awake() => BuildSelectionScreen();

    private void BuildSelectionScreen()
    {
        EnsureEventSystem();

        GameObject canvasObject = new("Control Selection Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = .5f;

        Image background = CreateImage("Background", canvas.transform, new Color(.035f, .055f, .09f, 1f), Vector2.zero, new Vector2(1280f, 720f));
        Stretch(background.rectTransform);
        background.raycastTarget = false;

        CreateText("Title", canvas.transform, "조작 방식 선택", 48, FontStyles.Bold, Color.white,
            new Vector2(0f, 222f), new Vector2(900f, 72f), uiFont);
        CreateText("Subtitle", canvas.transform, "인게임에서 사용할 조작 방식을 하나 선택하세요", 22, FontStyles.Normal,
            new Color(.72f, .8f, .89f), new Vector2(0f, 166f), new Vector2(900f, 42f), uiFont);

        Button wasdButton = CreateChoiceButton(canvas.transform, "WASD + 마우스", "이동   A / D\n점프   W 또는 Space\n공격   마우스 좌클릭\n보조   마우스 우클릭\n스킬   Q / E", uiFont,
            new Vector2(-245f, -10f), new Color(.12f, .31f, .48f), () => Select(PlayerControlScheme.WasdMouseQe));
        CreateChoiceButton(canvas.transform, "방향키 + ZXCV", "이동   ← / →\n점프   ↑ 또는 Space\n공격   Z\n보조   X\n스킬   C / V", uiFont,
            new Vector2(245f, -10f), new Color(.19f, .3f, .42f), () => Select(PlayerControlScheme.ArrowKeys));

        CreateText("Hint", canvas.transform, "선택한 조작 방식만 인게임에 적용됩니다", 18, FontStyles.Normal,
            new Color(.56f, .66f, .76f), new Vector2(0f, -246f), new Vector2(900f, 38f), uiFont);

        EventSystem.current?.SetSelectedGameObject(wasdButton.gameObject);
    }

    private void Select(PlayerControlScheme scheme)
    {
        PlayerControlSchemeSettings.Select(scheme);
        SceneFadeTransition.LoadScene(gameSceneName, fadeDuration);
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        GameObject eventSystemObject = new("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        eventSystemObject.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
    }

    private static Button CreateChoiceButton(Transform parent, string title, string detail, TMP_FontAsset uiFont, Vector2 position, Color color, UnityEngine.Events.UnityAction onClick)
    {
        GameObject buttonObject = new(title, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(455f, 280f);

        Image image = buttonObject.GetComponent<Image>();
        image.color = color;
        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = color * 1.22f;
        colors.selectedColor = color * 1.22f;
        colors.pressedColor = color * .78f;
        button.colors = colors;
        button.onClick.AddListener(onClick);

        CreateText("Details", buttonObject.transform, $"{title}\n\n{detail}", 25, FontStyles.Bold, Color.white,
            Vector2.zero, new Vector2(425f, 255f), uiFont);
        return button;
    }

    private static TextMeshProUGUI CreateText(string objectName, Transform parent, string content, float fontSize, FontStyles style, Color color, Vector2 position, Vector2 size, TMP_FontAsset uiFont)
    {
        GameObject textObject = new(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = uiFont;
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static Image CreateImage(string objectName, Transform parent, Color color, Vector2 position, Vector2 size)
    {
        GameObject imageObject = new(objectName, typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
