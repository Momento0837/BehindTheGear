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
    private const string SessionKey = "BehindTheGear.PlayerMovementDemo.Rebuilt.v3";

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

        GameObject player = new("Temporary Player", typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(PlayerController2D));
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

        new GameObject("EventSystem", typeof(EventSystem));
        canvasObject.GetComponent<PlayerControlSelector>().Configure(player.GetComponent<PlayerController2D>(), canvasObject);

        EditorSceneManager.SaveScene(scene, ScenePath, false);
        AssetDatabase.Refresh();
        Debug.Log("PlayerMovementDemo scene rebuilt successfully.");
    }
}
