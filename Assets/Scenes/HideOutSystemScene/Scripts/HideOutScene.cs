using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BehindTheGear.HideOut
{
    // Runtime-only prototype view. Game state, upgrades and saving remain owned by HideOutPlayerBridge.
    public sealed class HideOutScene : MonoBehaviour
    {
        private const float ReferenceWidth = 1600f;
        private const float ReferenceHeight = 900f;

        public HideOutSettings settings;

        private HideOutPlayerBridge player;
        private Transform ui;
        private GameObject baseView;
        private GameObject upgradeOverlay;
        private GameObject tooltip;
        private TMP_Text tooltipText;
        private TMP_Text statusText;
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
        private Sprite circleSprite;
        private Facility selected = Facility.Storage;
        private bool hasSelection;
        private float nextRefresh;
        private Rigidbody2D body;
        private bool bodyWasSimulated;
        private bool released;
        private readonly List<Behaviour> suspendedControls = new();
        private readonly Dictionary<Facility, Image> hotspotRings = new();

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
            if (player != null) player.Changed -= Refresh;
            ReleasePlayer();
            if (circleSprite != null)
            {
                if (circleSprite.texture != null) Destroy(circleSprite.texture);
                Destroy(circleSprite);
            }
        }

        private void Update()
        {
            if (player == null || !player.Initialized || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .25f;
            Refresh();
        }

        private void BuildUI()
        {
            EnsureEventSystem();
            circleSprite = CreateCircleSprite();

            var canvasObject = new GameObject("HideOut UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            ui = canvasObject.transform;
            ui.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;

            baseView = Box(ui, "HideOut Base View", 0, 0, ReferenceWidth, ReferenceHeight, Background).gameObject;
            Stretch(baseView.GetComponent<RectTransform>());
            BuildBunker(baseView.transform);
            BuildBaseHud(baseView.transform);
            BuildUpgradeOverlay();
        }

        private void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var events = new GameObject("HideOut EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(transform, false);
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        private void BuildBunker(Transform parent)
        {
            Box(parent, "Concrete Wall", 0, 0, ReferenceWidth, 710, Wall);
            Box(parent, "Ceiling Shadow", 0, 0, ReferenceWidth, 118, new Color(.018f, .023f, .027f));
            Box(parent, "Floor", 0, 710, ReferenceWidth, 190, Floor);
            Box(parent, "Floor Edge", 0, 704, ReferenceWidth, 8, new Color(.16f, .15f, .12f));

            Box(parent, "Main Pipe", 135, 92, 1160, 22, new Color(.13f, .14f, .135f));
            Box(parent, "Pipe Highlight", 135, 92, 1160, 4, new Color(.25f, .24f, .20f));
            for (int i = 0; i < 5; i++) Box(parent, "Wall Seam", 420 + i * 235, 120, 3, 584, new Color(.035f, .04f, .042f));

            BuildLockedArea(parent);
            BuildDesk(parent);
            BuildMedicalCabinet(parent);

            Text(parent, "임시 2D 공간 구성", 24, 852, 300, 28, 15, new Color(.45f, .48f, .49f));
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

        private void BuildDesk(Transform parent)
        {
            Box(parent, "Desk Back Glow", 480, 404, 500, 292, new Color(.16f, .13f, .08f, .08f));
            Box(parent, "Desk Top", 505, 546, 470, 42, new Color(.26f, .19f, .12f));
            Box(parent, "Desk Front", 530, 588, 420, 122, new Color(.15f, .105f, .07f));
            Box(parent, "Desk Left Leg", 540, 694, 34, 70, new Color(.09f, .065f, .045f));
            Box(parent, "Desk Right Leg", 906, 694, 34, 70, new Color(.09f, .065f, .045f));
            Box(parent, "Book A", 610, 516, 112, 25, new Color(.34f, .17f, .12f));
            Box(parent, "Book B", 626, 489, 98, 25, new Color(.12f, .23f, .25f));
            Box(parent, "Paper", 754, 514, 118, 30, new Color(.57f, .55f, .45f));
            Box(parent, "Lamp Stand", 890, 452, 8, 92, new Color(.26f, .25f, .21f));
            Box(parent, "Lamp Shade", 858, 440, 72, 22, new Color(.45f, .37f, .20f));

            var hotspot = CircularHotspot(parent, "Desk Hotspot", Facility.Storage, 682, 385);
            Box(hotspot.transform, "Book Left", 18, 24, 21, 25, new Color(.94f, .89f, .73f));
            Box(hotspot.transform, "Book Right", 41, 24, 21, 25, new Color(.94f, .89f, .73f));
            Box(hotspot.transform, "Book Spine", 39, 23, 3, 28, Accent);
        }

        private void BuildMedicalCabinet(Transform parent)
        {
            Box(parent, "Cabinet Shadow", 1110, 232, 374, 480, new Color(0, 0, 0, .24f));
            Box(parent, "Medical Cabinet", 1140, 254, 314, 450, new Color(.115f, .13f, .13f));
            Box(parent, "Cabinet Inner", 1160, 278, 274, 398, new Color(.07f, .083f, .085f));
            for (int i = 0; i < 4; i++) Box(parent, "Cabinet Shelf", 1160, 360 + i * 92, 274, 7, new Color(.20f, .21f, .195f));
            Box(parent, "Supply Box", 1190, 305, 92, 50, new Color(.28f, .29f, .24f));
            Box(parent, "Supply Box 2", 1300, 314, 104, 41, new Color(.19f, .25f, .24f));
            Box(parent, "Bottle A", 1205, 392, 30, 58, new Color(.22f, .38f, .36f));
            Box(parent, "Bottle B", 1252, 405, 27, 45, new Color(.38f, .28f, .22f));
            Box(parent, "Medical Case", 1304, 390, 90, 60, new Color(.42f, .42f, .37f));
            Box(parent, "Case Cross V", 1342, 399, 14, 42, new Color(.64f, .18f, .17f));
            Box(parent, "Case Cross H", 1328, 413, 42, 14, new Color(.64f, .18f, .17f));

            var hotspot = CircularHotspot(parent, "Medical Hotspot", Facility.Clinic, 1237, 470);
            Box(hotspot.transform, "Medical Cross V", 33, 18, 14, 44, new Color(.92f, .94f, .90f));
            Box(hotspot.transform, "Medical Cross H", 18, 33, 44, 14, new Color(.92f, .94f, .90f));
        }

        private GameObject CircularHotspot(Transform parent, string name, Facility facility, float x, float y)
        {
            var ring = Box(parent, name + " Glow", x - 10, y - 10, 100, 100, AccentSoft);
            ring.sprite = circleSprite;
            hotspotRings[facility] = ring;

            var button = Button(parent, name, x, y, 80, 80, () => OpenUpgrade(facility), new Color(.12f, .14f, .14f, .96f), 0);
            button.GetComponent<Image>().sprite = circleSprite;
            var outline = button.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(Accent.r, Accent.g, Accent.b, .85f);
            outline.effectDistance = new Vector2(2, -2);
            AddHover(button.gameObject, () => ShowFacilityTooltip(facility, x + 94, y - 2), HideTooltip);
            return button.gameObject;
        }

        private void BuildBaseHud(Transform parent)
        {
            Button(parent, "Exit Button", 24, 24, 126, 50, ExitHideOut, new Color(.10f, .12f, .13f, .94f), 18)
                .GetComponentInChildren<TMP_Text>().text = "← 나가기";
            Text(parent, "HIDEOUT", 176, 23, 250, 42, 26, Color.white);
            Text(parent, "부지 내부", 176, 55, 250, 26, 14, Muted);

            statusText = Text(parent, "", 1050, 26, 520, 38, 16, new Color(.83f, .84f, .80f));
            statusText.alignment = TextAlignmentOptions.Right;
            feedbackText = Text(parent, "", 400, 808, 800, 42, 18, Accent);
            feedbackText.alignment = TextAlignmentOptions.Center;

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
                var reset = Button(parent, "Reset Test Progress", 1160, 832, 194, 42, player.ResetTestProgress,
                    new Color(.18f, .105f, .10f, .90f), 14);
                reset.GetComponentInChildren<TMP_Text>().text = "TEST  초기화";
                var test = Button(parent, "Grant Test Resources", 1378, 832, 194, 42, player.GrantTestResources,
                    new Color(.12f, .14f, .145f, .86f), 14);
                test.GetComponentInChildren<TMP_Text>().text = "TEST  + 강화 자원";
            }
#endif
        }

        private void BuildUpgradeOverlay()
        {
            upgradeOverlay = Box(ui, "Upgrade Overlay", 0, 0, ReferenceWidth, ReferenceHeight, new Color(0, 0, 0, .54f)).gameObject;
            Stretch(upgradeOverlay.GetComponent<RectTransform>());
            var panel = Box(upgradeOverlay.transform, "Upgrade Panel", 190, 92, 1220, 716, Panel).gameObject;
            var panelOutline = panel.AddComponent<Outline>();
            panelOutline.effectColor = new Color(Accent.r, Accent.g, Accent.b, .42f);
            panelOutline.effectDistance = new Vector2(1, -1);

            Text(panel.transform, "시설 업그레이드", 42, 28, 360, 42, 25, Color.white);
            Button(panel.transform, "Close Upgrade", 1092, 24, 86, 44, CloseUpgrade, new Color(.12f, .14f, .15f), 16)
                .GetComponentInChildren<TMP_Text>().text = "닫기 ×";

            Box(panel.transform, "Facility Portrait Frame", 42, 102, 320, 376, PanelSoft);
            facilityImage = Box(panel.transform, "Facility Portrait", 64, 124, 276, 250, new Color(.12f, .13f, .13f));
            facilityImage.preserveAspect = true;
            upgradeTitle = Text(panel.transform, "", 62, 394, 280, 44, 27, Color.white);
            upgradeTitle.alignment = TextAlignmentOptions.Center;
            upgradeLevel = Text(panel.transform, "", 62, 440, 280, 28, 17, Accent);
            upgradeLevel.alignment = TextAlignmentOptions.Center;

            Text(panel.transform, "강화 진행도", 402, 102, 240, 30, 17, Muted);
            Box(panel.transform, "Progress Track", 402, 142, 744, 22, new Color(.12f, .14f, .14f));
            progressFill = Box(panel.transform, "Progress Fill", 402, 142, 0, 22, Accent);
            progressText = Text(panel.transform, "", 402, 172, 744, 34, 20, Color.white);
            timerText = Text(panel.transform, "", 900, 104, 246, 28, 15, Muted);
            timerText.alignment = TextAlignmentOptions.Right;

            Text(panel.transform, "현재 효과", 402, 230, 340, 28, 17, Muted);
            Box(panel.transform, "Current Effect", 402, 268, 350, 126, PanelSoft);
            currentEffectText = Text(panel.transform, "", 424, 288, 306, 86, 19, Color.white);
            currentEffectText.alignment = TextAlignmentOptions.MidlineLeft;

            Text(panel.transform, "다음 단계", 780, 230, 340, 28, 17, Accent);
            Box(panel.transform, "Next Effect", 780, 268, 366, 126, new Color(.13f, .125f, .09f, .94f));
            nextEffectText = Text(panel.transform, "", 802, 288, 322, 86, 19, Color.white);
            nextEffectText.alignment = TextAlignmentOptions.MidlineLeft;

            Text(panel.transform, "필요 재료", 402, 430, 240, 30, 17, Muted);
            BuildCostRow(panel.transform, "Coin Cost", "C", "코인", 402, 474, new Color(.75f, .58f, .18f), out coinCostText);
            BuildCostRow(panel.transform, "Action Cost", "AP", "행동 포인트", 650, 474, new Color(.23f, .48f, .53f), out actionCostText);
            BuildCostRow(panel.transform, "Material Cost", "M", "강화 재료", 898, 474, new Color(.48f, .38f, .25f), out materialCostText);

            upgradeButton = Button(panel.transform, "Upgrade Button", 402, 608, 744, 64, UpgradeSelected, Accent, 21);
            upgradeButton.GetComponentInChildren<TMP_Text>().text = "업그레이드";
            Button(panel.transform, "Back Button", 42, 608, 320, 64, CloseUpgrade, new Color(.13f, .15f, .155f), 19)
                .GetComponentInChildren<TMP_Text>().text = "← 부지 내부로 돌아가기";

            upgradeOverlay.SetActive(false);
        }

        private void BuildCostRow(Transform parent, string objectName, string symbol, string label, float x, float y, Color iconColor, out TMP_Text value)
        {
            var row = Box(parent, objectName, x, y, 226, 100, PanelSoft).gameObject;
            var icon = Box(row.transform, "Icon", 16, 18, 54, 54, iconColor);
            icon.sprite = circleSprite;
            var mark = Text(icon.transform, symbol, 0, 4, 54, 44, symbol.Length > 1 ? 15 : 20, Color.white);
            mark.alignment = TextAlignmentOptions.Center;
            Text(row.transform, label, 84, 14, 126, 28, 14, Muted);
            value = Text(row.transform, "", 84, 44, 126, 34, 20, Color.white);
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

        private void OpenUpgrade(Facility facility)
        {
            selected = facility;
            hasSelection = true;
            if (IsLocked(facility))
            {
                player.SetMessage("아직 해금되지 않은 시설입니다.");
                Refresh();
                return;
            }
            HideTooltip();
            upgradeOverlay.SetActive(true);
            Refresh();
        }

        private void CloseUpgrade()
        {
            upgradeOverlay.SetActive(false);
            RefreshHotspots();
        }

        private void UpgradeSelected()
        {
            int previous = player.State.levels[(int)selected];
            player.Upgrade(selected);
            Refresh();
            if (player.State.levels[(int)selected] > previous) player.SetMessage($"{DisplayName(selected)} 업그레이드 완료");
        }

        private void ShowFacilityTooltip(Facility facility, float x, float y)
        {
            selected = facility;
            hasSelection = true;
            Rect(tooltip.GetComponent<RectTransform>(), Mathf.Min(x, ReferenceWidth - 300), Mathf.Clamp(y, 110, 730), 278, 84);
            bool locked = IsLocked(facility);
            tooltipText.text = $"{DisplayName(facility)}\nLv. {player.State.levels[(int)facility]}  ·  {(locked ? "선택 불가" : "선택 가능")}";
            tooltip.SetActive(true);
            RefreshHotspots();
        }

        private void ShowLockedTooltip(float x, float y)
        {
            Rect(tooltip.GetComponent<RectTransform>(), x, y, 278, 84);
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
                Facility.Storage => "창고 관리 책상",
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
                Facility.Dummy => $"장착 무기 피해 +{level}",
                Facility.Potions => $"물약 {rules.potionCount}개\n대기 {Math.Max(rules.minimumPotionCooldown, rules.potionCooldown - level * rules.potionReduction):0}초",
                Facility.Gym => $"최대 MP +{level * rules.gymPercentPerLevel:P0}",
                _ => "프로토타입 미구현"
            };
        }

        private void Refresh()
        {
            if (statusText == null || player == null || !player.Initialized) return;
            var state = player.State;
            statusText.text = $"HP {player.Health.CurrentHealth}/{player.Health.MaxHealth}    MP {state.mana}/{player.MaxMana}    코인 {state.coins:N0}    AP {state.actionPoints}";
            feedbackText.text = player.Message;
            RefreshHotspots();
            if (upgradeOverlay.activeSelf) RefreshUpgrade();
        }

        private void RefreshHotspots()
        {
            foreach (var pair in hotspotRings)
            {
                bool active = hasSelection && pair.Key == selected;
                pair.Value.color = active ? new Color(Accent.r, Accent.g, Accent.b, .38f) : AccentSoft;
                pair.Value.rectTransform.sizeDelta = active ? new Vector2(106, 106) : new Vector2(100, 100);
            }
        }

        private void RefreshUpgrade()
        {
            var state = player.State;
            int level = state.levels[(int)selected];
            int maximum = MaxLevel(selected);
            var cost = HideOutModel.Next(state, settings.rules, selected);
            var presentation = settings.FindPresentation(selected);

            upgradeTitle.text = DisplayName(selected);
            upgradeLevel.text = $"현재 강화 단계  Lv. {level}";
            progressText.text = maximum > 0 && level < maximum ? $"Lv. {level}   →   Lv. {level + 1}" : $"Lv. {level}   ·   최대 단계";
            progressFill.rectTransform.sizeDelta = new Vector2(744f * (maximum > 0 ? Mathf.Clamp01(level / (float)maximum) : 0f), 22f);
            timerText.text = "강화 시간  ·  즉시 완료";
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
