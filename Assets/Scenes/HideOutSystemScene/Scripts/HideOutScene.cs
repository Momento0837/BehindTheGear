using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BehindTheGear.HideOut
{
    // Runtime-only prototype view. Game state, upgrades and saving remain owned by HideOutPlayerBridge.
    public sealed class HideOutScene : MonoBehaviour
    {
        private const float ReferenceWidth = 1600f;
        private const float ReferenceHeight = 900f;
        private const int StorageRowsPerPage = 10;

        private sealed class StorageSlotView
        {
            public Image background;
            public Image icon;
            public TMP_Text name;
            public TMP_Text amount;
        }

        public HideOutSettings settings;
        [Tooltip("Show the temporary hideout and HUD in Scene/Game views before Play.")]
        public bool editorAutoPreview = true;

        private HideOutPlayerBridge player;
        private Transform ui;
        private GameObject baseView;
        private RectTransform canvasRect;
        private Camera hideoutCamera;
        private HideOutCameraController cameraController;
        private HideOutIsometricEnvironment isometricEnvironment;
        private Transform overviewCameraAnchor;
        private GameObject upgradeOverlay;
        private GameObject storagePanel;
        private GameObject tooltip;
        private TMP_Text tooltipText;
        private readonly TMP_Text[] resourceValues = new TMP_Text[4];
        private readonly Image[] resourceBars = new Image[2];
        private readonly List<Sprite> hudSprites = new();
        private TMP_Text feedbackText;
        private TMP_Text upgradeTitle;
        private TMP_Text upgradeLevel;
        private TMP_Text progressText;
        private TMP_Text timerText;
        private TMP_Text currentEffectText;
        private TMP_Text nextEffectText;
        private TMP_Text coinCostText;
        private TMP_Text actionCostText;
        private TMP_Text materialCostText;
        private Image progressFill;
        private Image facilityImage;
        private Button upgradeButton;
        private Button useFacilityButton;
        private GameObject exhibitionPanel;
        private TMP_Text exhibitionTitle, exhibitionBody;
        [Serializable] public sealed class FacilityUpgradeEvent : UnityEngine.Events.UnityEvent<int, int> { }
        public FacilityUpgradeEvent onFacilityUpgraded = new();
        private readonly Dictionary<Facility, Button> shortcutButtons = new();
        private readonly Dictionary<Facility, TMP_Text> shortcutLabels = new();
        private bool editorPreview;
        private GameObject dummyPanel;
        private HideOutDummy dummy;
        private Image dummyHighlight;
        private TMP_Text dummyLevelText;
        private TMP_Text dummyWeaponText;
        private TMP_Text dummyDamageText;
        private TMP_Text dummyBonusText;
        private TMP_Text dummyLastDamageText;
        private TMP_Text dummyCostText;
        private TMP_Text dummyDamagePopup;
        private Button dummyTestButton;
        private Button dummyUpgradeButton;
        private Button dummyTargetButton;
        private TMP_Text roomNameText;
        private TMP_Text inventoryCapacityText;
        private TMP_Text storageCapacityText;
        private TMP_Text inventoryPageText;
        private TMP_Text storagePageText;
        private TMP_Text storageUpgradeCostText;
        private Button depositButton;
        private Button withdrawButton;
        private Button inventoryPreviousButton;
        private Button inventoryNextButton;
        private Button storagePreviousButton;
        private Button storageNextButton;
        private Button storageUpgradeButton;
        private readonly List<StorageSlotView> inventorySlotViews = new();
        private readonly List<StorageSlotView> storageSlotViews = new();
        private readonly GameObject[] storageTierVisuals = new GameObject[4];
        private bool dummyTesting;
        private float dummyDamagePopupUntil;
        private int inventoryPage;
        private int storagePage;
        private int selectedInventorySlot = -1;
        private int selectedStorageSlot = -1;
        private Sprite circleSprite;
        private Facility selected = Facility.Storage;
        private bool hasSelection;
        private float nextRefresh;
        private Rigidbody2D body;
        private bool bodyWasSimulated;
        private bool released;
        private readonly List<Behaviour> suspendedControls = new();
        private readonly Dictionary<Facility, Image> hotspotRings = new();
        private readonly Dictionary<Facility, Button> facilityButtons = new();
        private readonly Dictionary<Facility, TMP_Text> facilityLevelLabels = new();
        private readonly Dictionary<Facility, Transform> cameraAnchors = new();
        private readonly Dictionary<Facility, Transform> facilityWorldAnchors = new();
        private readonly Dictionary<Facility, RectTransform> facilityOverlayRoots = new();
        private RectTransform dummyTargetRect;
        private RectTransform dummyPopupRect;

        private static readonly Color Background = new(.025f, .035f, .045f, 1f);
        private static readonly Color Wall = new(.075f, .085f, .09f, 1f);
        private static readonly Color Floor = new(.045f, .055f, .06f, 1f);
        private static readonly Color Panel = new(.055f, .07f, .08f, .96f);
        private static readonly Color PanelSoft = new(.09f, .105f, .11f, .92f);
        private static readonly Color Accent = new(.83f, .65f, .32f, 1f);
        private static readonly Color AccentSoft = new(.83f, .65f, .32f, .22f);
        private static readonly Color Muted = new(.63f, .67f, .68f, 1f);

        private void Start()
        {
            if (editorPreview) return;
            if (settings == null || settings.font == null)
            {
                Debug.LogError("[HideOut] Settings / Korean Font is missing.", this);
                return;
            }

            var persistent = FindFirstObjectByType<PersistentPlayer2D>();
            if (persistent != null) player = persistent.GetComponent<HideOutPlayerBridge>();
            if (player == null)
            {
                // Reuse a live inventory when present. Direct scene play gets one isolated sandbox actor.
                var existing = FindFirstObjectByType<PlayerInventory>();
                if (existing != null)
                {
                    player = existing.GetComponent<HideOutPlayerBridge>() ?? existing.gameObject.AddComponent<HideOutPlayerBridge>();
                    if (existing.GetComponent<PlayerHealth>() == null)
                    {
                        Debug.LogError("[HideOut] Existing player has no PlayerHealth.", existing);
                        return;
                    }
                }
                else
                {
                    GameObject actor = new("HideOut Sandbox Player");
                    actor.SetActive(false);
                    actor.AddComponent<PlayerStats>();
                    actor.AddComponent<PlayerInventory>();
                    var controlLock = actor.AddComponent<PlayerControlLock>();
                    JsonUtility.FromJsonOverwrite("{\"controlsToDisable\":[]}", controlLock);
                    actor.AddComponent<PlayerHealth>();
                    actor.AddComponent<BoxCollider2D>();
                    actor.AddComponent<PlayerMeleeAttack>();
                    player = actor.AddComponent<HideOutPlayerBridge>();
                    actor.SetActive(true);
                }
            }

            try { player.Initialize(settings, !HideOutTravel.CanReturn); }
            catch (Exception exception)
            {
                Debug.LogError("[HideOut] " + exception.Message, this);
                enabled = false;
                return;
            }

            body = player.GetComponent<Rigidbody2D>();
            if (body != null)
            {
                bodyWasSimulated = body.simulated;
                body.simulated = false;
            }

            foreach (var control in player.GetComponents<Behaviour>())
            {
                if (!control.enabled || control is not (PlayerInputReader or PlayerMovement2D or PlayerInteractionController or InventoryUI)) continue;
                suspendedControls.Add(control);
                control.enabled = false;
            }

            BuildUI();
            player.Changed += Refresh;
            player.UpgradeSucceeded += OnFacilityUpgraded;
            player.Inventory.Changed += Refresh;
            Refresh();
        }

        public void ReleasePlayer()
        {
            if (released) return;
            released = true;
            if (body != null) body.simulated = bodyWasSimulated;
            foreach (var control in suspendedControls) if (control != null) control.enabled = true;
        }

        private void OnDestroy()
        {
            if (cameraController != null) cameraController.MoveCompleted -= Refresh;
            foreach (var sprite in hudSprites)
            {
                if (sprite == null) continue;
                if (Application.isPlaying) { Destroy(sprite.texture); Destroy(sprite); }
                else { DestroyImmediate(sprite.texture); DestroyImmediate(sprite); }
            }
            if (player != null) player.Changed -= Refresh;
            if (player != null) player.UpgradeSucceeded -= OnFacilityUpgraded;
            if (player != null && player.Inventory != null) player.Inventory.Changed -= Refresh;
            if (dummy != null) dummy.Hit -= OnDummyHit;
            ReleasePlayer();
            if (circleSprite != null)
            {
                if (circleSprite.texture != null) { if (Application.isPlaying) Destroy(circleSprite.texture); else DestroyImmediate(circleSprite.texture); }
                if (Application.isPlaying) Destroy(circleSprite); else DestroyImmediate(circleSprite);
            }
        }

        private void Update()
        {
            if (cameraController != null && Keyboard.current != null && Keyboard.current.homeKey.wasPressedThisFrame)
                ReturnToOverview();
            if (dummyDamagePopup != null && dummyDamagePopup.gameObject.activeSelf
                && Time.unscaledTime >= dummyDamagePopupUntil) dummyDamagePopup.gameObject.SetActive(false);
            if (player == null || !player.Initialized || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .25f;
            Refresh();
        }

        private void LateUpdate()
        {
            if (hideoutCamera == null || canvasRect == null) return;
            foreach (var pair in facilityOverlayRoots)
                if (facilityWorldAnchors.TryGetValue(pair.Key, out Transform anchor))
                    PositionOverlay(pair.Value, anchor.position);
            if (dummyTargetRect != null && isometricEnvironment != null && isometricEnvironment.DummyBodyAnchor != null)
                PositionOverlay(dummyTargetRect, isometricEnvironment.DummyBodyAnchor.position + Vector3.down * .95f);
            if (dummyPopupRect != null && isometricEnvironment != null && isometricEnvironment.DummyBodyAnchor != null)
                PositionOverlay(dummyPopupRect, isometricEnvironment.DummyBodyAnchor.position + Vector3.up * .35f);
        }

        private void BuildUI()
        {
            if (!editorPreview) EnsureEventSystem();
            circleSprite = CreateCircleSprite();

            hideoutCamera ??= gameObject.scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Camera>(true))
                .FirstOrDefault(camera => camera.CompareTag("MainCamera")) ?? Camera.main;
            if (hideoutCamera == null) throw new InvalidOperationException("HideOut Main Camera is required.");
            hideoutCamera.orthographic = true;
            hideoutCamera.clearFlags = CameraClearFlags.SolidColor;
            hideoutCamera.backgroundColor = new Color(.008f, .012f, .015f, 1f);
            BuildTopViewMap();

            var canvasObject = new GameObject("HideOut UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            ui = canvasObject.transform;
            canvasRect = canvasObject.GetComponent<RectTransform>();
            ui.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;

            baseView = Box(ui, "HideOut Base View", 0, 0, ReferenceWidth, ReferenceHeight, Color.clear).gameObject;
            Stretch(baseView.GetComponent<RectTransform>());
            BuildBaseHud(baseView.transform);
            BuildFacilityOverlayIcons(baseView.transform);
            BuildStoragePanel(baseView.transform);
            BuildDummyPanel(baseView.transform);
            BuildUpgradeOverlay();
            BuildExhibitionPanel(baseView.transform);
            BuildShortcutBar(baseView.transform);
            storagePanel.transform.localScale = Vector3.one * .86f;
            Rect(storagePanel.GetComponent<RectTransform>(), 895, 45, 770, 820);
            upgradeOverlay.transform.Find("Upgrade Panel").localScale = Vector3.one * .86f;
            Rect(upgradeOverlay.transform.Find("Upgrade Panel").GetComponent<RectTransform>(), 1020, 45, 650, 820);
            Rect(dummyPanel.GetComponent<RectTransform>(), 202, 500, 1196, 270);
            ConfigureTopViewCamera();
        }

        private void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var events = new GameObject("HideOut EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(transform, false);
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        private void BuildTopViewMap()
        {
            var environmentObject = new GameObject("HideOut Isometric Diorama");
            environmentObject.transform.SetParent(transform, false);
            isometricEnvironment = environmentObject.AddComponent<HideOutIsometricEnvironment>();
            isometricEnvironment.Build(settings);
            dummy = isometricEnvironment.Dummy;
            dummy.Hit += OnDummyHit;
            for (int i = 0; i < storageTierVisuals.Length; i++)
                storageTierVisuals[i] = isometricEnvironment.StorageTiers[i];
            facilityWorldAnchors.Clear();
            foreach (var pair in isometricEnvironment.IconAnchors) facilityWorldAnchors[pair.Key] = pair.Value;
            BuildCameraAnchors();
        }

        private Transform MapLayer(Transform parent, string name, float x, float y, float width, float height)
        {
            var layer = new GameObject(name, typeof(RectTransform));
            layer.transform.SetParent(parent, false);
            Rect(layer.GetComponent<RectTransform>(), x, y, width, height);
            return layer.transform;
        }

        private void BuildCameraAnchors()
        {
            overviewCameraAnchor = MapAnchor("OverviewCameraAnchor", settings.topView.overviewCameraPosition);
            cameraAnchors.Clear();
            foreach (var focus in settings.topView.facilities ?? Array.Empty<HideOutFacilityCameraFocus>())
            {
                if (focus == null || cameraAnchors.ContainsKey(focus.facility)) continue;
                cameraAnchors.Add(focus.facility, MapAnchor(focus.anchorName, focus.cameraPosition));
            }
        }

        private Transform MapAnchor(string name, Vector3 position)
        {
            var anchor = new GameObject(string.IsNullOrWhiteSpace(name) ? "FacilityCameraAnchor" : name);
            anchor.transform.SetParent(isometricEnvironment.transform, false);
            anchor.transform.position = position;
            return anchor.transform;
        }

        private void ConfigureTopViewCamera()
        {
            cameraController = hideoutCamera.GetComponent<HideOutCameraController>()
                ?? hideoutCamera.gameObject.AddComponent<HideOutCameraController>();
            cameraController.Configure(overviewCameraAnchor.position, Quaternion.Euler(settings.topView.cameraEuler),
                settings.topView.overviewOrthographicSize, settings.topView.cameraMoveDuration,
                isometricEnvironment.WorldBounds, settings.topView);
            if (!editorPreview) cameraController.MoveCompleted += Refresh;
        }

        private void BuildBunkerShell(Transform parent)
        {
            Box(parent, "Concrete Wall", 0, 0, ReferenceWidth, 710, Wall);
            Box(parent, "Ceiling Shadow", 0, 0, ReferenceWidth, 118, new Color(.018f, .023f, .027f));
            Box(parent, "Floor", 0, 710, ReferenceWidth, 190, Floor);
            Box(parent, "Floor Edge", 0, 704, ReferenceWidth, 8, new Color(.16f, .15f, .12f));

            Box(parent, "Main Pipe", 135, 92, 1160, 22, new Color(.13f, .14f, .135f));
            Box(parent, "Pipe Highlight", 135, 92, 1160, 4, new Color(.25f, .24f, .20f));
            for (int i = 0; i < 5; i++) Box(parent, "Wall Seam", 420 + i * 235, 120, 3, 584, new Color(.035f, .04f, .042f));
        }

        private void BuildLockedArea(Transform parent)
        {
            Box(parent, "Locked Area Frame", 22, 134, 360, 570, new Color(.03f, .034f, .036f));
            Box(parent, "Locked Door", 72, 215, 260, 489, new Color(.018f, .021f, .023f));
            Box(parent, "Locked Door Edge", 68, 204, 268, 12, new Color(.11f, .115f, .11f));
            var lockButton = Button(parent, "Locked Area", 142, 422, 120, 120,
                () => player.SetMessage("아직 해금되지 않은 시설입니다."), new Color(.08f, .085f, .085f), 0);
            lockButton.GetComponent<Image>().sprite = circleSprite;
            Text(lockButton.transform, "잠금", 10, 29, 100, 34, 22, new Color(.55f, .56f, .55f)).alignment = TextAlignmentOptions.Center;
            Text(lockButton.transform, "LOCKED", 10, 62, 100, 24, 12, new Color(.34f, .36f, .36f)).alignment = TextAlignmentOptions.Center;
            AddHover(lockButton.gameObject, () => ShowLockedTooltip(275, 400), HideTooltip);
            Box(parent, "Locked Shade", 0, 118, 405, 594, new Color(0, 0, 0, .38f));
        }

        private void BuildStorageArea(Transform parent)
        {
            for (int i = 0; i < storageTierVisuals.Length; i++)
            {
                storageTierVisuals[i] = new GameObject($"Storage Appearance Tier {i}", typeof(RectTransform));
                storageTierVisuals[i].transform.SetParent(parent, false);
                Stretch(storageTierVisuals[i].GetComponent<RectTransform>());
            }

            Transform initial = storageTierVisuals[0].transform;
            Box(initial, "Storage Back Glow", 470, 210, 520, 496, new Color(.20f, .14f, .075f, .10f));
            Box(initial, "Storage Frame", 500, 250, 460, 454, new Color(.13f, .095f, .06f));
            Box(initial, "Storage Inner", 520, 272, 420, 410, new Color(.055f, .058f, .052f));
            Box(initial, "Storage Left Post", 520, 272, 16, 410, new Color(.28f, .20f, .115f));
            Box(initial, "Storage Right Post", 924, 272, 16, 410, new Color(.28f, .20f, .115f));
            for (int i = 0; i < 4; i++)
                Box(initial, "Storage Shelf", 520, 350 + i * 106, 420, 14, new Color(.31f, .22f, .12f));
            Box(initial, "Storage Crate A", 552, 288, 128, 62, new Color(.28f, .18f, .09f));
            Box(initial, "Storage Crate A Brace", 608, 288, 10, 62, new Color(.15f, .09f, .045f));
            Box(initial, "Storage Crate B", 718, 300, 168, 50, new Color(.22f, .145f, .075f));
            Box(initial, "Storage Sack", 570, 390, 82, 64, new Color(.36f, .31f, .21f));

            Transform tier2 = storageTierVisuals[1].transform;
            Box(tier2, "Storage Extra Crate A", 694, 382, 186, 72, new Color(.31f, .20f, .10f));
            Box(tier2, "Storage Extra Crate B", 548, 500, 132, 62, new Color(.24f, .16f, .08f));
            Box(tier2, "Storage Extra Brace", 748, 382, 11, 72, new Color(.16f, .10f, .05f));

            Transform tier4 = storageTierVisuals[2].transform;
            Box(tier4, "Storage Metal Case", 710, 488, 176, 74, new Color(.20f, .25f, .25f));
            Box(tier4, "Storage Case Latch", 786, 506, 24, 18, new Color(.55f, .47f, .25f));
            Box(tier4, "Storage Upper Rack", 548, 594, 338, 72, new Color(.18f, .135f, .08f));

            Transform tier6 = storageTierVisuals[3].transform;
            Box(tier6, "Storage Tier 6 Light", 486, 226, 488, 12, new Color(.72f, .56f, .25f, .72f));
            Box(tier6, "Storage Tier 6 Side Light", 486, 226, 10, 478, new Color(.72f, .56f, .25f, .45f));
            Box(tier6, "Storage Secured Chest", 690, 588, 202, 78, new Color(.30f, .25f, .13f));
            Box(tier6, "Storage Secured Lock", 776, 606, 30, 28, Accent);

            var ring = Box(parent, "Storage Hotspot Glow", 672, 365, 100, 100, AccentSoft);
            ring.sprite = circleSprite;
            hotspotRings[Facility.Storage] = ring;
            var hotspot = Button(parent, "Storage Hotspot", 682, 375, 80, 80, () => SelectFacility(Facility.Storage),
                new Color(.12f, .14f, .14f, .96f), 0);
            facilityButtons[Facility.Storage] = hotspot;
            hotspot.GetComponent<Image>().sprite = circleSprite;
            var outline = hotspot.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(Accent.r, Accent.g, Accent.b, .85f);
            outline.effectDistance = new Vector2(2, -2);
            Box(hotspot.transform, "Storage Box Icon", 19, 25, 42, 32, new Color(.90f, .76f, .48f));
            Box(hotspot.transform, "Storage Box Lid", 16, 21, 48, 8, new Color(.97f, .86f, .62f));
            Box(hotspot.transform, "Storage Box Lock", 35, 34, 10, 10, new Color(.28f, .20f, .10f));
            var level = Text(hotspot.transform, "", -10, 80, 100, 24, 14, Color.white);
            level.alignment = TextAlignmentOptions.Center;
            facilityLevelLabels[Facility.Storage] = level;
            AddHover(hotspot.gameObject, () => ShowFacilityTooltip(Facility.Storage, 778, 372), HideTooltip);
        }

        private void BuildTreatmentBooth(Transform parent)
        {
            Box(parent, "Treatment Booth Shadow", 1095, 220, 400, 492, new Color(0, 0, 0, .25f));
            Box(parent, "Treatment Booth Back", 1120, 242, 350, 462, new Color(.07f, .095f, .095f));
            Box(parent, "Treatment Booth Canopy", 1108, 232, 374, 28, new Color(.18f, .235f, .23f));
            Box(parent, "Treatment Booth Left Post", 1120, 260, 16, 444, new Color(.17f, .22f, .215f));
            Box(parent, "Treatment Booth Right Post", 1454, 260, 16, 444, new Color(.17f, .22f, .215f));
            Box(parent, "Treatment Curtain Left", 1136, 264, 58, 270, new Color(.12f, .25f, .25f, .92f));
            Box(parent, "Treatment Curtain Right", 1396, 264, 58, 270, new Color(.12f, .25f, .25f, .92f));
            for (int i = 0; i < 4; i++)
            {
                Box(parent, "Curtain Fold Left", 1145 + i * 12, 264, 4, 270, new Color(.19f, .34f, .33f, .55f));
                Box(parent, "Curtain Fold Right", 1405 + i * 12, 264, 4, 270, new Color(.19f, .34f, .33f, .55f));
            }

            Box(parent, "Treatment Lamp Arm", 1288, 276, 10, 88, new Color(.34f, .36f, .33f));
            var lamp = Box(parent, "Treatment Lamp", 1257, 342, 72, 34, new Color(.68f, .70f, .58f));
            lamp.sprite = circleSprite;
            Box(parent, "Treatment Bed", 1172, 548, 246, 76, new Color(.31f, .43f, .42f));
            Box(parent, "Treatment Mattress", 1180, 536, 230, 30, new Color(.66f, .72f, .66f));
            Box(parent, "Treatment Pillow", 1192, 518, 70, 28, new Color(.78f, .80f, .72f));
            Box(parent, "Treatment Bed Base", 1200, 624, 190, 34, new Color(.15f, .18f, .18f));
            Box(parent, "Treatment Bed Left Leg", 1210, 656, 14, 46, new Color(.12f, .14f, .14f));
            Box(parent, "Treatment Bed Right Leg", 1366, 656, 14, 46, new Color(.12f, .14f, .14f));

            Box(parent, "IV Stand", 1420, 390, 7, 194, new Color(.43f, .46f, .43f));
            Box(parent, "IV Hook", 1397, 390, 52, 7, new Color(.43f, .46f, .43f));
            Box(parent, "IV Bag", 1394, 407, 34, 52, new Color(.39f, .62f, .58f));
            Box(parent, "Treatment Mark V", 1279, 278, 14, 48, new Color(.72f, .22f, .20f));
            Box(parent, "Treatment Mark H", 1262, 295, 48, 14, new Color(.72f, .22f, .20f));

            var hotspot = CircularHotspot(parent, "Treatment Booth Hotspot", Facility.Clinic, 1237, 430);
            Box(hotspot.transform, "Treatment Cross V", 33, 18, 14, 44, new Color(.92f, .94f, .90f));
            Box(hotspot.transform, "Treatment Cross H", 18, 33, 44, 14, new Color(.92f, .94f, .90f));
        }

        // Temporary code-drawn art is intentionally isolated here so a later reference/prefab can replace it.
        private void BuildDummyTrainingArea(Transform parent)
        {
            var light = Box(parent, "Dummy Soft Light", 590, 104, 420, 590, new Color(.76f, .58f, .28f, .075f));
            light.sprite = circleSprite;
            Box(parent, "Dummy Training Recess", 430, 154, 740, 542, new Color(.018f, .023f, .025f, .78f));
            Box(parent, "Dummy Training Mat", 535, 646, 530, 42, new Color(.11f, .09f, .065f));

            Box(parent, "Dummy Crate A", 500, 570, 108, 76, new Color(.24f, .16f, .09f));
            Box(parent, "Dummy Crate A Top", 494, 562, 120, 13, new Color(.34f, .23f, .12f));
            Box(parent, "Dummy Crate A Brace", 542, 570, 10, 76, new Color(.14f, .09f, .055f));
            Box(parent, "Dummy Crate B", 1012, 590, 94, 56, new Color(.20f, .135f, .08f));
            Box(parent, "Dummy Rack Post", 1028, 330, 12, 254, new Color(.25f, .18f, .105f));
            Box(parent, "Dummy Rack Bar", 978, 376, 112, 10, new Color(.32f, .22f, .12f));
            Box(parent, "Dummy Practice Blade", 998, 394, 8, 126, new Color(.48f, .49f, .46f));
            Box(parent, "Dummy Practice Hilt", 984, 490, 38, 8, new Color(.42f, .29f, .13f));

            dummyHighlight = Box(parent, "Dummy Selection Highlight", 705, 178, 190, 450, AccentSoft);
            dummyHighlight.sprite = circleSprite;
            hotspotRings[Facility.Dummy] = dummyHighlight;

            var target = Button(parent, "Training Dummy", 719, 198, 162, 438, OnDummySelected,
                new Color(1f, 1f, 1f, .001f), 0);
            dummyTargetButton = target;
            dummy = target.gameObject.AddComponent<HideOutDummy>();
            dummy.Hit += OnDummyHit;

            var head = Box(target.transform, "Straw Head", 48, 24, 66, 66, new Color(.66f, .49f, .24f));
            head.sprite = circleSprite;
            Box(target.transform, "Head Band", 48, 52, 66, 9, new Color(.25f, .13f, .07f));
            Box(target.transform, "Wood Arm", 8, 120, 146, 15, new Color(.34f, .22f, .11f));
            Box(target.transform, "Straw Left Arm", 12, 107, 48, 40, new Color(.57f, .41f, .20f));
            Box(target.transform, "Straw Right Arm", 102, 107, 48, 40, new Color(.57f, .41f, .20f));
            Box(target.transform, "Dummy Torso", 48, 94, 66, 146, new Color(.61f, .43f, .20f));
            Box(target.transform, "Torso Rope A", 47, 127, 68, 10, new Color(.30f, .17f, .08f));
            Box(target.transform, "Torso Rope B", 47, 190, 68, 10, new Color(.30f, .17f, .08f));
            Box(target.transform, "Dummy Post", 74, 232, 14, 154, new Color(.31f, .20f, .105f));
            Box(target.transform, "Dummy Base", 28, 380, 106, 18, new Color(.28f, .18f, .09f));
            Box(target.transform, "Dummy Base Left", 40, 398, 14, 38, new Color(.24f, .15f, .075f));
            Box(target.transform, "Dummy Base Right", 108, 398, 14, 38, new Color(.24f, .15f, .075f));

            var sword = Button(parent, "Dummy Sword Icon", 765, 128, 72, 72, () => SelectFacility(Facility.Dummy),
                new Color(.10f, .12f, .12f, .97f), 0);
            facilityButtons[Facility.Dummy] = sword;
            sword.GetComponent<Image>().sprite = circleSprite;
            var swordOutline = sword.gameObject.AddComponent<Outline>();
            swordOutline.effectColor = new Color(Accent.r, Accent.g, Accent.b, .9f);
            swordOutline.effectDistance = new Vector2(2, -2);
            var blade = Box(sword.transform, "Sword Blade", 33, 14, 8, 36, new Color(.9f, .91f, .86f));
            blade.rectTransform.localRotation = Quaternion.Euler(0, 0, -42);
            var hilt = Box(sword.transform, "Sword Hilt", 26, 43, 24, 6, Accent);
            hilt.rectTransform.localRotation = Quaternion.Euler(0, 0, -42);
            var dummyLevel = Text(sword.transform, "", -14, 72, 100, 24, 14, Color.white);
            dummyLevel.alignment = TextAlignmentOptions.Center;
            facilityLevelLabels[Facility.Dummy] = dummyLevel;
            AddHover(sword.gameObject, () => ShowFacilityTooltip(Facility.Dummy, 850, 132), HideTooltip);

            dummyDamagePopup = Text(parent, "", 645, 92, 310, 54, 30, new Color(1f, .77f, .30f));
            dummyDamagePopup.alignment = TextAlignmentOptions.Center;
            dummyDamagePopup.gameObject.SetActive(false);
        }

        private void BuildStoragePanel(Transform parent)
        {
            storagePanel = Box(parent, "Storage Panel", 810, 55, 770, 820, new Color(Panel.r, Panel.g, Panel.b, .985f)).gameObject;
            var outline = storagePanel.AddComponent<Outline>();
            outline.effectColor = new Color(Accent.r, Accent.g, Accent.b, .45f);
            outline.effectDistance = new Vector2(1, -1);

            Text(storagePanel.transform, "창고", 28, 20, 220, 42, 28, Color.white);
            var subtitle = Text(storagePanel.transform, "아이템을 선택한 뒤 이동하세요", 124, 28, 420, 30, 15, Muted);
            subtitle.alignment = TextAlignmentOptions.MidlineLeft;
            Button(storagePanel.transform, "Storage Close", 650, 18, 92, 44, CloseStoragePanel,
                new Color(.13f, .15f, .155f), 16).GetComponentInChildren<TMP_Text>().text = "닫기";

            Box(storagePanel.transform, "Inventory Column", 18, 76, 354, 512, PanelSoft);
            Text(storagePanel.transform, "플레이어 인벤토리", 34, 92, 190, 30, 18, Color.white);
            inventoryCapacityText = Text(storagePanel.transform, "", 218, 94, 136, 28, 14, Muted);
            inventoryCapacityText.alignment = TextAlignmentOptions.Right;
            Text(storagePanel.transform, "아이템", 34, 120, 180, 16, 11, Muted);
            var inventoryAmountHeader = Text(storagePanel.transform, "수량", 280, 120, 74, 16, 11, Muted);
            inventoryAmountHeader.alignment = TextAlignmentOptions.Right;

            Box(storagePanel.transform, "Storage Column", 398, 76, 354, 512, PanelSoft);
            Text(storagePanel.transform, "창고", 414, 92, 180, 30, 18, Color.white);
            storageCapacityText = Text(storagePanel.transform, "", 598, 94, 136, 28, 14, Muted);
            storageCapacityText.alignment = TextAlignmentOptions.Right;
            Text(storagePanel.transform, "아이템", 414, 120, 180, 16, 11, Muted);
            var storageAmountHeader = Text(storagePanel.transform, "수량", 660, 120, 74, 16, 11, Muted);
            storageAmountHeader.alignment = TextAlignmentOptions.Right;

            for (int i = 0; i < StorageRowsPerPage; i++)
            {
                int row = i;
                inventorySlotViews.Add(BuildStorageSlot(storagePanel.transform, 32, 138 + i * 42, 326,
                    () => SelectInventorySlot(row)));
                storageSlotViews.Add(BuildStorageSlot(storagePanel.transform, 412, 138 + i * 42, 326,
                    () => SelectStorageSlot(row)));
            }

            inventoryPreviousButton = Button(storagePanel.transform, "Inventory Previous", 32, 558, 46, 30,
                () => ChangeInventoryPage(-1), new Color(.12f, .14f, .15f), 18);
            inventoryPreviousButton.GetComponentInChildren<TMP_Text>().text = "<";
            inventoryPageText = Text(storagePanel.transform, "", 84, 559, 220, 28, 14, Muted);
            inventoryPageText.alignment = TextAlignmentOptions.Center;
            inventoryNextButton = Button(storagePanel.transform, "Inventory Next", 312, 558, 46, 30,
                () => ChangeInventoryPage(1), new Color(.12f, .14f, .15f), 18);
            inventoryNextButton.GetComponentInChildren<TMP_Text>().text = ">";

            storagePreviousButton = Button(storagePanel.transform, "Storage Previous", 412, 558, 46, 30,
                () => ChangeStoragePage(-1), new Color(.12f, .14f, .15f), 18);
            storagePreviousButton.GetComponentInChildren<TMP_Text>().text = "<";
            storagePageText = Text(storagePanel.transform, "", 464, 559, 220, 28, 14, Muted);
            storagePageText.alignment = TextAlignmentOptions.Center;
            storageNextButton = Button(storagePanel.transform, "Storage Next", 692, 558, 46, 30,
                () => ChangeStoragePage(1), new Color(.12f, .14f, .15f), 18);
            storageNextButton.GetComponentInChildren<TMP_Text>().text = ">";

            depositButton = Button(storagePanel.transform, "Deposit Selected", 20, 616, 168, 48,
                DepositSelected, new Color(.18f, .31f, .32f), 15);
            depositButton.GetComponentInChildren<TMP_Text>().text = "창고에 넣기  →";
            withdrawButton = Button(storagePanel.transform, "Withdraw Selected", 198, 616, 168, 48,
                WithdrawSelected, new Color(.18f, .31f, .32f), 15);
            withdrawButton.GetComponentInChildren<TMP_Text>().text = "←  꺼내기";
            Button(storagePanel.transform, "Sort Storage", 376, 616, 168, 48,
                SortStorage, new Color(.14f, .16f, .165f), 14)
                .GetComponentInChildren<TMP_Text>().text = "이름순 정렬";
            Button(storagePanel.transform, "Deposit Existing Types", 554, 616, 196, 48,
                DepositExistingTypes, new Color(.14f, .16f, .165f), 14)
                .GetComponentInChildren<TMP_Text>().text = "기존 종류\n추가 수납";
            var storageNextEffect = Text(storagePanel.transform, "다음: 가방 +2 · 창고 +5", 20, 694, 250, 30, 14, Accent);
            storageNextEffect.alignment = TextAlignmentOptions.Center;
            storageUpgradeButton = Button(storagePanel.transform, "Upgrade Storage", 554, 688, 196, 58,
                UpgradeStorage, Accent, 15);
            storageUpgradeButton.GetComponentInChildren<TMP_Text>().text = "창고 강화";
            storageUpgradeCostText = Text(storagePanel.transform, "", 282, 680, 260, 112, 14, Color.white);
            storageUpgradeCostText.alignment = TextAlignmentOptions.Top;

            storagePanel.SetActive(false);
        }

        private StorageSlotView BuildStorageSlot(Transform parent, float x, float y, float width,
            UnityEngine.Events.UnityAction action)
        {
            var button = Button(parent, "Storage Slot", x, y, width, 40, action,
                new Color(.075f, .085f, .09f, .96f), 0);
            var icon = Box(button.transform, "Item Icon", 4, 4, 32, 32, new Color(.12f, .13f, .13f));
            icon.preserveAspect = true;
            float amountWidth = 72f;
            float amountX = width - amountWidth - 8f;
            var name = Text(button.transform, "", 46, 4, Mathf.Max(72f, amountX - 52f), 32, 14, Color.white);
            name.alignment = TextAlignmentOptions.MidlineLeft;
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.overflowMode = TextOverflowModes.Ellipsis;
            var amount = Text(button.transform, "", amountX, 4, amountWidth, 32, 13, Muted);
            amount.alignment = TextAlignmentOptions.MidlineRight;
            amount.textWrappingMode = TextWrappingModes.NoWrap;
            return new StorageSlotView { background = button.GetComponent<Image>(), icon = icon, name = name, amount = amount };
        }

        private void BuildDummyPanel(Transform parent)
        {
            dummyPanel = Box(parent, "Dummy Bottom Panel", 202, 604, 1196, 270, Panel).gameObject;
            var outline = dummyPanel.AddComponent<Outline>();
            outline.effectColor = new Color(Accent.r, Accent.g, Accent.b, .5f);
            outline.effectDistance = new Vector2(1, -1);

            Text(dummyPanel.transform, "허수아비", 28, 22, 172, 42, 27, Color.white);
            dummyLevelText = Text(dummyPanel.transform, "", 28, 68, 172, 30, 17, Accent);
            Text(dummyPanel.transform, "무기 시험 · 영구 강화", 28, 108, 172, 42, 14, Muted);

            Text(dummyPanel.transform, "현재 장착 무기", 220, 22, 250, 25, 14, Muted);
            dummyWeaponText = Text(dummyPanel.transform, "", 220, 50, 250, 34, 20, Color.white);
            Text(dummyPanel.transform, "현재 무기 최종 데미지", 220, 98, 250, 25, 14, Muted);
            dummyDamageText = Text(dummyPanel.transform, "", 220, 126, 250, 36, 23, Color.white);

            Text(dummyPanel.transform, "허수아비 강화 보너스", 490, 22, 230, 25, 14, Muted);
            dummyBonusText = Text(dummyPanel.transform, "", 490, 50, 230, 34, 22, Accent);
            Text(dummyPanel.transform, "최근 공격 데미지", 490, 98, 230, 25, 14, Muted);
            dummyLastDamageText = Text(dummyPanel.transform, "", 490, 126, 230, 36, 23, new Color(1f, .76f, .3f));

            Text(dummyPanel.transform, "다음 단계 효과", 740, 22, 210, 25, 14, Muted);
            var next = Text(dummyPanel.transform, "무기 데미지  +1", 740, 50, 210, 36, 22, Accent);
            next.fontStyle = FontStyles.Bold;
            Text(dummyPanel.transform, "필요 재료  (보유 / 필요)", 740, 98, 210, 25, 14, Muted);
            dummyCostText = Text(dummyPanel.transform, "", 740, 126, 210, 80, 16, Color.white);

            dummyTestButton = Button(dummyPanel.transform, "Dummy Test Button", 970, 22, 196, 54,
                StartOrAttackDummyTest, new Color(.18f, .31f, .32f), 18);
            dummyUpgradeButton = Button(dummyPanel.transform, "Dummy Upgrade Button", 970, 88, 196, 54,
                UpgradeDummy, Accent, 18);
            Button(dummyPanel.transform, "Dummy Close Button", 970, 172, 196, 48,
                CloseDummyPanel, new Color(.13f, .15f, .155f), 17)
                .GetComponentInChildren<TMP_Text>().text = "닫기";

            dummyPanel.SetActive(false);
        }

        private GameObject CircularHotspot(Transform parent, string name, Facility facility, float x, float y)
        {
            var ring = Box(parent, name + " Glow", x - 10, y - 10, 100, 100, AccentSoft);
            ring.sprite = circleSprite;
            hotspotRings[facility] = ring;

            var button = Button(parent, name, x, y, 80, 80, () => SelectFacility(facility), new Color(.12f, .14f, .14f, .96f), 0);
            facilityButtons[facility] = button;
            button.GetComponent<Image>().sprite = circleSprite;
            var outline = button.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(Accent.r, Accent.g, Accent.b, .85f);
            outline.effectDistance = new Vector2(2, -2);
            var level = Text(button.transform, "", -10, 80, 100, 24, 14, Color.white);
            level.alignment = TextAlignmentOptions.Center;
            facilityLevelLabels[facility] = level;
            AddHover(button.gameObject, () => ShowFacilityTooltip(facility, x + 94, y - 2), HideTooltip);
            return button.gameObject;
        }

        private void BuildFacilityOverlayIcons(Transform parent)
        {
            foreach (var facility in settings.topView.shortcutOrder)
                if (facilityWorldAnchors.ContainsKey(facility))
                    BuildFacilityOverlayIcon(parent, facility, DisplayName(facility), icon => DrawRegisteredIcon(facility, icon));

            dummyTargetButton = Button(parent, "Training Dummy Screen Target", 0, 0, 150, 210,
                OnDummySelected, new Color(1f, 1f, 1f, .001f), 0);
            dummyTargetRect = dummyTargetButton.GetComponent<RectTransform>();
            CenterAnchors(dummyTargetRect);

            dummyDamagePopup = Text(parent, "", 0, 0, 250, 58, 34, new Color(1f, .72f, .28f));
            dummyDamagePopup.alignment = TextAlignmentOptions.Center;
            dummyPopupRect = dummyDamagePopup.rectTransform;
            CenterAnchors(dummyPopupRect);
            dummyDamagePopup.gameObject.SetActive(false);
        }

        private void BuildFacilityOverlayIcon(Transform parent, Facility facility, string label,
            Action<Transform> drawIcon)
        {
            var rootObject = new GameObject(label + " World Icon", typeof(RectTransform));
            rootObject.transform.SetParent(parent, false);
            var root = rootObject.GetComponent<RectTransform>();
            root.sizeDelta = new Vector2(184f, 146f);
            CenterAnchors(root);
            facilityOverlayRoots[facility] = root;

            var ring = Box(root, label + " Selection Ring", 38, -12, 108, 108, AccentSoft);
            ring.sprite = circleSprite;
            hotspotRings[facility] = ring;

            var button = Button(root, label + " Facility Icon", 48, -2, 88, 88,
                () => SelectFacility(facility), new Color(.055f, .065f, .067f, .98f), 0);
            button.GetComponent<Image>().sprite = circleSprite;
            facilityButtons[facility] = button;
            var outline = button.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(.76f, .77f, .72f, .7f);
            outline.effectDistance = new Vector2(2f, -2f);
            drawIcon(button.transform);

            TMP_Text nameLabel = Text(root, label, 0, 91, 184, 28, 19, Color.white);
            nameLabel.alignment = TextAlignmentOptions.Center;
            TMP_Text level = Text(root, "", 0, 119, 184, 24, 15, Accent);
            level.alignment = TextAlignmentOptions.Center;
            facilityLevelLabels[facility] = level;
            AddHover(button.gameObject, () => ShowFacilityTooltip(facility, 0f, 0f), HideTooltip);
        }

        private void DrawRegisteredIcon(Facility facility, Transform parent)
        {
            var sprite = settings.FindPresentation(facility)?.icon;
            if (sprite != null) { var image = Box(parent, "Registered Icon", 18, 18, 52, 52, Color.white); image.sprite = sprite; image.preserveAspect = true; return; }
            if (facility == Facility.Clinic) DrawMedicalIcon(parent);
            else if (facility == Facility.Storage) DrawStorageIcon(parent);
            else if (facility == Facility.Dummy) DrawDummyIcon(parent);
            else if (HideOutModel.IsExhibition(facility))
            {
                if (facility == Facility.Dining)
                {
                    Box(parent, "Table Icon Top", 18, 31, 52, 10, Accent);
                    Box(parent, "Table Icon Leg L", 24, 41, 6, 22, Accent);
                    Box(parent, "Table Icon Leg R", 58, 41, 6, 22, Accent);
                    var plate = Box(parent, "Table Icon Plate", 33, 17, 22, 10, Color.white); plate.sprite = circleSprite;
                }
                else
                {
                    var tool = Box(parent, "Workshop Tool Handle", 40, 19, 9, 47, Color.white);
                    tool.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -35f);
                    Box(parent, "Workshop Tool Head", 23, 17, 29, 12, Accent);
                }
            }
            else
            {
                if (facility == Facility.Bed)
                {
                    Box(parent, "Bed Mattress Icon", 17, 30, 54, 24, Accent);
                    Box(parent, "Bed Pillow Icon", 20, 32, 15, 13, Color.white);
                    Box(parent, "Bed Left Post Icon", 14, 25, 5, 40, Accent);
                    Box(parent, "Bed Right Post Icon", 69, 42, 5, 23, Accent);
                }
                else if (facility == Facility.Potions)
                {
                    var vial = Box(parent, "Potion Vial Icon", 26, 30, 36, 37, Accent); vial.sprite = circleSprite;
                    Box(parent, "Potion Neck Icon", 36, 21, 16, 20, Accent);
                    Box(parent, "Potion Cork Icon", 33, 18, 22, 8, Color.white);
                }
                else
                {
                    Box(parent, "Gym Bar Icon", 16, 39, 56, 8, Color.white);
                    Box(parent, "Gym Left Weight Icon", 22, 23, 12, 40, Accent);
                    Box(parent, "Gym Right Weight Icon", 54, 23, 12, 40, Accent);
                }
            }
        }

        private void BuildShortcutBar(Transform parent)
        {
            var bar = Box(parent, "Facility Shortcuts", 24, 794, 1552, 100, new Color(.035f, .045f, .05f, .98f));
            bar.raycastTarget = true;
            var viewport = Box(bar.transform, "Viewport", 8, 4, 1536, 92, Color.clear);
            viewport.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport.transform, false);
            Rect(content, 0, 0, Mathf.Max(1536, settings.topView.shortcutOrder.Length * 250), 92);
            var scroll = bar.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport.rectTransform; scroll.content = content;
            scroll.horizontal = true; scroll.vertical = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 35f;
            for (int i = 0; i < settings.topView.shortcutOrder.Length; i++)
            {
                Facility facility = settings.topView.shortcutOrder[i];
                var button = Button(content, "Shortcut " + facility, i * 250, 0, 244, 88,
                    () => SelectFacility(facility), PanelSoft, 0);
                shortcutButtons[facility] = button;
                var iconRoot = new GameObject("Icon", typeof(RectTransform)).GetComponent<RectTransform>();
                iconRoot.SetParent(button.transform, false); Rect(iconRoot, 0, 0, 88, 88);
                DrawRegisteredIcon(facility, iconRoot);
                var label = Text(button.transform, "", 90, 14, 150, 64, 16, Color.white);
                shortcutLabels[facility] = label;
            }
        }

        private void DrawMedicalIcon(Transform parent)
        {
            Box(parent, "Medical Cross V", 35, 19, 18, 50, new Color(.88f, .91f, .87f));
            Box(parent, "Medical Cross H", 19, 35, 50, 18, new Color(.88f, .91f, .87f));
        }

        private void DrawStorageIcon(Transform parent)
        {
            Box(parent, "Storage Cube Front", 21, 32, 46, 35, new Color(.89f, .72f, .42f));
            var lid = Box(parent, "Storage Cube Lid", 18, 24, 52, 14, new Color(.98f, .84f, .56f));
            lid.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -7f);
            Box(parent, "Storage Cube Lock", 39, 42, 10, 12, new Color(.27f, .20f, .11f));
        }

        private void DrawDummyIcon(Transform parent)
        {
            var bladeA = Box(parent, "Crossed Blade A", 41, 15, 8, 56, new Color(.88f, .89f, .85f));
            bladeA.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -42f);
            var bladeB = Box(parent, "Crossed Blade B", 41, 15, 8, 56, new Color(.73f, .76f, .74f));
            bladeB.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 42f);
            var hiltA = Box(parent, "Crossed Hilt A", 29, 51, 31, 7, Accent);
            hiltA.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -42f);
            var hiltB = Box(parent, "Crossed Hilt B", 29, 51, 31, 7, Accent);
            hiltB.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 42f);
        }

        private static void CenterAnchors(RectTransform rect)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
        }

        private void PositionOverlay(RectTransform rect, Vector3 worldPosition)
        {
            Vector3 screen = hideoutCamera.WorldToScreenPoint(worldPosition);
            if (screen.z <= 0f) return;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local))
                rect.anchoredPosition = local;
        }

        private void BuildBaseHud(Transform parent)
        {
            Box(parent, "Vignette Top", 0, 0, ReferenceWidth, 105, new Color(0f, 0f, 0f, .48f));
            Box(parent, "Vignette Bottom", 0, 790, ReferenceWidth, 110, new Color(0f, 0f, 0f, .55f));
            Box(parent, "Vignette Left", 0, 100, 96, 690, new Color(0f, 0f, 0f, .32f));
            Box(parent, "Vignette Right", 1504, 100, 96, 690, new Color(0f, 0f, 0f, .32f));

            Button(parent, "Exit Button", 24, 24, 126, 50, ExitHideOut, new Color(.10f, .12f, .13f, .94f), 18)
                .GetComponentInChildren<TMP_Text>().text = "← 나가기";
            Text(parent, "HIDEOUT", 176, 23, 250, 42, 26, Color.white);
            Text(parent, "부지 내부", 176, 55, 250, 26, 14, Muted);

            roomNameText = Text(parent, "전체 하이드아웃", 650, 24, 300, 34, 17, new Color(.80f, .79f, .72f));
            roomNameText.alignment = TextAlignmentOptions.Center;

            BuildResourceHud(parent);
            feedbackText = Text(parent, "", 400, 750, 800, 36, 17, Accent);
            feedbackText.alignment = TextAlignmentOptions.Center;

            var resetCamera = Button(parent, "Reset HideOut Camera", 1418, 92, 154, 44, ReturnToOverview,
                new Color(.09f, .11f, .115f, .94f), 15);
            resetCamera.GetComponentInChildren<TMP_Text>().text = "전체 보기  Home";

            tooltip = Box(parent, "Facility Tooltip", 0, 0, 278, 84, new Color(.025f, .03f, .032f, .96f)).gameObject;
            var tooltipOutline = tooltip.AddComponent<Outline>();
            tooltipOutline.effectColor = new Color(Accent.r, Accent.g, Accent.b, .5f);
            tooltipOutline.effectDistance = new Vector2(1, -1);
            tooltipText = Text(tooltip.transform, "", 14, 10, 250, 64, 16, Color.white);
            tooltipText.alignment = TextAlignmentOptions.MidlineLeft;
            tooltip.SetActive(false);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (player.Sandbox)
            {
                var developmentRoot = new GameObject("Development Only Controls", typeof(RectTransform));
                developmentRoot.transform.SetParent(parent, false); Stretch(developmentRoot.GetComponent<RectTransform>());
                parent = developmentRoot.transform;
                var reset = Button(parent, "Reset Test Progress", 1160, 150, 194, 42, player.ResetTestProgress,
                    new Color(.18f, .105f, .10f, .90f), 14);
                reset.GetComponentInChildren<TMP_Text>().text = "TEST  초기화";
                var test = Button(parent, "Grant Test Resources", 1378, 150, 194, 42, player.GrantTestResources,
                    new Color(.12f, .14f, .145f, .86f), 14);
                test.GetComponentInChildren<TMP_Text>().text = "TEST  + 강화 자원";
                Button(parent, "TEST Selena", 1378, 200, 194, 42, player.TestRecruit, PanelSoft, 14);
            }
#endif
        }

        private void BuildResourceHud(Transform parent)
        {
            string[] labels = { "HP", "MP", "코인", "AP" };
            Color[] colors = { new(.81f,.43f,.39f), new(.35f,.64f,.71f), new(.82f,.67f,.38f), new(.83f,.79f,.66f) };
            for (int i = 0; i < 4; i++)
            {
                var card = Box(parent, "Resource " + labels[i], 976 + i * 151, 18, 143, 60, new Color(.045f,.055f,.06f,.97f));
                Box(card.transform, "Top Edge", 0, 0, 143, 1, new Color(colors[i].r, colors[i].g, colors[i].b, .6f));
                var badge = Box(card.transform, "Icon Socket", 8, 12, 34, 34, new Color(.10f,.12f,.12f)); badge.sprite = circleSprite;
                var icon = Box(card.transform, labels[i] + " Symbol", 13, 17, 24, 24, colors[i]);
                icon.sprite = CreateResourceSprite(i); icon.preserveAspect = true;
                Text(card.transform, labels[i], 50, 6, 86, 18, 12, Muted);
                resourceValues[i] = Text(card.transform, "—", 50, 24, 88, 25, 17, Color.white);
                resourceValues[i].textWrappingMode = TextWrappingModes.NoWrap;
                if (i < 2)
                {
                    Box(card.transform, "Gauge Track", 50, 51, 81, 3, new Color(.15f,.18f,.18f));
                    resourceBars[i] = Box(card.transform, "Gauge Fill", 50, 51, 81, 3, colors[i]);
                }
            }
        }

        private Sprite CreateResourceSprite(int kind)
        {
            const int size = 40;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "HideOut HUD Symbol " + kind, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float u = (x + .5f) / size * 2f - 1f, v = (y + .5f) / size * 2f - 1f;
                bool filled;
                if (kind == 0)
                {
                    float a = u * u + v * v - .56f;
                    filled = a * a * a - u * u * v * v * v < 0f;
                }
                else if (kind == 1) filled = (u*u + (v+.25f)*(v+.25f) < .32f && v < .05f) || (v >= -.1f && v <= .85f && Mathf.Abs(u) < (.85f-v)*.6f);
                else if (kind == 2)
                {
                    float r = u*u + v*v;
                    filled = r < .77f && (r > .48f || Mathf.Abs(u) < .13f && Mathf.Abs(v) < .45f);
                }
                else filled = v >= 0f ? u >= -.17f && u <= .35f + v*.5f && v < .85f : u >= -.35f + v*.5f && u <= .17f && v > -.85f;
                pixels[y * size + x] = filled ? new Color32(255,255,255,255) : new Color32(0,0,0,0);
            }
            texture.SetPixels32(pixels); texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0,0,size,size), new Vector2(.5f,.5f), 100f);
            hudSprites.Add(sprite); return sprite;
        }

        private void BuildExhibitionPanel(Transform parent)
        {
            exhibitionPanel = Box(parent, "Exhibition Information Panel", 1010, 55, 565, 700, Panel).gameObject;
            var outline = exhibitionPanel.AddComponent<Outline>();
            outline.effectColor = AccentSoft; outline.effectDistance = new Vector2(1f, -1f);
            Text(exhibitionPanel.transform, "기지 시설 · 전시 공간", 28, 25, 480, 26, 15, Muted);
            exhibitionTitle = Text(exhibitionPanel.transform, "", 28, 65, 470, 52, 32, Color.white);
            Text(exhibitionPanel.transform, "추후 구현", 28, 125, 480, 30, 20, Accent);
            Box(exhibitionPanel.transform, "Divider", 28, 173, 509, 2, AccentSoft);
            exhibitionBody = Text(exhibitionPanel.transform, "", 28, 202, 509, 390, 21, Color.white);
            Button(exhibitionPanel.transform, "Exhibition Overview", 28, 612, 509, 58, ReturnToOverview, PanelSoft, 19)
                .GetComponentInChildren<TMP_Text>().text = "닫기 · 전체 보기";
            exhibitionPanel.SetActive(false);
        }

        private void ShowExhibition(Facility facility)
        {
            HideStoragePanelSilently(); HideDummyPanelSilently(); upgradeOverlay.SetActive(false);
            exhibitionTitle.text = DisplayName(facility);
            exhibitionBody.text = settings.FindPresentation(facility)?.exhibitionDescription ?? "추후 구현할 전시 공간입니다. 현재 효과는 없습니다.";
            exhibitionPanel.SetActive(true);
        }

        private void OnFacilityUpgraded(Facility facility, int level)
        {
            if (editorPreview || HideOutModel.IsExhibition(facility)) return;
            Refresh(); // Apply the persisted appearance before the animation hook.
            isometricEnvironment.PlayUpgradeFeedback(facility);
            onFacilityUpgraded.Invoke((int)facility, level);
        }

        private void BuildUpgradeOverlay()
        {
            upgradeOverlay = Box(ui, "Upgrade Overlay", 0, 0, ReferenceWidth, ReferenceHeight, Color.clear).gameObject;
            Stretch(upgradeOverlay.GetComponent<RectTransform>());
            var panel = Box(upgradeOverlay.transform, "Upgrade Panel", 930, 55, 650, 820, new Color(Panel.r, Panel.g, Panel.b, .985f)).gameObject;
            var panelOutline = panel.AddComponent<Outline>();
            panelOutline.effectColor = new Color(Accent.r, Accent.g, Accent.b, .42f);
            panelOutline.effectDistance = new Vector2(1, -1);

            Text(panel.transform, "시설 업그레이드", 24, 24, 360, 42, 25, Color.white);
            Button(panel.transform, "Close Upgrade", 530, 20, 92, 44, CloseUpgrade, new Color(.12f, .14f, .15f), 16)
                .GetComponentInChildren<TMP_Text>().text = "닫기 ×";

            Box(panel.transform, "Facility Portrait Frame", 24, 82, 602, 180, PanelSoft);
            facilityImage = Box(panel.transform, "Facility Portrait", 40, 100, 160, 142, new Color(.12f, .13f, .13f));
            facilityImage.preserveAspect = true;
            upgradeTitle = Text(panel.transform, "", 220, 108, 380, 44, 27, Color.white);
            upgradeTitle.alignment = TextAlignmentOptions.Left;
            upgradeLevel = Text(panel.transform, "", 220, 158, 380, 28, 17, Accent);
            upgradeLevel.alignment = TextAlignmentOptions.Left;
            useFacilityButton = Button(panel.transform, "Use Facility", 220, 204, 380, 42, UseSelectedFacility, Accent, 17);

            Text(panel.transform, "강화 진행도", 24, 282, 240, 30, 17, Muted);
            Box(panel.transform, "Progress Track", 24, 320, 602, 22, new Color(.12f, .14f, .14f));
            progressFill = Box(panel.transform, "Progress Fill", 24, 320, 0, 22, Accent);
            progressText = Text(panel.transform, "", 24, 348, 602, 34, 20, Color.white);
            timerText = Text(panel.transform, "", 356, 284, 270, 28, 15, Muted);
            timerText.alignment = TextAlignmentOptions.Right;

            Text(panel.transform, "현재 효과", 24, 392, 270, 28, 17, Muted);
            Box(panel.transform, "Current Effect", 24, 428, 290, 120, PanelSoft);
            currentEffectText = Text(panel.transform, "", 40, 446, 258, 84, 18, Color.white);
            currentEffectText.alignment = TextAlignmentOptions.MidlineLeft;

            Text(panel.transform, "다음 단계", 336, 392, 270, 28, 17, Accent);
            Box(panel.transform, "Next Effect", 336, 428, 290, 120, new Color(.13f, .125f, .09f, .94f));
            nextEffectText = Text(panel.transform, "", 352, 446, 258, 84, 18, Color.white);
            nextEffectText.alignment = TextAlignmentOptions.MidlineLeft;

            Text(panel.transform, "필요 재료", 24, 574, 240, 30, 17, Muted);
            BuildCostRow(panel.transform, "Coin Cost", "C", "코인", 24, 612, new Color(.75f, .58f, .18f), out coinCostText);
            BuildCostRow(panel.transform, "Action Cost", "AP", "행동 포인트", 220, 612, new Color(.23f, .48f, .53f), out actionCostText);
            BuildCostRow(panel.transform, "Material Cost", "M", "강화 재료", 416, 612, new Color(.48f, .38f, .25f), out materialCostText);

            upgradeButton = Button(panel.transform, "Upgrade Button", 220, 738, 406, 58, UpgradeSelected, Accent, 20);
            upgradeButton.GetComponentInChildren<TMP_Text>().text = "업그레이드";
            Button(panel.transform, "Back Button", 24, 738, 180, 58, CloseUpgrade, new Color(.13f, .15f, .155f), 17)
                .GetComponentInChildren<TMP_Text>().text = "← 부지 내부로 돌아가기";

            upgradeOverlay.SetActive(false);
        }

        private void BuildCostRow(Transform parent, string objectName, string symbol, string label, float x, float y, Color iconColor, out TMP_Text value)
        {
            var row = Box(parent, objectName, x, y, 184, 100, PanelSoft).gameObject;
            var icon = Box(row.transform, "Icon", 12, 20, 46, 46, iconColor);
            icon.sprite = circleSprite;
            var mark = Text(icon.transform, symbol, 0, 2, 46, 42, symbol.Length > 1 ? 13 : 18, Color.white);
            mark.alignment = TextAlignmentOptions.Center;
            Text(row.transform, label, 68, 14, 106, 28, 13, Muted);
            value = Text(row.transform, "", 68, 44, 106, 34, 17, Color.white);
        }

        private void ExitHideOut()
        {
            if (!HideOutTravel.CanReturn)
            {
                player.SetMessage("직접 실행 테스트입니다. 플레이 구간의 HideOutEntrance로 진입하면 원래 위치로 돌아갑니다.");
                return;
            }
            player.StartCoroutine(HideOutTravel.Exit(player, gameObject.scene));
        }

        private void OpenStoragePanel()
        {
            selected = Facility.Storage;
            hasSelection = true;
            dummyTesting = false;
            dummyPanel.SetActive(false);
            upgradeOverlay.SetActive(false);
            HideTooltip();
            selectedInventorySlot = selectedStorageSlot = -1;
            inventoryPage = storagePage = 0;
            storagePanel.SetActive(true);
            player.SetMessage("창고를 열었습니다. 이동할 아이템을 선택하세요.");
            Refresh();
        }

        private void CloseStoragePanel()
        {
            ReturnToOverview();
        }

        private void HideStoragePanelSilently()
        {
            if (storagePanel != null) storagePanel.SetActive(false);
            selectedInventorySlot = selectedStorageSlot = -1;
        }

        private void SelectInventorySlot(int row)
        {
            int index = inventoryPage * StorageRowsPerPage + row;
            var slot = player.Inventory.GetSlot(index);
            selectedInventorySlot = slot != null && !slot.IsEmpty ? index : -1;
            selectedStorageSlot = -1;
            RefreshStoragePanel();
        }

        private void SelectStorageSlot(int row)
        {
            int index = storagePage * StorageRowsPerPage + row;
            var slot = index >= 0 && index < player.State.storage.Count ? player.State.storage[index] : null;
            selectedStorageSlot = slot != null && !slot.Empty ? index : -1;
            selectedInventorySlot = -1;
            RefreshStoragePanel();
        }

        private void ChangeInventoryPage(int direction)
        {
            inventoryPage = Mathf.Clamp(inventoryPage + direction, 0, PageCount(player.Inventory.Capacity) - 1);
            selectedInventorySlot = -1;
            RefreshStoragePanel();
        }

        private void ChangeStoragePage(int direction)
        {
            storagePage = Mathf.Clamp(storagePage + direction, 0, PageCount(player.State.storage.Count) - 1);
            selectedStorageSlot = -1;
            RefreshStoragePanel();
        }

        private void DepositSelected()
        {
            if (selectedInventorySlot < 0) { player.SetMessage("창고에 넣을 아이템을 선택하세요."); return; }
            int index = selectedInventorySlot;
            player.Transfer(true, index);
            var slot = player.Inventory.GetSlot(index);
            if (slot == null || slot.IsEmpty) selectedInventorySlot = -1;
            Refresh();
        }

        private void WithdrawSelected()
        {
            if (selectedStorageSlot < 0) { player.SetMessage("꺼낼 창고 아이템을 선택하세요."); return; }
            int index = selectedStorageSlot;
            player.Transfer(false, index);
            var slot = index < player.State.storage.Count ? player.State.storage[index] : null;
            if (slot == null || slot.Empty) selectedStorageSlot = -1;
            Refresh();
        }

        private void SortStorage()
        {
            selectedStorageSlot = -1;
            player.SortStorage();
            Refresh();
        }

        private void DepositExistingTypes()
        {
            selectedInventorySlot = selectedStorageSlot = -1;
            player.DepositExisting();
            Refresh();
        }

        private void UpgradeStorage()
        {
            int previous = player.State.levels[(int)Facility.Storage];
            player.UpgradeStorage();
            if (player.State.levels[(int)Facility.Storage] > previous)
                player.SetMessage($"창고 Lv. {player.State.levels[(int)Facility.Storage]} · 인벤토리 +2 · 창고 +5");
            Refresh();
        }

        private void SelectFacility(Facility facility)
        {
            if (editorPreview) { if (hasSelection && selected == facility) HideEditorDetails(); else ShowEditorSample(facility); return; }
            if (cameraController == null || cameraController.IsMoving || cameraController.ShouldSuppressFacilityClick
                || dummyTesting && selected != facility) return;
            if (hasSelection && selected == facility) { ReturnToOverview(); return; }
            if (!cameraAnchors.TryGetValue(facility, out Transform anchor))
            {
                player.SetMessage("시설 카메라 포커스가 등록되지 않았습니다.");
                return;
            }
            var focus = settings.topView.Find(facility);
            if (focus == null)
            {
                player.SetMessage("시설 카메라 설정이 없습니다.");
                return;
            }

            selected = facility;
            hasSelection = true;
            exhibitionPanel.SetActive(false);
            HideStoragePanelSilently();
            HideDummyPanelSilently();
            upgradeOverlay.SetActive(false);
            HideTooltip();
            roomNameText.text = DisplayName(facility);
            player.SetMessage($"{DisplayName(facility)} 구역으로 이동합니다.");
            RefreshHotspots();
            RefreshFacilityInput();

            if (!cameraController.TryFocus(anchor, focus.orthographicSize, () => OpenSelectedFacility(facility)))
            {
                hasSelection = false;
                roomNameText.text = "전체 하이드아웃";
                Refresh();
            }
            RefreshFacilityInput();
        }

        private void OpenSelectedFacility(Facility facility)
        {
            if (!hasSelection || selected != facility) return;
            if (HideOutModel.IsExhibition(facility)) { ShowExhibition(facility); RefreshFacilityInput(); return; }
            switch (facility)
            {
                case Facility.Storage: OpenStoragePanel(); break;
                case Facility.Dummy: OpenDummyPanel(); break;
                default: OpenUpgrade(facility); break;
            }
            RefreshFacilityInput();
        }

        private void UseSelectedFacility()
        {
            if (editorPreview || HideOutModel.IsExhibition(selected) || IsLocked(selected)) return;
            if (selected == Facility.Bed) player.Save();
            else if (selected == Facility.Clinic) player.Heal();
            else if (selected == Facility.Potions) player.ReceivePotions();
            Refresh();
        }

        public void ShowEditorSample(Facility facility)
        {
            if (!editorPreview) return;
            selected = facility; hasSelection = true;
            exhibitionPanel.SetActive(false);
            if (HideOutModel.IsExhibition(facility))
            { ShowExhibition(facility); RefreshFacilityLevels(); RefreshHotspots(); return; }
            storagePanel.SetActive(facility == Facility.Storage);
            dummyPanel.SetActive(facility == Facility.Dummy);
            upgradeOverlay.SetActive(facility != Facility.Storage && facility != Facility.Dummy);
            if (upgradeOverlay.activeSelf) RefreshUpgrade();
            if (facility == Facility.Storage)
            {
                inventoryCapacityText.text = "샘플 0 / 15"; storageCapacityText.text = "샘플 0 / 10";
                inventoryPageText.text = storagePageText.text = "1 / 1";
            }
            if (facility == Facility.Dummy)
            {
                dummyLevelText.text = "미리보기 Lv. 0"; dummyWeaponText.text = "샘플 무기";
                dummyDamageText.text = "10"; dummyBonusText.text = "+0"; dummyLastDamageText.text = "—";
            }
            RefreshFacilityLevels(); RefreshHotspots();
        }

        public void HideEditorDetails()
        {
            if (!editorPreview) return;
            hasSelection = false;
            exhibitionPanel.SetActive(false);
            storagePanel.SetActive(false); dummyPanel.SetActive(false); upgradeOverlay.SetActive(false);
            RefreshFacilityLevels(); RefreshHotspots();
        }

