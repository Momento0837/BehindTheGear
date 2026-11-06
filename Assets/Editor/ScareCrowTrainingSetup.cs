using System;
using System.IO;
using System.Linq;
using BehindTheGear.HideOut;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Runs only on an explicit menu action or project-Temp request. Never regenerates on scene open.
[InitializeOnLoad]
public static class ScareCrowTrainingSetup
{
    private const string Folder = "Assets/Scenes/HideOutSystemScene/ScareCrowScene";
    private const string Request = "Temp/ScareCrowValidation/setup.flag";
    private static Sprite square, circle;
    private static Material material;
    static ScareCrowTrainingSetup() => EditorApplication.update += Poll;
    private static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        try { Build(); File.WriteAllText("Temp/ScareCrowValidation/setup-result.txt", "PASS scene built and serialized with existing GUID; scene references checked.\n"); }
        catch (Exception e) { File.WriteAllText("Temp/ScareCrowValidation/setup-result.txt", "FAIL " + e); Debug.LogException(e); }
    }

    [MenuItem("Tools/HideOut/Build ScareCrow training room (explicit)")]
    public static void Build()
    {
        if (Application.unityVersion != "6000.3.23f1" || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Requires edit mode in Unity 6000.3.23f1.");
        var existing = SceneManager.GetSceneByPath(ScareCrowTrainingScene.ScenePath);
        if (existing.IsValid() && existing.isDirty) throw new InvalidOperationException("ScareCrowScene has unsaved edits; preserve them before generating.");
        if (existing.IsValid() && existing.GetRootGameObjects().Any(x => x.GetComponent<ScareCrowTrainingScene>() != null))
            throw new InvalidOperationException("Training room already exists; edit its objects directly.");
        string original = File.ReadAllText(ScareCrowTrainingScene.ScenePath);
        if (original.Contains("ScareCrow Training Room")) throw new InvalidOperationException("Refusing to overwrite an authored training room.");
        var previous = SceneManager.GetActiveScene();
        bool opened = !existing.IsValid();
        var scene = opened ? EditorSceneManager.OpenScene(ScareCrowTrainingScene.ScenePath, OpenSceneMode.Additive) : existing;
        try
        {
            SceneManager.SetActiveScene(scene);
            square = Shape("TrainingSquare", false); circle = Shape("TrainingDisc", true);
            string matPath = Folder + "/TrainingUnlit.mat";
            material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
                AssetDatabase.CreateAsset(material, matPath);
            }
            var settings = AssetDatabase.LoadAssetAtPath<HideOutSettings>("Assets/Scenes/HideOutSystemScene/Data/HideOutPrototype.asset");
            var rig = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Player/SavePlayerRig.prefab");
            var room = new GameObject("ScareCrow Training Room").AddComponent<ScareCrowTrainingScene>();
            room.settings = settings; room.playerPrefab = rig;
            var camera = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Camera>()).Single();
            camera.orthographic = true; camera.orthographicSize = 5.2f;
            camera.transform.SetPositionAndRotation(new Vector3(0, 1.1f, -10), Quaternion.identity);
            camera.backgroundColor = new Color(.045f, .055f, .065f); camera.clearFlags = CameraClearFlags.SolidColor;
            var follow = camera.gameObject.AddComponent<PlayerCameraFollow2D>();
            var followData = new SerializedObject(follow);
            followData.FindProperty("risingCameraY").floatValue = 1.2f;
            followData.FindProperty("fallingCameraY").floatValue = 1.1f;
            followData.FindProperty("lookAheadDistance").floatValue = .65f;
            followData.ApplyModifiedPropertiesWithoutUndo();
            var impact = camera.gameObject.AddComponent<ScareCrowCameraImpact>(); room.trainingCamera = camera;

            Transform background = Root("Background - replaceable", room.transform);
            Draw(background, "Stone wall", 0, 2, 28, 11, new Color(.105f, .13f, .14f), -40);
            for (int y = 0; y < 8; y++)
            {
                Draw(background, "Mortar horizontal", 0, -1.4f + y * 1.2f, 27, .025f, new Color(.065f, .083f, .09f), -39);
                for (int x = 0; x < 10; x++)
                    Draw(background, "Mortar vertical", -13 + x * 3 + (y % 2) * 1.5f, -.8f + y * 1.2f, .025f, 1.2f, new Color(.065f, .083f, .09f), -39);
            }
            Draw(background, "Lower timber", 0, -1.5f, 27, .28f, new Color(.22f, .16f, .11f), -37);
            for (int x = -12; x <= 12; x += 6)
            {
                Draw(background, "Timber support", x, 2.5f, .24f, 9, new Color(.23f, .17f, .115f), -36);
                Draw(background, "Iron strap", x, 1.8f, .34f, .22f, new Color(.33f, .36f, .35f), -35);
            }
            Transform floor = Root("Floor and safety boundaries", room.transform);
            var ground = Draw(floor, "Solid floor", 0, -2.5f, 28, 1, new Color(.21f, .205f, .17f), -10);
            ground.gameObject.layer = LayerMask.NameToLayer("Ground"); ground.gameObject.AddComponent<BoxCollider2D>();
            Draw(floor, "Floor brass edge", 0, -2.01f, 28, .08f, new Color(.53f, .39f, .2f), -9);
            for (int i = -12; i <= 12; i += 2) Draw(floor, "Floor board join", i, -2.5f, .035f, .9f, new Color(.11f, .11f, .095f), -9);
            foreach (float x in new[] { -13.5f, 13.5f })
            {
                var boundary = Root("Side boundary", floor); boundary.position = new Vector3(x, 4, 0);
                boundary.gameObject.layer = LayerMask.NameToLayer("Ground"); boundary.gameObject.AddComponent<BoxCollider2D>().size = new Vector2(1, 14);
            }
            var ceiling = Root("Ceiling boundary", floor); ceiling.position = new Vector3(0, 10, 0);
            ceiling.gameObject.layer = LayerMask.NameToLayer("Ground"); ceiling.gameObject.AddComponent<BoxCollider2D>().size = new Vector2(28, 1);
            Draw(floor, "Training mat", 0, -1.945f, 12, .09f, new Color(.29f, .29f, .24f), -8);
            for (int i = -5; i <= 5; i++) Draw(floor, "Training marks", i, -1.88f, .035f, .09f, new Color(.73f, .57f, .3f), -7);

            Transform decor = Root("Decorations - replaceable", room.transform);
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 9.3f;
                Draw(decor, "Crate", x, -1.25f, 1.55f, 1.4f, new Color(.32f, .225f, .13f), -25);
                Draw(decor, "Crate lid", x, -.55f, 1.65f, .13f, new Color(.46f, .33f, .19f), -24);
                for (int i = -1; i <= 1; i += 2) Draw(decor, "Crate brace", x + i * .5f, -1.25f, .12f, 1.35f, new Color(.20f, .15f, .1f), -24);
                Draw(decor, "Lamp halo", side * 6.8f, 3.5f, 2.8f, 2.8f, new Color(1, .67f, .22f, .045f), -34, true);
                Draw(decor, "Lamp bracket", side * 6.8f, 3.6f, .5f, .8f, new Color(.19f, .19f, .17f), -33);
                Draw(decor, "Lamp", side * 6.8f, 3.5f, .23f, .43f, new Color(1, .75f, .35f), -32);
            }
            Draw(decor, "Weapon rack", -9.3f, 1.05f, 2.3f, .15f, new Color(.4f, .28f, .16f), -25);
            for (int i = 0; i < 3; i++)
            {
                float x = -10 + i * .65f;
                Draw(decor, "Rack practice blade", x, 1.1f, .1f, 1.7f, new Color(.51f, .57f, .56f), -24);
                Draw(decor, "Rack hilt", x, .55f, .4f, .1f, new Color(.65f, .45f, .21f), -23);
            }
            var dummyRoot = Root("Training Dummy - single damage receiver", room.transform);
            dummyRoot.position = new Vector3(2.3f, -2, 0); dummyRoot.gameObject.layer = LayerMask.NameToLayer("Monster");
            var hitbox = dummyRoot.gameObject.AddComponent<BoxCollider2D>(); hitbox.size = new Vector2(1.2f, 2.8f); hitbox.offset = new Vector2(0, 1.4f);
            room.dummy = dummyRoot.gameObject.AddComponent<HideOutDummy>();
            var visual = Root("Visual Root - replaceable", dummyRoot);
            Draw(visual, "Wooden base", 0, .12f, 1.8f, .24f, new Color(.31f, .22f, .125f), 1);
            Draw(visual, "Wooden post", 0, .95f, .22f, 1.8f, new Color(.38f, .27f, .15f), 2);
            Draw(visual, "Crossbar", 0, 1.9f, 2, .2f, new Color(.46f, .32f, .17f), 3);
            Draw(visual, "Linen torso", 0, 1.65f, 1.1f, 1.5f, new Color(.66f, .53f, .32f), 4);
            Draw(visual, "Straw head", 0, 2.65f, .64f, .64f, new Color(.74f, .61f, .38f), 4, true);
            Draw(visual, "Rope belt", 0, 1.05f, 1.17f, .12f, new Color(.38f, .27f, .14f), 5);
            Draw(visual, "Target outer", 0, 1.83f, .92f, .92f, new Color(.34f, .12f, .085f), 5, true);
            Draw(visual, "Target linen", 0, 1.83f, .68f, .68f, new Color(.9f, .81f, .6f), 6, true);
            Draw(visual, "Target ring", 0, 1.83f, .45f, .45f, new Color(.55f, .19f, .1f), 7, true);
            Draw(visual, "Target center", 0, 1.83f, .18f, .18f, new Color(.95f, .76f, .32f), 8, true);
            var anchor = Root("Damage number anchor", dummyRoot); anchor.localPosition = new Vector3(0, 3.1f, 0);
            var sparks = Root("Impact sparks - replaceable", dummyRoot); sparks.localPosition = new Vector3(0, 1.83f, 0);
            for (int i = 0; i < 4; i++)
            {
                var ray = Draw(sparks, "Spark ray", 0, 0, .07f, .72f, new Color(1, .8f, .4f), 12);
                ray.transform.localRotation = Quaternion.Euler(0, 0, i * 45);
            }
            sparks.gameObject.SetActive(false);
            room.playerStart = Root("Player Start", room.transform); room.playerStart.position = new Vector3(-3.8f, -.85f, 0);
            room.playerStart.gameObject.AddComponent<SceneSpawnPoint2D>();
            // Visual-only placement reference: never a second player or combat target.
            var silhouette = Draw(room.playerStart, "Player placement preview (editor only)", 0, 0, .9f, 2.15f, new Color(.55f, .8f, .8f, .2f), -5);
            silhouette.gameObject.AddComponent<ScareCrowPlacementPreview>();

            var canvas = new GameObject("Training HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
            canvas.transform.SetParent(room.transform, false); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f; room.trainingCanvas = canvas;
            // Upper-right leaves the existing upper-left HP/MP and lower skill HUD untouched.
            var plate = new GameObject("Training panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            plate.transform.SetParent(canvas.transform, false); SetRect(plate.GetComponent<RectTransform>(), 1110, 24, 462, 222);
            plate.GetComponent<Image>().color = new Color(.035f, .045f, .05f, .94f);
            Label(plate.transform, "훈련장", 18, 12, 425, 30, 24, settings.font);
            room.weaponLabel = Label(plate.transform, "장착된 무기가 없습니다.", 18, 50, 425, 48, 18, settings.font);
            room.lastHitLabel = Label(plate.transform, "최근 타격  —", 18, 104, 425, 32, 18, settings.font);
            MakeButton(plate.transform, "시설 강화", 18, 156, 190, room.OpenUpgrade, settings.font);
            MakeButton(plate.transform, "기지로 돌아가기", 224, 156, 220, room.ReturnToBase, settings.font);
            Label(canvas.transform, "이동·공격: 현재 조작 설정 사용   ·   ESC: 패널 닫기 / 기지 복귀", 450, 842, 930, 32, 16, settings.font);
            var events = new GameObject("Training input context", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(room.transform, false); room.inputContext = events;
            var feedback = dummyRoot.gameObject.AddComponent<ScareCrowHitFeedback>();
            feedback.dummy = room.dummy; feedback.visualRoot = visual; feedback.numberAnchor = anchor; feedback.sparkRoot = sparks;
            feedback.numberCanvas = canvas; feedback.worldCamera = camera; feedback.cameraImpact = impact; feedback.font = settings.font;
            feedback.hitAudio = dummyRoot.gameObject.AddComponent<AudioSource>(); feedback.hitAudio.playOnAwake = false; feedback.hitAudio.spatialBlend = 0;
            CheckReferences(room);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            var list = EditorBuildSettings.scenes.ToList();
            if (!list.Any(x => x.path == ScareCrowTrainingScene.ScenePath)) list.Add(new EditorBuildSettingsScene(ScareCrowTrainingScene.ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            ConfigureBase(settings, rig);
        }
        finally
        {
            if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void ConfigureBase(HideOutSettings settings, GameObject rig)
    {
        var scene = SceneManager.GetSceneByPath(settings.scenePath); bool opened = !scene.IsValid();
        if (!opened && scene.isDirty) throw new InvalidOperationException("Base has unsaved scene edits. Assign trainingPlayerPrefab to SavePlayerRig without losing those edits.");
        if (opened) scene = EditorSceneManager.OpenScene(settings.scenePath, OpenSceneMode.Additive);
        try
        {
            var view = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<HideOutScene>()).Single(x => x.gameObject.hideFlags == HideFlags.None);
            view.trainingPlayerPrefab = rig;
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
    }
    private static void CheckReferences(ScareCrowTrainingScene room)
    {
        if (room.dummy == null || room.playerPrefab == null || room.settings == null || room.trainingCamera == null || room.weaponLabel == null)
            throw new InvalidOperationException("Missing scene references.");
        if (room.GetComponentsInChildren<HideOutDummy>().Length != 1 || room.GetComponentsInChildren<PlayerInventory>().Length != 0)
            throw new InvalidOperationException("Must have one dummy and no pre-spawned player.");
    }
    private static Transform Root(string name, Transform parent)
    { var root = new GameObject(name).transform; root.SetParent(parent, false); return root; }
    private static SpriteRenderer Draw(Transform parent, string name, float x, float y, float w, float h, Color tint, int order, bool disc = false)
    {
        var root = Root(name, parent); root.localPosition = new Vector3(x, y, 0); root.localScale = new Vector3(w, h, 1);
        var renderer = root.gameObject.AddComponent<SpriteRenderer>(); renderer.sprite = disc ? circle : square;
        renderer.color = tint; renderer.sortingOrder = order; renderer.sharedMaterial = material; return renderer;
    }
    private static Sprite Shape(string name, bool disc)
    {
        string path = Folder + "/" + name + ".png";
        if (!File.Exists(path))
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), Vector2.one * (size / 2f));
                texture.SetPixel(x, y, new Color(1, 1, 1, disc ? Mathf.Clamp01(size / 2f - d) : 1));
            }
            texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = size; importer.mipmapEnabled = false; importer.alphaIsTransparency = true; importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
    private static void SetRect(RectTransform rect, float x, float y, float w, float h)
    { rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h); }
    private static TMP_Text Label(Transform parent, string value, float x, float y, float w, float h, int size, TMP_FontAsset font)
    {
        var label = new GameObject(value, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        label.transform.SetParent(parent, false); SetRect(label.rectTransform, x, y, w, h);
        UIFontTheme.Apply(label, font); label.text = value; label.fontSize = size; label.raycastTarget = false; label.color = new Color(.94f, .9f, .79f);
        return label;
    }
    private static void MakeButton(Transform parent, string value, float x, float y, float w, UnityEngine.Events.UnityAction callback, TMP_FontAsset font)
    {
        var button = new GameObject(value, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button)).GetComponent<Button>();
        button.transform.SetParent(parent, false); SetRect(button.GetComponent<RectTransform>(), x, y, w, 46);
        button.GetComponent<Image>().color = new Color(.28f, .23f, .15f); button.targetGraphic = button.GetComponent<Image>();
        Label(button.transform, value, 0, 0, w, 46, 18, font).alignment = TextAlignmentOptions.Center;
        UnityEventTools.AddPersistentListener(button.onClick, callback);
    }
}
