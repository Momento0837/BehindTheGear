using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BehindTheGear.HideOut
{
    // Bedroom screen entered from the base when the bed is selected (same fade-through travel as TreatmentScene).
    // The player is frozen and hidden with the base's own SuspendPlayer policy; the bedroom backdrop fills the screen,
    // a bed chart on the right offers the disk save (the only save point) and the bed upgrade, and Selena answers
    // each event with one line (rule-based for now; an SLM can replace it via IBedDialogueSource).
    // Save / upgrade stay owned by HideOutPlayerBridge + HideOutModel; this screen only calls them.
    [DefaultExecutionOrder(-75)]
    public sealed class BedRoomScene : MonoBehaviour, IHideOutFacilityRoom
    {
        public const string ScenePath = "Assets/Scenes/HideOutSystemScene/BedScene/BedScene.unity";
        public static bool IsActive { get; private set; }

        public HideOutSettings settings;
        [Tooltip("Existing gameplay rig used only when this scene is played directly.")]
        public GameObject playerPrefab;
        [Tooltip("Where the (hidden, frozen) player is parked while the screen is open.")]
        public Transform bedSpot;
        public Camera roomCamera;
        public GameObject inputContext;

        [Header("Bedroom art (BedScene/Art, assigned by Tools > HideOut > Create Bed room)")]
        [Tooltip("Pixel-art bedroom drawn behind the screen. Empty = flat wall color.")]
        public Sprite backdrop;

        [Header("Selena (replaceable)")]
        [Tooltip("Portrait in the dialogue box. Empty = Resources/Dialogue/Portraits selena sprite.")]
        public Sprite selenaPortrait;
        public string speakerName = "Selena";
        [Tooltip("MonoBehaviour implementing IBedDialogueSource (SLM bridge). Empty = rule-based lines.")]
        public MonoBehaviour dialogueSource;

        [Header("Palette")]
        public Color wallColor = new(.149f, .169f, .161f);
        public Color slateColor = new(.118f, .149f, .141f);
        public Color linenColor = new(.933f, .945f, .914f);
        public Color sageColor = new(.612f, .765f, .659f);
        public Color deepSageColor = new(.184f, .420f, .353f);
        public Color brassColor = new(.831f, .651f, .322f);
        public Color crossColor = new(.788f, .310f, .247f);
        public Color savedColor = new(.310f, .682f, .486f);    // Save button right after a successful save.
        public Color bedStatColor = new(.710f, .408f, .235f);  // Bed upgrade card (max HP).
        [Range(0f, 1f)] public float backdropShade = .12f;

        [Header("Dialogue (unscaled seconds)")]
        [Min(.005f)] public float characterInterval = .04f;
        [Min(0f)] public float lineHoldSeconds = 2.6f;
        [Min(.05f)] public float dialogueFadeSeconds = .35f;

        [Header("Motion (unscaled seconds)")]
        [Min(.05f)] public float introSeconds = .45f;
        [Min(.05f)] public float gearTurnSeconds = .6f;
        [Min(.2f)] public float savedHoldSeconds = 1.6f;
        [Min(.05f)] public float statusRefreshInterval = .25f;
        [Min(.5f)] public float systemMessageSeconds = 4f;

        public HideOutPlayerBridge Player { get; private set; }

        private const int GearTeeth = 14;
        private const float ResourceGaugeWidth = 65f; // Same as HideOutScene.

        private sealed class CostChip
        {
            public RectTransform root;
            public Image back, icon;
            public TMP_Text text;
        }

        private HideOutScene baseView;
        private IBedDialogueSource lines;
        private BedRuleLines fallbackLines;
        private PlayerControlLock controlLock;
        private Rigidbody2D frozenBody;
        private bool bound, leaving, ownsControlLock, bodyWasSimulated;

        // Runtime UI
        private Canvas screen;
        private readonly List<UnityEngine.Object> generated = new();
        private Sprite chamfer, chamferTop, gear, circle, vignette, coinIcon, apIcon, heartIcon, materialFallback;
        private RectTransform panel, dial, portraitImage;
        private Vector2 panelBase, portraitBase;
        private CanvasGroup panelGroup, dialogueGroup, systemGroup, plusGroup;
        private Image saveButtonImage, saveFlashImage, continueMark;
        private Image[] pips = Array.Empty<Image>();
        private TMP_Text lastSaveLabel, saveButtonLabel, systemLabel, levelLabel, levelText, effectLabel, maxedLabel, bodyLabel;
        private readonly TMP_Text[] resourceValues = new TMP_Text[4];
        private readonly RectTransform[] resourceBars = new RectTransform[2];
        private CostChip[] chips = Array.Empty<CostChip>();
        private Button plusButton;

        // Runtime state
        private float introTime = -1f, refreshTimer, systemRemaining, savedRemaining, flashRemaining, breathTime;
        private float gearFrom, gearTo, gearTime = -1f;
        private int talkTicket;
        private bool typing, holding, skipRequested;
        private Coroutine lineRoutine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState() => IsActive = false;

        private void Awake()
        {
            roomCamera.gameObject.SetActive(false);
            inputContext.SetActive(false);
        }

        private void Start()
        {
            // Additive entry is bound by HideOutTravel after loading. Direct Play uses the same rig.
            if (bound || FindFirstObjectByType<HideOutScene>() != null) return;
            var inventory = FindFirstObjectByType<PlayerInventory>();
            if (inventory == null)
            {
                var rig = Instantiate(playerPrefab);
                inventory = rig.GetComponentInChildren<PlayerInventory>();
            }
            var bridge = inventory.GetComponent<HideOutPlayerBridge>();
            if (bridge == null) bridge = inventory.gameObject.AddComponent<HideOutPlayerBridge>();
            bridge.Initialize(settings, true);
            Bind(bridge, null);
        }

        public void Bind(HideOutPlayerBridge bridge, HideOutScene view)
        {
            if (bound) return;
            Player = bridge; baseView = view; bound = true; IsActive = true;

            if (bedSpot != null) Player.transform.position = bedSpot.position;
            if (Player.TryGetComponent<Rigidbody2D>(out var body)) { body.linearVelocity = Vector2.zero; body.angularVelocity = 0; }
            // Freeze/hide with the base's own policy BEFORE this screen's canvas exists:
            // SuspendPlayer hides every active root canvas outside the base scene (field HUD etc.).
            if (baseView != null) baseView.SuspendPlayer();
            else
            {
                controlLock = Player.GetComponent<PlayerControlLock>();
                if (controlLock != null) { controlLock.Acquire(); ownsControlLock = true; }
                // Direct Play: this scene has no floor, so hold the body still instead of letting it fall.
                if (body != null) { frozenBody = body; bodyWasSimulated = body.simulated; body.simulated = false; }
            }

            roomCamera.gameObject.SetActive(true);
            inputContext.SetActive(true);

            fallbackLines = new BedRuleLines();
            lines = dialogueSource as IBedDialogueSource;
            if (dialogueSource != null && lines == null)
                Debug.LogWarning("[Bedroom] dialogueSource does not implement IBedDialogueSource; using rule-based lines.", this);
            lines ??= fallbackLines;

            BuildScreen();
            Player.Changed += OnPlayerChanged;
            if (Player.Inventory != null) Player.Inventory.Changed += OnPlayerChanged;
            RefreshAll();
            StartCoroutine(GreetAfterFade());
        }

        private IEnumerator GreetAfterFade()
        {
            // Bind runs while the screen is black; once the room is visible the chart slides in, then Selena speaks.
            while (SceneFadeTransition.IsRunning) yield return null;
            introTime = 0f;
            yield return new WaitForSecondsRealtime(introSeconds * .6f);
            Say(BedTalkEvent.Enter);
        }

        private void Update()
        {
            if (!bound || leaving) return;
            float dt = Time.unscaledDeltaTime;
            AnimateIntro(dt);
            AnimateEffects(dt);
            refreshTimer -= dt;
            if (refreshTimer <= 0f) { refreshTimer = statusRefreshInterval; RefreshUpgrade(); RefreshResources(); }

            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.escapeKey.wasPressedThisFrame && !InventoryUI.EscapeHandledThisFrame) { ReturnToBase(); return; }
            if (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame) AdvanceDialogue();
        }

        // ---------- Actions ----------

        private bool CanAct => bound && !leaving && Player != null && Player.Initialized && !SceneFadeTransition.IsRunning;

        // The existing bed save (HideOutPlayerBridge.Save): validated snapshot, atomic write, cloud upload when signed in.
        public void SaveGame()
        {
            if (!CanAct || savedRemaining > 0f) return;
            PlayerInputReader.SuppressPointerForCurrentFrame();
            bool saved = Player.Save();
            if (saved) { savedRemaining = savedHoldSeconds; flashRemaining = .5f; }
            ShowSystemMessage(Player.Message);
            Say(saved ? BedTalkEvent.Saved : BedTalkEvent.SaveFailed);
            RefreshAll();
        }

        public void UpgradeBed()
        {
            if (!CanAct) return;
            PlayerInputReader.SuppressPointerForCurrentFrame();
            int before = BedLevel;
            bool hasNext = HideOutModel.Next(Player.State, Player.Settings.rules, Facility.Bed) != null;
            Player.Upgrade(Facility.Bed); // Existing transaction: validate, pay once, level +1, save model.
            BedTalkEvent result;
            if (BedLevel > before)
            {
                result = BedTalkEvent.UpgradeDone;
                if (dial != null) { gearFrom = dial.localEulerAngles.z; gearTo = gearFrom - 360f / GearTeeth; gearTime = 0f; }
            }
            else result = hasNext ? BedTalkEvent.UpgradeShort : BedTalkEvent.UpgradeMaxed;
            ShowSystemMessage(Player.Message);
            Say(result);
            RefreshAll();
        }

        public void ReturnToBase()
        {
            if (!bound || leaving || SceneFadeTransition.IsRunning) return;
            leaving = true;
            Player.StartCoroutine(HideOutTravel.ExitBedroom(this));
        }

        // Called by HideOutTravel while the screen is black. The base re-applies SuspendPlayer itself.
        public void PrepareToLeave()
        {
            StopAllCoroutines();
            lineRoutine = null;
            typing = holding = false;
            if (Player != null && Player.TryGetComponent<Rigidbody2D>(out var body)) body.linearVelocity = Vector2.zero;
            ReleaseDirectPlayHold();
            if (screen != null) screen.gameObject.SetActive(false);
            roomCamera.gameObject.SetActive(false);
            inputContext.SetActive(false);
        }

        // Player bounds are not touched by this screen (the player is frozen and hidden), so nothing to restore.
        public void RestorePlayerBounds() { }

        private void ReleaseDirectPlayHold()
        {
            if (frozenBody != null) { frozenBody.simulated = bodyWasSimulated; frozenBody = null; }
            if (!ownsControlLock) return;
            ownsControlLock = false;
            if (controlLock != null) controlLock.Release();
        }

        // ---------- Dialogue ----------

        private bool SelenaPresent => Player != null && Player.Initialized && Player.State.selenaRecruited;

        private void Say(BedTalkEvent talkEvent)
        {
            if (!SelenaPresent || dialogueGroup == null) return;
            bool hasSave = TryGetLastSave(out DateTime savedAt);
            var context = new BedTalkContext(talkEvent, Player.Health.CurrentHealth, Player.Health.MaxHealth, BedLevel,
                hasSave, hasSave ? (DateTime.Now - savedAt).TotalMinutes : 0);
            int ticket = ++talkTicket;
            // deliver must run on the main thread; a newer event drops older (late) replies.
            void Deliver(string line) { if (ticket == talkTicket && bound && !leaving) ShowLine(line); }
            try { lines.RequestLine(context, Deliver); }
            catch (Exception e)
            {
                Debug.LogException(e, this);
                if (!ReferenceEquals(lines, fallbackLines)) fallbackLines.RequestLine(context, Deliver);
            }
        }

        private void ShowLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            if (lineRoutine != null) StopCoroutine(lineRoutine);
            lineRoutine = StartCoroutine(PlayLine(line.Trim()));
        }

        private IEnumerator PlayLine(string line)
        {
            typing = true; holding = false; skipRequested = false;
            continueMark.gameObject.SetActive(false);
            bodyLabel.text = line;
            bodyLabel.maxVisibleCharacters = 0;
            bodyLabel.ForceMeshUpdate();
            dialogueGroup.blocksRaycasts = true;
            while (dialogueGroup.alpha < 1f)
            {
                dialogueGroup.alpha = Mathf.MoveTowards(dialogueGroup.alpha, 1f, Time.unscaledDeltaTime / .12f);
                yield return null;
            }
            int count = bodyLabel.textInfo.characterCount;
            for (int i = 1; i <= count && !skipRequested; i++)
            {
                bodyLabel.maxVisibleCharacters = i;
                yield return new WaitForSecondsRealtime(characterInterval);
            }
            bodyLabel.maxVisibleCharacters = int.MaxValue;
            typing = false; holding = true; skipRequested = false;
            continueMark.gameObject.SetActive(true);
            float until = Time.unscaledTime + lineHoldSeconds;
            while (!skipRequested && Time.unscaledTime < until) yield return null;
            holding = false;
            continueMark.gameObject.SetActive(false);
            while (dialogueGroup.alpha > 0f)
            {
                dialogueGroup.alpha = Mathf.MoveTowards(dialogueGroup.alpha, 0f, Time.unscaledDeltaTime / dialogueFadeSeconds);
                yield return null;
            }
            dialogueGroup.blocksRaycasts = false;
            lineRoutine = null;
        }

        // Click on the dialogue box, Space or Enter: finish the line, then close it.
        public void AdvanceDialogue()
        {
            if (typing || holding) skipRequested = true;
        }

        // ---------- Refresh ----------

        private int BedLevel => Player.State.levels[(int)Facility.Bed];

        private void OnPlayerChanged() { if (bound && !leaving) RefreshAll(); }

        private void RefreshAll()
        {
            if (screen == null || Player == null || !Player.Initialized) return;
            RefreshSave();
            RefreshUpgrade();
            RefreshResources();
        }

        // Last write time of the slot this bridge saves to (real slot or sandbox); the file itself stays untouched.
        private bool TryGetLastSave(out DateTime savedAt)
        {
            savedAt = default;
            try
            {
                string path = Player != null ? Player.SavePath : null;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
                savedAt = File.GetLastWriteTime(path);
                return true;
            }
            catch (Exception) { return false; }
        }

        private void RefreshSave()
        {
            if (lastSaveLabel == null) return;
            lastSaveLabel.text = TryGetLastSave(out DateTime savedAt)
                ? $"마지막 저장   <b>{savedAt.ToString("M월 d일 HH:mm")}</b>"
                : "저장 기록 없음";
        }

        private void RefreshUpgrade()
        {
            if (effectLabel == null || Player == null || !Player.Initialized) return;
            var rules = Player.Settings.rules;
            var state = Player.State;
            int level = BedLevel, max = MaxLevel(rules, Facility.Bed);
            levelLabel.text = HideOutModel.DisplayLevel(level).ToString(); // Shown from Lv 1; pips count upgrades.
            levelText.text = max > 0 ? $"Lv {HideOutModel.DisplayLevel(level)} / {HideOutModel.DisplayLevel(max)}" : $"Lv {HideOutModel.DisplayLevel(level)}";
            for (int i = 0; i < pips.Length; i++) pips[i].color = i < level ? bedStatColor : WithAlpha(slateColor, .12f);

            var cost = HideOutModel.Next(state, rules, Facility.Bed);
            int now = level * rules.bedHPPerLevel;
            effectLabel.text = cost == null ? $"최대 HP +{now}"
                : $"최대 HP +{now}  <color=#{Hex(bedStatColor)}>→  +{(level + 1) * rules.bedHPPerLevel}</color>";
            plusButton.interactable = cost != null;
            plusGroup.alpha = cost != null ? 1f : .35f;
            maxedLabel.gameObject.SetActive(cost == null);

            int used = 0;
            float x = 0f;
            if (cost != null)
            {
                x = SetChip(chips[used++], x, coinIcon, new Color(.82f, .67f, .38f), state.coins, cost.coins);
                x = SetChip(chips[used++], x, apIcon, new Color(.83f, .79f, .66f), state.actionPoints, cost.actionPoints, "AP ");
                foreach (var material in cost.materials ?? Array.Empty<MaterialCost>())
                {
                    if (material == null || string.IsNullOrEmpty(material.itemId) || used >= chips.Length) continue;
                    var item = Player.Settings.Find(material.itemId);
                    bool hasIcon = item != null && item.Icon != null;
                    x = SetChip(chips[used++], x, hasIcon ? item.Icon : materialFallback, hasIcon ? Color.white : brassColor,
                        HideOutModel.Count(state.inventory, material.itemId), material.amount, Player.Settings.Name(material.itemId) + " ");
                }
            }
            for (int i = used; i < chips.Length; i++) chips[i].root.gameObject.SetActive(false);
        }

        private float SetChip(CostChip chip, float x, Sprite icon, Color iconTint, int owned, int required, string label = "")
        {
            bool short_ = owned < required;
            chip.root.gameObject.SetActive(true);
            chip.icon.sprite = icon; chip.icon.color = iconTint;
            chip.text.text = $"{label}{owned}/{required}";
            chip.text.color = short_ ? crossColor : slateColor;
            chip.back.color = short_ ? WithAlpha(crossColor, .13f) : WithAlpha(Color.white, .85f);
            float width = Mathf.Ceil(34f + chip.text.GetPreferredValues(chip.text.text).x + 10f);
            chip.root.anchoredPosition = new Vector2(x, chip.root.anchoredPosition.y);
            chip.root.sizeDelta = new Vector2(width, chip.root.sizeDelta.y);
            chip.text.rectTransform.sizeDelta = new Vector2(width - 34f, chip.text.rectTransform.sizeDelta.y);
            return x + width + 6f;
        }

        // Mirrors HideOutScene.Refresh for the resource cards so the values read the same in and out of the room.
        private void RefreshResources()
        {
            if (resourceValues[0] == null || Player == null || !Player.Initialized) return;
            var state = Player.State;
            int health = Player.Health.CurrentHealth, maxHealth = Math.Max(1, Player.Health.MaxHealth), maxMana = Math.Max(1, Player.MaxMana);
            resourceValues[0].text = $"{health}/{Player.Health.MaxHealth}";
            resourceValues[1].text = $"{state.mana}/{Player.MaxMana}";
            resourceValues[2].text = state.coins.ToString("N0");
            resourceValues[3].text = state.actionPoints.ToString("N0");
            resourceBars[0].sizeDelta = new Vector2(ResourceGaugeWidth * Mathf.Clamp01((float)health / maxHealth), 3f);
            resourceBars[1].sizeDelta = new Vector2(ResourceGaugeWidth * Mathf.Clamp01((float)state.mana / maxMana), 3f);
        }

        private static string Hex(Color color) => ColorUtility.ToHtmlStringRGB(color);

        private static int MaxLevel(HideOutRules rules, Facility facility)
        {
            foreach (var definition in rules.facilities)
                if (definition != null && definition.facility == facility) return definition.upgrades?.Length ?? 0;
            return -1;
        }

        private void ShowSystemMessage(string message)
        {
            if (systemLabel == null || string.IsNullOrEmpty(message)) return;
            systemLabel.text = message;
            systemRemaining = systemMessageSeconds;
            systemGroup.alpha = 1f;
        }

        // ---------- Motion ----------

        private void AnimateIntro(float dt)
        {
            if (panel == null) return;
            if (introTime < 0f) { panelGroup.alpha = 0f; return; }
            if (introTime > introSeconds) return;
            introTime += dt;
            float t = Mathf.Clamp01(introTime / introSeconds), eased = 1f - (1f - t) * (1f - t) * (1f - t);
            panelGroup.alpha = eased;
            panel.anchoredPosition = panelBase + new Vector2(32f * (1f - eased), 0f);
        }

        private void AnimateEffects(float dt)
        {
            // Save button: "저장 완료" in green for savedHoldSeconds, then back to the normal action.
            if (saveButtonImage != null)
            {
                bool saved = savedRemaining > 0f;
                if (saved) savedRemaining = Mathf.Max(0f, savedRemaining - dt);
                saveButtonImage.color = saved ? savedColor : brassColor;
                saveButtonLabel.text = saved ? "저장 완료" : "지금 저장하기";
                saveButtonLabel.color = saved ? Color.white : slateColor;
            }
            if (saveFlashImage != null)
            {
                flashRemaining = Mathf.Max(0f, flashRemaining - dt);
                saveFlashImage.color = WithAlpha(new Color(1f, .93f, .76f), .25f * Mathf.Clamp01(flashRemaining / .5f));
            }
            if (dial != null && gearTime >= 0f)
            {
                gearTime += dt;
                float t = Mathf.Clamp01(gearTime / gearTurnSeconds), eased = 1f - Mathf.Pow(1f - t, 3f);
                dial.localEulerAngles = new Vector3(0f, 0f, Mathf.LerpAngle(gearFrom, gearTo, eased));
                if (t >= 1f) gearTime = -1f;
            }
            if (portraitImage != null)
            {
                breathTime += dt;
                float breath = 1f + .01f * Mathf.Sin(breathTime * 2f * Mathf.PI / 3.4f);
                portraitImage.localScale = new Vector3(breath, breath, 1f);
                portraitImage.anchoredPosition = portraitBase + new Vector2(0f, typing ? Mathf.Abs(Mathf.Sin(breathTime * 9f)) * 2f : 0f);
            }
            if (continueMark != null && continueMark.gameObject.activeSelf)
                continueMark.color = WithAlpha(brassColor, .35f + .65f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f)));
            if (systemGroup != null && systemRemaining > 0f)
            {
                systemRemaining -= dt;
                systemGroup.alpha = Mathf.Clamp01(systemRemaining / .6f);
            }
        }

        // ---------- Layout (1600x900 reference, top-left coordinates; see bedroom_design_mock.png) ----------
        //   [back]                                              [HP][MP][코인][AP]
        //   bedroom backdrop (bed, shelf, lamp, wardrobe)   +-- bed chart (clipped) ---+
        //                                                   | 침대                    |
        //                                                   | 마지막 저장 / [지금 저장하기] |
        //                                                   | [(gear) + 침대 강화 Lv .. ]  |
        //   [Selena]                                        +-------------------------+
        //   +-- dialogue glass: [portrait] line --------------------------------------+

        private void BuildScreen()
        {
            chamfer = MakeSprite(CreateChamferTexture(24, 6, true), new Vector4(8, 8, 8, 8));
            chamferTop = MakeSprite(CreateChamferTexture(24, 6, false), new Vector4(8, 8, 8, 8));
            gear = MakeSprite(CreateGearTexture(256, GearTeeth), Vector4.zero);
            circle = MakeSprite(CreateCircleTexture(64), Vector4.zero);
            vignette = MakeSprite(CreateRadialTexture(128), Vector4.zero);
            heartIcon = MakeSprite(CreateResourceTexture(0), Vector4.zero);
            coinIcon = MakeSprite(CreateResourceTexture(2), Vector4.zero);
            apIcon = MakeSprite(CreateResourceTexture(3), Vector4.zero);
            materialFallback = MakeSprite(CreateChamferTexture(24, 6, true), Vector4.zero);

            var root = new GameObject("Bedroom Screen (runtime)", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            screen = root.GetComponent<Canvas>();
            screen.renderMode = RenderMode.ScreenSpaceOverlay;
            screen.sortingOrder = 300; // Same layer as the treatment screen: above base UI, below story dialogue and fades.
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = .5f;
            var t = root.transform;

            var wall = Box(t, "Bedroom backdrop - replaceable", 0, 0, 0, 0, wallColor);
            Stretch(wall.rectTransform);
            if (backdrop != null) { wall.sprite = backdrop; wall.color = Color.white; }
            Stretch(Box(t, "Backdrop shade", 0, 0, 0, 0, WithAlpha(slateColor, backdropShade)).rectTransform);
            var edges = Box(t, "Vignette", 0, 0, 0, 0, WithAlpha(Color.black, .55f)); edges.sprite = vignette;
            Stretch(edges.rectTransform);

            var back = MakeButton(t, "Back to base", 40, 28, 250, 42, ReturnToBase, WithAlpha(slateColor, .85f), chamfer);
            Label(back.transform, "← 기지로 돌아가기", 16, 0, 180, 42, 18, linenColor, TextAlignmentOptions.MidlineLeft);
            var key = Box(back.transform, "ESC key", 194, 11, 42, 20, WithAlpha(linenColor, .16f));
            Label(key.transform, "ESC", 0, 0, 42, 20, 13, WithAlpha(linenColor, .8f), TextAlignmentOptions.Center);

            BuildPanel(t);
            BuildResourceHud(t);
            BuildDialogue(t);

            saveFlashImage = Box(t, "Save flash", 0, 0, 0, 0, Color.clear);
            Stretch(saveFlashImage.rectTransform);
        }

        private void BuildPanel(Transform t)
        {
            panel = new GameObject("Bed chart", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
            panel.SetParent(t, false); Rect(panel, 1048, 88, 492, 512);
            panelBase = panel.anchoredPosition;
            panelGroup = panel.GetComponent<CanvasGroup>();
            var c = panel.transform;

            var paper = Box(c, "Paper", 0, 0, 492, 512, linenColor); paper.sprite = chamfer; paper.type = Image.Type.Sliced;
            var clip = Box(c, "Clip", 186, -14, 120, 26, brassColor); clip.sprite = chamfer; clip.type = Image.Type.Sliced;
            Box(clip.transform, "Clip slot", 36, 9, 48, 8, Color.Lerp(brassColor, slateColor, .45f));

            // Header: a small bed mark and the facility name.
            Box(c, "Bed mark mattress", 24, 34, 36, 12, bedStatColor);
            Box(c, "Bed mark pillow", 27, 28, 11, 8, slateColor);
            Box(c, "Bed mark head", 22, 22, 4, 30, slateColor);
            Box(c, "Bed mark foot", 58, 32, 4, 20, slateColor);
            Label(c, "침대", 74, 14, 300, 44, 30, slateColor, TextAlignmentOptions.MidlineLeft, true);
            Box(c, "Rule", 24, 66, 444, 2, WithAlpha(slateColor, .14f));

            // Save: the only disk save point, same call as the base's "저장하기".
            Label(c, "기록 저장", 24, 80, 200, 24, 18, deepSageColor, TextAlignmentOptions.MidlineLeft);
            lastSaveLabel = Label(c, "", 24, 106, 444, 28, 19, slateColor, TextAlignmentOptions.MidlineLeft);
            var save = MakeButton(c, "Save", 24, 140, 444, 64, SaveGame, brassColor, chamfer);
            saveButtonImage = save.GetComponent<Image>();
            Box(save.transform, "Disk", 26, 18, 28, 28, slateColor);
            Box(save.transform, "Disk label", 32, 18, 16, 10, linenColor);
            Box(save.transform, "Disk shutter", 31, 34, 18, 9, WithAlpha(linenColor, .55f));
            saveButtonLabel = Label(save.transform, "지금 저장하기", 0, 0, 444, 64, 24, slateColor, TextAlignmentOptions.Center, true);
            Label(c, "저장은 침대에서만 할 수 있어요.", 24, 212, 444, 22, 15, Color.Lerp(slateColor, linenColor, .45f), TextAlignmentOptions.MidlineLeft);
            var systemRoot = new GameObject("System message", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
            systemRoot.SetParent(c, false); Rect(systemRoot, 24, 236, 444, 26);
            systemGroup = systemRoot.GetComponent<CanvasGroup>(); systemGroup.alpha = 0f; systemGroup.blocksRaycasts = false;
            systemLabel = Label(systemRoot, "", 0, 0, 444, 26, 16, Color.Lerp(slateColor, linenColor, .35f), TextAlignmentOptions.MidlineLeft);
            Box(c, "Rule", 24, 272, 444, 2, WithAlpha(slateColor, .14f));

            BuildUpgradeCard(c);
        }

        // Bed upgrade (max HP): + runs the existing upgrade transaction; there is no downgrade for the bed.
        private void BuildUpgradeCard(Transform c)
        {
            var back = Box(c, "Bed upgrade card", 12, 290, 468, 206, WithAlpha(Color.white, .55f)); back.sprite = chamfer; back.type = Image.Type.Sliced;
            var r = back.transform;
            Box(r, "Stripe", 0, 12, 5, 182, bedStatColor);

            dial = Gear(r, "Dial", 18, 18, 104, bedStatColor);
            levelLabel = Label(r, "1", 18, 18, 104, 104, 28, slateColor, TextAlignmentOptions.Center, true);
            plusButton = SignButton(r, "Upgrade +", 134, 28, 40, UpgradeBed);
            plusGroup = plusButton.gameObject.AddComponent<CanvasGroup>();

            var badge = Box(r, "Icon", 188, 18, 24, 24, bedStatColor); badge.sprite = heartIcon; badge.preserveAspect = true;
            Label(r, "침대 강화", 220, 12, 150, 36, 23, slateColor, TextAlignmentOptions.MidlineLeft, true);
            levelText = Label(r, "", 340, 16, 112, 28, 17, deepSageColor, TextAlignmentOptions.MidlineRight);
            int count = Mathf.Clamp(MaxLevel(Player.Settings.rules, Facility.Bed), 0, 12);
            pips = new Image[count];
            for (int i = 0; i < count; i++)
            {
                pips[i] = Box(r, "Level pip", 188 + i * 17f, 56, 14, 12, WithAlpha(slateColor, .12f));
                pips[i].sprite = chamfer; pips[i].type = Image.Type.Sliced; pips[i].pixelsPerUnitMultiplier = 2f;
            }
            effectLabel = Label(r, "", 188, 76, 270, 32, 19, slateColor, TextAlignmentOptions.MidlineLeft);

            var chipRow = new GameObject("Cost chips", typeof(RectTransform)).GetComponent<RectTransform>();
            chipRow.SetParent(r, false); Rect(chipRow, 18, 146, 440, 32);
            chips = new CostChip[4];
            for (int i = 0; i < chips.Length; i++)
            {
                var chipBack = Box(chipRow, "Cost chip", 0, 0, 100, 32, Color.white); chipBack.sprite = chamfer; chipBack.type = Image.Type.Sliced;
                chipBack.pixelsPerUnitMultiplier = 2f;
                var chipIcon = Box(chipBack.transform, "Icon", 8, 6, 20, 20, Color.white); chipIcon.preserveAspect = true;
                var chipText = Label(chipBack.transform, "", 34, 0, 64, 32, 16, slateColor, TextAlignmentOptions.MidlineLeft);
                chipText.textWrappingMode = TextWrappingModes.NoWrap;
                chips[i] = new CostChip { root = chipBack.rectTransform, back = chipBack, icon = chipIcon, text = chipText };
            }
            maxedLabel = Label(r, "최대 단계까지 강화했습니다.", 18, 146, 440, 32, 17, deepSageColor, TextAlignmentOptions.MidlineLeft);
            maxedLabel.gameObject.SetActive(false);
        }

        // Same cards, position and colors as HideOutScene.BuildResourceHud (x 1095..1572, y 14..62).
        private void BuildResourceHud(Transform t)
        {
            string[] labels = { "HP", "MP", "코인", "AP" };
            Color[] colors = { new(.81f, .43f, .39f), new(.35f, .64f, .71f), new(.82f, .67f, .38f), new(.83f, .79f, .66f) };
            var muted = new Color(.63f, .67f, .68f);
            var root = new GameObject("Resource HUD (same as base)", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(t, false); Stretch(root);
            for (int i = 0; i < 4; i++)
            {
                var card = Box(root, "Resource " + labels[i], 1095 + i * 121, 14, 114, 48, new Color(.045f, .055f, .06f, .97f));
                Box(card.transform, "Top Edge", 0, 0, 114, 1, WithAlpha(colors[i], .6f));
                var badge = Box(card.transform, "Icon Socket", 6, 10, 27, 27, new Color(.10f, .12f, .12f)); badge.sprite = circle;
                var icon = Box(card.transform, labels[i] + " Symbol", 10, 14, 19, 19, colors[i]);
                icon.sprite = MakeSprite(CreateResourceTexture(i), Vector4.zero); icon.preserveAspect = true;
                Label(card.transform, labels[i], 40, 5, 70, 14, 10, muted, TextAlignmentOptions.TopLeft);
                resourceValues[i] = Label(card.transform, "-", 40, 19, 70, 20, 14, Color.white, TextAlignmentOptions.TopLeft);
                resourceValues[i].textWrappingMode = TextWrappingModes.NoWrap;
                if (i < 2)
                {
                    Box(card.transform, "Gauge Track", 40, 41, ResourceGaugeWidth, 3, new Color(.15f, .18f, .18f));
                    resourceBars[i] = Box(card.transform, "Gauge Fill", 40, 41, ResourceGaugeWidth, 3, colors[i]).rectTransform;
                }
            }
        }

        // Name tab above a dark glass box with a small Selena portrait; click / Space / Enter advances.
        private void BuildDialogue(Transform t)
        {
            var dialogueRoot = new GameObject("Selena dialogue", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
            dialogueRoot.SetParent(t, false); Stretch(dialogueRoot);
            dialogueGroup = dialogueRoot.GetComponent<CanvasGroup>(); dialogueGroup.alpha = 0f; dialogueGroup.blocksRaycasts = false;
            var tab = Box(dialogueRoot, "Name tab", 64, 648, 190, 44, sageColor); tab.sprite = chamferTop; tab.type = Image.Type.Sliced;
            Label(tab.transform, speakerName, 18, 0, 170, 44, 26, slateColor, TextAlignmentOptions.MidlineLeft, true);
            var box = Box(dialogueRoot, "Dialogue box", 40, 692, 1520, 176, WithAlpha(slateColor, .94f));
            box.sprite = chamfer; box.type = Image.Type.Sliced; box.raycastTarget = true;
            Box(box.transform, "Brass rule", 8, 0, 1504, 3, brassColor);
            var advance = box.gameObject.AddComponent<Button>(); advance.transition = Selectable.Transition.None;
            advance.onClick.AddListener(AdvanceDialogue);

            var frame = Box(box.transform, "Portrait frame", 18, 18, 140, 140, brassColor); frame.sprite = chamfer; frame.type = Image.Type.Sliced;
            var window = Box(frame.transform, "Portrait window - replaceable", 4, 4, 132, 132, deepSageColor);
            window.gameObject.AddComponent<RectMask2D>();
            var sprite = selenaPortrait;
            if (sprite == null)
            {
                var library = Resources.Load<DialoguePortraitLibrary>("Dialogue/Portraits");
                if (library != null) sprite = library.selena;
            }
            if (sprite != null)
            {
                var image = Box(window.transform, "Selena", 0, 0, 132, 132, Color.white);
                image.sprite = sprite;
                portraitImage = image.rectTransform;
                portraitImage.pivot = new Vector2(.5f, 0f);
                float aspect = sprite.rect.width / Mathf.Max(1f, sprite.rect.height);
                portraitImage.sizeDelta = aspect >= 1f ? new Vector2(132f * aspect, 132f) : new Vector2(132f, 132f / aspect);
                portraitBase = portraitImage.anchoredPosition = new Vector2(66f, -132f);
            }

            bodyLabel = Label(box.transform, "", 184, 26, 1300, 130, 30, linenColor, TextAlignmentOptions.TopLeft);
            continueMark = Box(box.transform, "Continue mark", 1480, 142, 10, 10, brassColor);
            continueMark.gameObject.SetActive(false);
        }

        // ---------- Widgets ----------

        private TMP_FontAsset Font => Player != null && Player.Settings != null && Player.Settings.font != null ? Player.Settings.font : settings.font;

        private static Color WithAlpha(Color color, float alpha) { color.a = alpha; return color; }

        private static void Rect(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static Image Box(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            Rect(image.rectTransform, x, y, w, h);
            image.color = color; image.raycastTarget = false;
            return image;
        }

        private RectTransform Gear(Transform parent, string name, float x, float y, float size, Color color)
        {
            var image = Box(parent, name, x, y, size, size, color);
            image.sprite = gear;
            var rect = image.rectTransform;
            rect.pivot = new Vector2(.5f, .5f); // Spins around its hub.
            rect.anchoredPosition = new Vector2(x + size * .5f, -(y + size * .5f));
            return rect;
        }

        private TMP_Text Label(Transform parent, string value, float x, float y, float w, float h, int size, Color color, TextAlignmentOptions alignment, bool bold = false)
        {
            var text = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            text.transform.SetParent(parent, false);
            Rect(text.rectTransform, x, y, w, h);
            var font = Font;
            if (font != null) { text.font = font; text.fontSharedMaterial = font.material; }
            UIFontTheme.Apply(text, bold ? UIFontTheme.ExtraBold : UIFontTheme.Normal);
            text.text = value; text.fontSize = size; text.color = color; text.alignment = alignment;
            text.raycastTarget = false; text.richText = true;
            text.textWrappingMode = TextWrappingModes.Normal;
            return text;
        }

        private Button MakeButton(Transform parent, string name, float x, float y, float w, float h, Action onClick, Color color, Sprite shape)
        {
            var image = Box(parent, name, x, y, w, h, color);
            if (shape != null) { image.sprite = shape; image.type = Image.Type.Sliced; }
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(.9f, .9f, .9f); colors.pressedColor = new Color(.78f, .78f, .78f);
            colors.selectedColor = Color.white; colors.disabledColor = Color.white; // Disabled look comes from a CanvasGroup.
            button.colors = colors;
            if (onClick != null) button.onClick.AddListener(() => onClick());
            return button;
        }

        // Filled brass "+" drawn with bars (font-independent).
        private Button SignButton(Transform parent, string name, float x, float y, float size, Action onClick)
        {
            var button = MakeButton(parent, name, x, y, size, size, onClick, brassColor, null);
            float bar = Mathf.Max(3f, Mathf.Round(size * .08f)), length = Mathf.Round(size * .45f);
            Box(button.transform, "Bar H", (size - length) * .5f, (size - bar) * .5f, length, bar, linenColor);
            Box(button.transform, "Bar V", (size - bar) * .5f, (size - length) * .5f, bar, length, linenColor);
            return button;
        }

        // ---------- Generated shapes (destroyed with the screen) ----------

        private Sprite MakeSprite(Texture2D texture, Vector4 border)
        {
            generated.Add(texture);
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect, border);
            generated.Add(sprite);
            return sprite;
        }

        private static Texture2D NewTexture(int size) =>
            new(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };

        private static void Finish(Texture2D texture, Color32[] pixels)
        {
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
        }

        // White square with 45-degree cut corners (all four, or the top two only), for 9-slice panels.
        private static Texture2D CreateChamferTexture(int size, int cut, bool bottomToo)
        {
            var texture = NewTexture(size);
            texture.filterMode = FilterMode.Point; // Crisp steps, like the pixel font.
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int dx = Math.Min(x, size - 1 - x), dyTop = size - 1 - y, dyBottom = y;
                bool cutTop = dx + dyTop < cut, cutBottom = bottomToo && dx + dyBottom < cut;
                pixels[y * size + x] = cutTop || cutBottom ? new Color32(255, 255, 255, 0) : new Color32(255, 255, 255, 255);
            }
            Finish(texture, pixels);
            return texture;
        }

        private static Texture2D CreateCircleTexture(int size)
        {
            var texture = NewTexture(size);
            var pixels = new Color32[size * size];
            float radius = size * .5f - 1f, center = (size - 1) * .5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float distance = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(radius - distance + .5f)));
            }
            Finish(texture, pixels);
            return texture;
        }

        // Heart / drop / coin / bolt: the same shapes as HideOutScene.CreateResourceSprite.
        private static Texture2D CreateResourceTexture(int kind)
        {
            const int size = 40;
            var texture = NewTexture(size);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + .5f) / size * 2f - 1f, v = (y + .5f) / size * 2f - 1f;
                bool filled;
                if (kind == 0)
                {
                    float a = u * u + v * v - .56f;
                    filled = a * a * a - u * u * v * v * v < 0f;
                }
                else if (kind == 1) filled = (u * u + (v + .25f) * (v + .25f) < .32f && v < .05f) || (v >= -.1f && v <= .85f && Mathf.Abs(u) < (.85f - v) * .6f);
                else if (kind == 2)
                {
                    float r = u * u + v * v;
                    filled = r < .77f && (r > .48f || Mathf.Abs(u) < .13f && Mathf.Abs(v) < .45f);
                }
                else filled = v >= 0f ? u >= -.17f && u <= .35f + v * .5f && v < .85f : u >= -.35f + v * .5f && u <= .17f && v > -.85f;
                pixels[y * size + x] = filled ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            }
            Finish(texture, pixels);
            return texture;
        }

        // Gear with trapezoid teeth, an open hub and a thin inner ring (all white, tinted by the Image).
        private static Texture2D CreateGearTexture(int size, int teeth)
        {
            var texture = NewTexture(size);
            var pixels = new Color32[size * size];
            float center = (size - 1) * .5f, outer = size * .5f - 2f, root = outer - size * .12f;
            float hub = size * .26f, ringIn = root - size * .06f, ringOut = ringIn + size * .025f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - center, dy = y - center, r = Mathf.Sqrt(dx * dx + dy * dy);
                float phase = Mathf.Repeat((Mathf.Atan2(dy, dx) / (2f * Mathf.PI) + 1f) * teeth, 1f);
                float profile = Mathf.Clamp01(Mathf.Min(phase - .12f, .88f - phase) * 6f);
                float edge = Mathf.Lerp(root, outer, profile);
                float body = Mathf.Clamp01(edge - r + .5f);
                float hole = Mathf.Clamp01(r - hub + .5f);
                float groove = Mathf.Clamp01(Mathf.Min(r - ringIn, ringOut - r) + .5f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * body * hole * (1f - groove)));
            }
            Finish(texture, pixels);
            return texture;
        }

        // Dark edges, clear centre (vignette over the bedroom backdrop).
        private static Texture2D CreateRadialTexture(int size)
        {
            var texture = NewTexture(size);
            var pixels = new Color32[size * size];
            float center = (size - 1) * .5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center)) / center;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.6f, 1.3f, d));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * a));
            }
            Finish(texture, pixels);
            return texture;
        }

        private void OnDestroy()
        {
            if (bound)
            {
                ReleaseDirectPlayHold();
                if (Player != null)
                {
                    Player.Changed -= OnPlayerChanged;
                    if (Player.Inventory != null) Player.Inventory.Changed -= OnPlayerChanged;
                }
                IsActive = false;
            }
            foreach (var item in generated) if (item != null) Destroy(item);
            generated.Clear();
        }
    }
}