#if UNITY_EDITOR
        public void BuildEditorPreview(HideOutSettings source)
        {
            editorPreview = true; settings = source; enabled = false;
            player = gameObject.AddComponent<HideOutPlayerBridge>(); player.enabled = false;
            player.InitializeEditorSample(source);
            var cameraObject = new GameObject("Preview Camera", typeof(Camera));
            cameraObject.transform.SetParent(transform, false);
            hideoutCamera = cameraObject.GetComponent<Camera>(); hideoutCamera.depth = 100;
            BuildUI();
            cameraController.enabled = false;
            foreach (var canvas in GetComponentsInChildren<Canvas>(true))
            {
                var group = canvas.gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = false;
            }
            Canvas.ForceUpdateCanvases(); HideEditorDetails(); LateUpdate();
            resourceValues[0].text = "10/10"; resourceValues[1].text = "100/100";
            resourceValues[2].text = "250"; resourceValues[3].text = "10";
        }
#endif

        private void ReturnToOverview()
        {
            if (cameraController == null || cameraController.IsMoving) return;
            dummyTesting = false;
            exhibitionPanel.SetActive(false);
            HideStoragePanelSilently();
            HideDummyPanelSilently();
            upgradeOverlay.SetActive(false);
            hasSelection = false;
            selectedInventorySlot = selectedStorageSlot = -1;
            HideTooltip();
            roomNameText.text = "전체 하이드아웃";
            player.SetMessage("전체 하이드아웃으로 돌아갑니다.");
            cameraController.TryReturnToOverview(() =>
            {
                player.SetMessage("시설을 선택하세요.");
                Refresh();
            });
            Refresh();
        }

        private void OnDummySelected()
        {
            if (cameraController != null && (cameraController.IsMoving || cameraController.ShouldSuppressFacilityClick)) return;
            if (dummyTesting) AttackDummy();
            else if (hasSelection && selected == Facility.Dummy) OpenDummyPanel();
            else SelectFacility(Facility.Dummy);
        }

        private void OpenDummyPanel()
        {
            HideStoragePanelSilently();
            selected = Facility.Dummy;
            hasSelection = true;
            HideTooltip();
            upgradeOverlay.SetActive(false);
            dummyPanel.SetActive(true);
            player.SetMessage("허수아비를 선택했습니다.");
            Refresh();
        }

        private void CloseDummyPanel()
        {
            ReturnToOverview();
        }

        private void HideDummyPanelSilently()
        {
            dummyTesting = false;
            if (dummyPanel != null) dummyPanel.SetActive(false);
        }

        private void StartOrAttackDummyTest()
        {
            if (player.EquippedWeapon == null)
            {
                dummyTesting = false;
                player.SetMessage("장착된 무기가 없습니다.");
                Refresh();
                return;
            }
            if (player.Melee == null)
            {
                dummyTesting = false;
                player.SetMessage("기존 플레이어에 PlayerMeleeAttack이 연결되어 있지 않습니다.");
                Refresh();
                return;
            }
            if (!dummyTesting)
            {
                dummyTesting = true;
                player.SetMessage("시험 시작 · 허수아비 또는 공격 실행 버튼을 누르세요.");
                Refresh();
                return;
            }
            AttackDummy();
        }

        private void AttackDummy()
        {
            if (!dummyTesting) return;
            dummy.Test(player);
            Refresh();
        }

        private void OnDummyHit()
        {
            dummyDamagePopup.text = $"{dummy.LastDamage}";
            dummyDamagePopup.gameObject.SetActive(true);
            dummyDamagePopupUntil = Time.unscaledTime + .9f;
            Refresh();
        }

        private void UpgradeDummy()
        {
            int previousLevel = player.State.levels[(int)Facility.Dummy];
            int previousBonus = player.EquippedWeaponDummyBonus;
            player.UpgradeDummy();
            if (player.State.levels[(int)Facility.Dummy] > previousLevel
                && player.EquippedWeaponDummyBonus == previousBonus + 1)
                player.SetMessage($"허수아비 Lv. {player.State.levels[(int)Facility.Dummy]} · 현재 무기 데미지 +{player.EquippedWeaponDummyBonus}");
            Refresh();
        }

        private void OpenUpgrade(Facility facility)
        {
            if (HideOutModel.IsExhibition(facility)) { ShowExhibition(facility); return; }
            HideStoragePanelSilently();
            HideDummyPanelSilently();
            selected = facility;
            hasSelection = true;
            HideTooltip();
            upgradeOverlay.SetActive(true);
            Refresh();
        }

        private void CloseUpgrade()
        {
            ReturnToOverview();
        }

        private void UpgradeSelected()
        {
            if (editorPreview || HideOutModel.IsExhibition(selected)) return;
            int previous = player.State.levels[(int)selected];
            player.Upgrade(selected);
            Refresh();
            if (player.State.levels[(int)selected] > previous) player.SetMessage($"{DisplayName(selected)} 업그레이드 완료");
        }

        private void ShowFacilityTooltip(Facility facility, float x, float y)
        {
            Rect(tooltip.GetComponent<RectTransform>(), 661, 74, 278, 84);
            bool locked = IsLocked(facility);
            tooltipText.text = HideOutModel.IsExhibition(facility) ? $"{DisplayName(facility)}\n추후 구현 · 공간 둘러보기" : $"{DisplayName(facility)}\nLv. {player.State.levels[(int)facility]}  ·  {(locked ? "이용 잠금" : "선택 가능")}";
            tooltip.SetActive(true);
        }

        private void ShowLockedTooltip(float x, float y)
        {
            Rect(tooltip.GetComponent<RectTransform>(), 661, 74, 278, 84);
            tooltipText.text = "미해금 구역\n아직 해금되지 않은 시설입니다";
            tooltip.SetActive(true);
        }

        private void HideTooltip()
        {
            if (tooltip != null) tooltip.SetActive(false);
        }

        private bool IsLocked(Facility facility)
        {
            var presentation = settings.FindPresentation(facility);
            return presentation != null && presentation.prototypeLocked || HideOutModel.Locked(player.State, facility);
        }

        private string DisplayName(Facility facility)
        {
            string configured = settings.FindPresentation(facility)?.displayName;
            if (!string.IsNullOrWhiteSpace(configured)) return configured;
            return facility switch
            {
                Facility.Clinic => "치료 부스",
                Facility.Dummy => "허수아비",
                Facility.Storage => "창고",
                Facility.Bed => "침대",
                Facility.Potions => "물약 보관대",
                Facility.Gym => "체력단련실",
                Facility.Dining => "식탁",
                Facility.Workshop => "무기제작실",
                _ => facility.ToString()
            };
        }

        private int MaxLevel(Facility facility)
        {
            foreach (var definition in settings.rules.facilities ?? Array.Empty<FacilityDefinition>())
                if (definition != null && definition.facility == facility) return definition.upgrades?.Length ?? 0;
            return 0;
        }

        private string Effect(Facility facility, int level)
        {
            var rules = settings.rules;
            return facility switch
            {
                Facility.Clinic => $"회복량 {Math.Ceiling(rules.baseHealing * (1d + level * (double)rules.clinicPercentPerLevel))} HP\n회복 성능 +{level * rules.clinicPercentPerLevel:P0}",
                Facility.Storage => $"가방 슬롯 +{level * rules.inventorySlotsPerLevel}\n창고 슬롯 {rules.baseStorageSlots + level * rules.storageSlotsPerLevel}",
                Facility.Bed => $"최대 HP +{level * rules.bedHPPerLevel}",
                Facility.Dummy => $"시설 단계 Lv. {level}\n강화 시 현재 무기 +1",
                Facility.Potions => $"물약 {rules.potionCount}개\n대기 {Math.Max(rules.minimumPotionCooldown, rules.potionCooldown - level * rules.potionReduction):0}초",
                Facility.Gym => $"최대 MP +{level * rules.gymPercentPerLevel:P0}",
                _ => "프로토타입 미구현"
            };
        }

        private void Refresh()
        {
            if (resourceValues[0] == null || player == null || !player.Initialized) return;
            var state = player.State;
            resourceValues[0].text = $"{player.Health.CurrentHealth}/{player.Health.MaxHealth}";
            resourceValues[1].text = $"{state.mana}/{player.MaxMana}";
            resourceValues[2].text = state.coins.ToString("N0");
            resourceValues[3].text = state.actionPoints.ToString("N0");
            resourceBars[0].rectTransform.sizeDelta = new Vector2(81f * Mathf.Clamp01((float)player.Health.CurrentHealth / Math.Max(1, player.Health.MaxHealth)), 3f);
            resourceBars[1].rectTransform.sizeDelta = new Vector2(81f * Mathf.Clamp01((float)state.mana / Math.Max(1, player.MaxMana)), 3f);
            feedbackText.text = player.Message;
            RefreshStorageAppearance();
            RefreshFacilityLevels();
            RefreshHotspots();
            RefreshFacilityInput();
            if (storagePanel.activeSelf) RefreshStoragePanel();
            if (dummyPanel.activeSelf) RefreshDummyPanel();
            if (upgradeOverlay.activeSelf) RefreshUpgrade();
        }

        private void RefreshHotspots()
        {
            foreach (var pair in hotspotRings)
            {
                bool active = hasSelection && pair.Key == selected;
                float alpha = active && pair.Key == Facility.Dummy && dummyTesting ? .58f : .38f;
                pair.Value.color = active ? new Color(Accent.r, Accent.g, Accent.b, alpha) : AccentSoft;
                pair.Value.rectTransform.sizeDelta = active ? new Vector2(116, 116) : new Vector2(108, 108);
                if (facilityButtons.TryGetValue(pair.Key, out Button button))
                {
                    var outline = button.GetComponent<Outline>();
                    if (outline != null)
                        outline.effectColor = active
                            ? new Color(1f, .68f, .22f, 1f)
                            : new Color(.76f, .77f, .72f, .7f);
                }
            }
        }

        private void RefreshFacilityLevels()
        {
            foreach (var pair in facilityLevelLabels)
                pair.Value.text = HideOutModel.IsExhibition(pair.Key) ? "추후 구현" : IsLocked(pair.Key) ? "잠금 · 셀레나 영입 필요" : $"Lv. {player.State.levels[(int)pair.Key]}";
            foreach (var pair in shortcutLabels)
            {
                pair.Value.text = $"{DisplayName(pair.Key)}\n" + (HideOutModel.IsExhibition(pair.Key) ? "추후 구현" : IsLocked(pair.Key) ? "잠금" : $"Lv. {player.State.levels[(int)pair.Key]}");
                shortcutButtons[pair.Key].GetComponent<Image>().color = hasSelection && selected == pair.Key ? AccentSoft : PanelSoft;
            }
            isometricEnvironment.RefreshFacilityVisuals(player.State.levels);
            isometricEnvironment.ApplyArt(settings, player.State.levels);
        }

        private void RefreshFacilityInput()
        {
            bool moving = cameraController != null && cameraController.IsMoving;
            foreach (var pair in facilityButtons) pair.Value.interactable = !moving && (!dummyTesting || selected == pair.Key);
            foreach (var pair in shortcutButtons) pair.Value.interactable = !moving && (!dummyTesting || selected == pair.Key);
            if (dummyTargetButton != null)
                dummyTargetButton.interactable = !moving && (!hasSelection || selected == Facility.Dummy);
        }

        private void RefreshStorageAppearance()
        {
            int tier = HideOutModel.Tier(player.State.levels[(int)Facility.Storage]);
            for (int i = 0; i < storageTierVisuals.Length; i++)
                if (storageTierVisuals[i] != null) storageTierVisuals[i].SetActive(i <= tier);
        }

        private void RefreshStoragePanel()
        {
            int inventoryCapacity = player.Inventory.Capacity;
            int storageCapacity = player.State.storage.Count;
            int inventoryPages = PageCount(inventoryCapacity);
            int storagePages = PageCount(storageCapacity);
            inventoryPage = Mathf.Clamp(inventoryPage, 0, inventoryPages - 1);
            storagePage = Mathf.Clamp(storagePage, 0, storagePages - 1);

            int usedInventory = 0;
            for (int i = 0; i < inventoryCapacity; i++)
            {
                var slot = player.Inventory.GetSlot(i);
                if (slot != null && !slot.IsEmpty) usedInventory++;
            }
            int usedStorage = 0;
            foreach (var slot in player.State.storage) if (slot != null && !slot.Empty) usedStorage++;
            inventoryCapacityText.text = $"사용 {usedInventory} / {inventoryCapacity}";
            storageCapacityText.text = $"사용 {usedStorage} / {storageCapacity}";
            inventoryPageText.text = $"{inventoryPage + 1} / {inventoryPages}";
            storagePageText.text = $"{storagePage + 1} / {storagePages}";
            inventoryPreviousButton.interactable = inventoryPage > 0;
            inventoryNextButton.interactable = inventoryPage + 1 < inventoryPages;
            storagePreviousButton.interactable = storagePage > 0;
            storageNextButton.interactable = storagePage + 1 < storagePages;

            for (int row = 0; row < StorageRowsPerPage; row++)
            {
                int inventoryIndex = inventoryPage * StorageRowsPerPage + row;
                var inventorySlot = inventoryIndex < inventoryCapacity ? player.Inventory.GetSlot(inventoryIndex) : null;
                RefreshStorageSlot(inventorySlotViews[row], inventoryIndex < inventoryCapacity ? inventoryIndex : -1, inventorySlot?.Item,
                    inventorySlot != null ? inventorySlot.Amount : 0, inventoryIndex == selectedInventorySlot);

                int storageIndex = storagePage * StorageRowsPerPage + row;
                ItemStack storageSlot = storageIndex < storageCapacity ? player.State.storage[storageIndex] : null;
                var storageItem = storageSlot != null && !storageSlot.Empty ? settings.Find(storageSlot.id) : null;
                RefreshStorageSlot(storageSlotViews[row], storageIndex < storageCapacity ? storageIndex : -1, storageItem,
                    storageSlot != null ? storageSlot.count : 0, storageIndex == selectedStorageSlot);
            }

            var selectedBagSlot = selectedInventorySlot >= 0 ? player.Inventory.GetSlot(selectedInventorySlot) : null;
            depositButton.interactable = selectedBagSlot != null && !selectedBagSlot.IsEmpty
                && HideOutModel.Space(player.State.storage, selectedBagSlot.Item.ItemId, settings) > 0;
            ItemStack selectedStoreSlot = selectedStorageSlot >= 0 && selectedStorageSlot < storageCapacity
                ? player.State.storage[selectedStorageSlot] : null;
            var selectedStoreItem = selectedStoreSlot != null && !selectedStoreSlot.Empty
                ? settings.Find(selectedStoreSlot.id) : null;
            withdrawButton.interactable = selectedStoreItem != null && LiveInventorySpace(selectedStoreItem) > 0;

            int level = player.State.levels[(int)Facility.Storage];
            int maximum = MaxLevel(Facility.Storage);
            var cost = HideOutModel.Next(player.State, settings.rules, Facility.Storage);
            var costLines = new List<string> { maximum > 0 ? $"Lv. {level} / {maximum}" : $"Lv. {level}" };
            if (cost == null) costLines.Add("최대 강화 단계");
            else
            {
                costLines.Add(CostLine("코인", player.State.coins, cost.coins));
                costLines.Add(CostLine("AP", player.State.actionPoints, cost.actionPoints));
                foreach (var material in cost.materials ?? Array.Empty<MaterialCost>())
                {
                    if (material == null) continue;
                    costLines.Add(CostLine(settings.Name(material.itemId),
                        HideOutModel.Count(player.State.inventory, material.itemId), material.amount));
                }
            }
            storageUpgradeCostText.text = string.Join("\n", costLines);
            storageUpgradeButton.interactable = cost != null;
            storageUpgradeButton.GetComponentInChildren<TMP_Text>().text = cost == null ? "최대 단계" : "창고 강화";
        }

        private void RefreshStorageSlot(StorageSlotView view, int index, InventoryItemDefinition item, int amount, bool selectedSlot)
        {
            bool validIndex = index >= 0;
            view.background.color = selectedSlot
                ? new Color(Accent.r, Accent.g, Accent.b, .34f)
                : new Color(.075f, .085f, .09f, .96f);
            if (item == null)
            {
                view.icon.gameObject.SetActive(false);
                view.name.text = validIndex ? $"{index + 1:00}   빈 슬롯" : "";
                view.name.color = new Color(.42f, .45f, .46f);
                view.amount.text = "";
                return;
            }

            view.icon.gameObject.SetActive(true);
            view.icon.sprite = item.Icon;
            view.icon.color = Color.white;
            view.name.text = $"{index + 1:00}   {item.DisplayName}";
            view.name.color = Color.white;
            view.amount.text = $"{amount} / {item.MaxStack}";
        }

        private int LiveInventorySpace(InventoryItemDefinition item)
        {
            if (item == null) return 0;
            int space = 0;
            for (int i = 0; i < player.Inventory.Capacity; i++)
            {
                var slot = player.Inventory.GetSlot(i);
                if (slot == null || slot.IsEmpty) space += item.MaxStack;
                else if (slot.Item != null && slot.Item.ItemId == item.ItemId)
                    space += Mathf.Max(0, item.MaxStack - slot.Amount);
            }
            return space;
        }

        private static int PageCount(int capacity) => Mathf.Max(1, Mathf.CeilToInt(capacity / (float)StorageRowsPerPage));

        private void RefreshDummyPanel()
        {
            var state = player.State;
            var weapon = player.EquippedWeapon;
            var cost = HideOutModel.Next(state, settings.rules, Facility.Dummy);
            int level = state.levels[(int)Facility.Dummy];

            dummyLevelText.text = $"강화 단계  Lv. {level}";
            dummyWeaponText.text = weapon != null ? weapon.DisplayName : "장착 없음";
            dummyDamageText.text = weapon != null && player.Melee != null ? player.EquippedWeaponFinalDamage.ToString() : "-";
            dummyBonusText.text = weapon != null ? $"+{player.EquippedWeaponDummyBonus}" : "+0";
            dummyLastDamageText.text = dummy.Hits > 0 ? dummy.LastDamage.ToString() : "-";

            var lines = new List<string>();
            if (cost == null) lines.Add("최대 강화 단계");
            else
            {
                lines.Add(CostLine("코인", state.coins, cost.coins));
                lines.Add(CostLine("AP", state.actionPoints, cost.actionPoints));
                foreach (var material in cost.materials ?? Array.Empty<MaterialCost>())
                {
                    if (material == null) continue;
                    int owned = HideOutModel.Count(state.inventory, material.itemId);
                    lines.Add(CostLine(settings.Name(material.itemId), owned, material.amount));
                }
            }
            dummyCostText.text = string.Join("\n", lines);

            dummyTestButton.GetComponentInChildren<TMP_Text>().text = dummyTesting ? "공격 실행" : "시험 시작";
            dummyUpgradeButton.interactable = cost != null;
            dummyUpgradeButton.GetComponentInChildren<TMP_Text>().text = cost == null ? "최대 단계" : "업그레이드";
        }

        private static string CostLine(string label, int owned, int required)
        {
            string value = $"{label}  {owned:N0} / {required:N0}";
            return owned >= required ? value : $"<color=#F07878>{value}</color>";
        }

        private void RefreshUpgrade()
        {
            var state = player.State;
            int level = state.levels[(int)selected];
            int maximum = MaxLevel(selected);
            var cost = HideOutModel.Next(state, settings.rules, selected);
            var presentation = settings.FindPresentation(selected);
            useFacilityButton.gameObject.SetActive(selected == Facility.Bed || selected == Facility.Clinic || selected == Facility.Potions);
            useFacilityButton.GetComponentInChildren<TMP_Text>().text = selected == Facility.Bed ? "진행 상황 저장" : selected == Facility.Clinic ? "치료 이용" : "물약 수령";
            bool missingPotion = selected == Facility.Potions && settings.Find(settings.rules.potionId) == null;
            useFacilityButton.interactable = !IsLocked(selected) && !missingPotion && (selected != Facility.Potions || player.Now >= state.potionsReadyAt);

            upgradeTitle.text = DisplayName(selected);
            upgradeLevel.text = $"현재 강화 단계  Lv. {level}";
            progressText.text = maximum > 0 && level < maximum ? $"Lv. {level}   →   Lv. {level + 1}" : $"Lv. {level}   ·   최대 단계";
            progressFill.rectTransform.sizeDelta = new Vector2(602f * (maximum > 0 ? Mathf.Clamp01(level / (float)maximum) : 0f), 22f);
            timerText.text = "강화 시간  ·  즉시 완료";
            if (selected == Facility.Potions) timerText.text = missingPotion ? "물약 정의 설정 누락" : IsLocked(selected) ? "셀레나 영입 필요" : $"수령 대기 {Math.Max(0, state.potionsReadyAt - player.Now):0}초";
            facilityImage.sprite = presentation?.facilityImage ?? AppearanceFor(level);
            facilityImage.color = facilityImage.sprite != null ? Color.white : new Color(.13f, .145f, .145f);

            currentEffectText.text = !string.IsNullOrWhiteSpace(presentation?.currentEffectOverride)
                ? presentation.currentEffectOverride : Effect(selected, level);
            nextEffectText.text = cost == null ? "최대 단계입니다" : !string.IsNullOrWhiteSpace(presentation?.nextEffectOverride)
                ? presentation.nextEffectOverride : Effect(selected, level + 1);

            if (cost == null)
            {
                coinCostText.text = "-";
                actionCostText.text = "-";
                materialCostText.text = "-";
                upgradeButton.interactable = false;
                upgradeButton.GetComponentInChildren<TMP_Text>().text = "최대 강화 단계";
                return;
            }

            coinCostText.text = $"{state.coins:N0} / {cost.coins:N0}";
            actionCostText.text = $"{state.actionPoints} / {cost.actionPoints}";
            if (cost.materials != null && cost.materials.Length > 0 && cost.materials[0] != null)
            {
                var material = cost.materials[0];
                materialCostText.text = $"{HideOutModel.Count(state.inventory, material.itemId)} / {material.amount}";
            }
            else materialCostText.text = "필요 없음";

            upgradeButton.interactable = !IsLocked(selected);
            upgradeButton.GetComponentInChildren<TMP_Text>().text = "업그레이드";
        }

        private Sprite AppearanceFor(int level)
        {
            if (settings.appearanceSprites == null || settings.appearanceSprites.Length == 0) return null;
            int tier = Mathf.Clamp(HideOutModel.Tier(level), 0, settings.appearanceSprites.Length - 1);
            return settings.appearanceSprites[tier];
        }

        private void AddHover(GameObject target, UnityEngine.Events.UnityAction enter, UnityEngine.Events.UnityAction exit)
        {
            var trigger = target.AddComponent<EventTrigger>();
            var enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enterEntry.callback.AddListener(_ => enter());
            trigger.triggers.Add(enterEntry);
            var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exitEntry.callback.AddListener(_ => exit());
            trigger.triggers.Add(exitEntry);
        }

        private static Sprite CreateCircleSprite()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "HideOut Runtime Circle",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[size * size];
            float center = (size - 1) * .5f;
            float radius = center - 1f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                byte alpha = distance <= radius ? (byte)255 : (byte)0;
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100f);
        }

        private static void Stretch(RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }

        private static void Rect(RectTransform rectTransform, float x, float y, float width, float height)
        {
            rectTransform.anchorMin = rectTransform.anchorMax = rectTransform.pivot = new Vector2(0, 1);
            rectTransform.anchoredPosition = new Vector2(x, -y);
            rectTransform.sizeDelta = new Vector2(width, height);
        }

        private static Image Box(Transform parent, string name, float x, float y, float width, float height, Color color)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            var image = gameObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            Rect(image.rectTransform, x, y, width, height);
            return image;
        }

        private TMP_Text Text(Transform parent, string value, float x, float y, float width, float height, int size, Color color)
        {
            var gameObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            gameObject.transform.SetParent(parent, false);
            var text = gameObject.GetComponent<TextMeshProUGUI>();
            text.font = settings.font;
            text.fontSharedMaterial = settings.font.material;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            Rect(text.rectTransform, x, y, width, height);
            return text;
        }

        private Button Button(Transform parent, string name, float x, float y, float width, float height,
            UnityEngine.Events.UnityAction action, Color color, int labelSize)
        {
            var image = Box(parent, name, x, y, width, height, color);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            if (labelSize > 0)
            {
                var label = Text(image.transform, name, 8, 0, width - 16, height, labelSize, Color.white);
                label.alignment = TextAlignmentOptions.Center;
            }
            return button;
        }
    }
}
