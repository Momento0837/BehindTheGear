using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>Rebuilds the demo through Unity's scene APIs, avoiding fragile hand-authored YAML.</summary>
internal static class PlayerMovementDemoSceneBuilder
{
    private const string ScenePath = "Assets/Player/Script/PlayerMovementDemo.unity";
    private const string PlayerSpritePath = "Assets/Player/Model/temporary-mechanic-player.png";
    private const string SessionKey = "BehindTheGear.PlayerMovementDemo.Rebuilt.v7";

    [InitializeOnLoadMethod]
    private static void RebuildOnceAfterCompilation()
    {
        if (SessionState.GetBool(SessionKey, false)) return;
        SessionState.SetBool(SessionKey, true);
        EditorApplication.delayCall += Build;
    }

    [MenuItem("Behind The Gear/Rebuild Player Movement Demo")]
    private static void Build()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject cameraObject = new("Main Camera", typeof(Camera), typeof(AudioListener));
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        camera.backgroundColor = new Color(.07f, .11f, .19f);
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);

        GameObject ground = new("Ground", typeof(BoxCollider2D));
        ground.transform.position = new Vector3(0f, -4f, 0f);
        ground.GetComponent<BoxCollider2D>().size = new Vector2(20f, 1f);

        GameObject platform = new("One Way Platform (S+W to drop)", typeof(BoxCollider2D), typeof(PlatformEffector2D));
        platform.transform.position = new Vector3(1f, -1.3f, 0f);
        BoxCollider2D platformCollider = platform.GetComponent<BoxCollider2D>();
        platformCollider.size = new Vector2(5f, .25f);
        platformCollider.usedByEffector = true;
        platform.GetComponent<PlatformEffector2D>().useOneWay = true;

        GameObject player = new("Temporary Player", typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(PlayerInputReader), typeof(PlayerMovement2D), typeof(PlayerAttackController), typeof(PlayerInteractionController), typeof(PlayerSpriteFacing), typeof(PlayerInputDebugLogger));
        player.transform.position = new Vector3(-4f, -2.5f, 0f);
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        body.gravityScale = 3f;
        body.freezeRotation = true;
        player.GetComponent<BoxCollider2D>().size = new Vector2(.9f, 1.4f);
        // Import first so rebuilding the scene always assigns the PNG as a Sprite.
        if (AssetImporter.GetAtPath(PlayerSpritePath) is TextureImporter textureImporter)
        {
            textureImporter.textureType = TextureImporterType.Sprite;
            textureImporter.spriteImportMode = SpriteImportMode.Single;
            textureImporter.alphaIsTransparency = true;
            textureImporter.SaveAndReimport();
        }
        else
        {
            AssetDatabase.ImportAsset(PlayerSpritePath, ImportAssetOptions.ForceUpdate);
        }
        Sprite playerSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PlayerSpritePath);
        if (playerSprite == null)
        {
            Debug.LogError($"Player sprite could not be loaded: {PlayerSpritePath}");
        }

        SpriteRenderer playerRenderer = player.GetComponent<SpriteRenderer>();
        playerRenderer.sprite = playerSprite;
        playerRenderer.sortingOrder = 1;

        GameObject interactionObject = new("Interaction Test Object (F)", typeof(BoxCollider2D), typeof(Interactable2D));
        interactionObject.transform.position = new Vector3(-1.6f, -2.8f, 0f);
        BoxCollider2D interactionCollider = interactionObject.GetComponent<BoxCollider2D>();
        interactionCollider.isTrigger = true;
        interactionCollider.size = new Vector2(1.2f, 1.2f);
        GameObject interactionLabel = new("Label (TMP)", typeof(TextMeshPro));
        interactionLabel.transform.SetParent(interactionObject.transform, false);
        TextMeshPro worldLabel = interactionLabel.GetComponent<TextMeshPro>();
        worldLabel.font = TMP_Settings.defaultFontAsset;
        worldLabel.text = "Interact\nObject";
        worldLabel.fontSize = 4f;
        worldLabel.alignment = TextAlignmentOptions.Center;
        worldLabel.color = new Color(1f, .72f, .2f);
        worldLabel.rectTransform.sizeDelta = new Vector2(2.5f, 1.4f);

        GameObject canvasObject = new("Control Selection UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(PlayerControlSelector));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);

        GameObject label = new("Selection Instructions (TMP)", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        label.transform.SetParent(canvasObject.transform, false);
        RectTransform labelTransform = label.GetComponent<RectTransform>();
        labelTransform.anchorMin = labelTransform.anchorMax = new Vector2(.5f, .5f);
        labelTransform.anchoredPosition = new Vector2(0f, 120f);
        labelTransform.sizeDelta = new Vector2(700f, 180f);
        TextMeshProUGUI labelText = label.GetComponent<TextMeshProUGUI>();
        labelText.font = TMP_Settings.defaultFontAsset;
        labelText.fontSize = 34;
        labelText.fontStyle = FontStyles.Bold;
        labelText.alignment = TextAlignmentOptions.Center;
        labelText.color = Color.white;
        labelText.text = "Choose controls\n[1] WASD     [2] Arrow Keys";

        GameObject promptObject = new("Interaction Prompt UI (TMP)", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(InteractionPromptUI));
        Canvas promptCanvas = promptObject.GetComponent<Canvas>();
        promptCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler promptScaler = promptObject.GetComponent<CanvasScaler>();
        promptScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        promptScaler.referenceResolution = new Vector2(1280f, 720f);
        GameObject promptLabel = new("Prompt Text (TMP)", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        promptLabel.transform.SetParent(promptObject.transform, false);
        RectTransform promptTransform = promptLabel.GetComponent<RectTransform>();
        promptTransform.anchorMin = promptTransform.anchorMax = new Vector2(.5f, .5f);
        promptTransform.anchoredPosition = new Vector2(0f, -150f);
        promptTransform.sizeDelta = new Vector2(360f, 70f);
        TextMeshProUGUI promptText = promptLabel.GetComponent<TextMeshProUGUI>();
        promptText.font = TMP_Settings.defaultFontAsset;
        promptText.fontSize = 32f;
        promptText.alignment = TextAlignmentOptions.Center;
        promptText.color = new Color(1f, .9f, .35f);
        InteractionPromptUI prompt = promptObject.GetComponent<InteractionPromptUI>();
        prompt.Configure(promptText);
        player.GetComponent<PlayerInteractionController>().ConfigurePrompt(prompt);

        new GameObject("EventSystem", typeof(EventSystem));
        canvasObject.GetComponent<PlayerControlSelector>().Configure(player.GetComponent<PlayerInputReader>(), canvasObject);

        EditorSceneManager.SaveScene(scene, ScenePath, false);
        AssetDatabase.Refresh();
        Debug.Log("PlayerMovementDemo scene rebuilt successfully.");
    }
}
