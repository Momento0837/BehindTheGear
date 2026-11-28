using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace BehindTheGear.HideOut
{
    // Treatment booth screen entered from the base when the clinic is selected (same fade-through travel as the
    // ScareCrow training room). Visual-novel style: the player is frozen and hidden with the base's own
    // SuspendPlayer policy, Selena's portrait hangs in a brass frame on the left, the clinic chart (cooldown, heal,
    // upgrade dials) is clipped on the right and she answers each event with one line (ITreatmentDialogueSource).
    // Heal / cooldown / upgrade stay owned by HideOutPlayerBridge + HideOutModel; this screen only calls them.
    // The UI is built at runtime (like HideOutScene); shapes (chamfered panels, gear dials) are generated here,
    // the wall and lamp glow reuse TreatmentScene/Art sprites when assigned.
    [DefaultExecutionOrder(-75)]
    public sealed class TreatmentRoomScene : MonoBehaviour, IHideOutFacilityRoom
    {
        public const string ScenePath = "Assets/Scenes/HideOutSystemScene/TreatmentScene/TreatmentScene.unity";
        public static bool IsActive { get; private set; }

        public HideOutSettings settings;
        [Tooltip("Existing gameplay rig used only when this scene is played directly.")]
        public GameObject playerPrefab;
        [Tooltip("Where the (hidden, frozen) player is parked while the screen is open.")]
        public Transform treatmentSpot;
        public Camera roomCamera;
        public GameObject inputContext;
        [Tooltip("Old walk-in room HUD. Kept inactive; Tools > HideOut > Rebuild Treatment room set removes it.")]
        [FormerlySerializedAs("roomCanvas")] public Canvas legacyHud;

        [Header("Selena (replaceable)")]
        [Tooltip("Portrait shown in the frame. Empty = Resources/Dialogue/Portraits selena sprite.")]
        public Sprite selenaPortrait;
        [Tooltip("Optional animated portrait (e.g. Live2D) instantiated into the frame instead of the sprite.")]
        public GameObject portraitPrefab;
        public string speakerName = "Selena";
        [Tooltip("MonoBehaviour implementing ITreatmentDialogueSource (SLM bridge). Empty = Selena line pool (+ rule-based fallback).")]
        public MonoBehaviour dialogueSource;

        [Header("Room art (TreatmentScene/Art, assigned by Rebuild Treatment room set)")]
        [Tooltip("Back wall drawn behind the screen (point-filtered pixel art). Empty = flat wall color.")]
        public Sprite wallSprite;
        [Tooltip("Soft lamp glow behind the portrait. Empty = generated glow.")]
        public Sprite glowSprite;

        [Header("Palette")]
        public Color wallColor = new(.149f, .169f, .161f);      // #262B29 riveted wall
        public Color slateColor = new(.118f, .149f, .141f);     // #1E2624 dialogue glass / ink
        public Color linenColor = new(.933f, .945f, .914f);     // #EEF1E9 clinic chart
        public Color sageColor = new(.612f, .765f, .659f);      // #9CC3A8 Selena, name tab
        public Color deepSageColor = new(.184f, .420f, .353f);  // #2F6B5A labels, gauge
        public Color readyColor = new(.310f, .682f, .486f);     // #4FAE7C ready / heal
        public Color brassColor = new(.831f, .651f, .322f);     // #D4A652 frame, clip, + buttons (base Accent)
        public Color crossColor = new(.788f, .310f, .247f);     // #C94F3F medical red, shortfalls
        public Color healStatColor = new(.784f, .341f, .290f);  // #C8574A recovery amount stat
        public Color speedStatColor = new(.243f, .541f, .604f); // #3E8A9A time reduction stat
        [Range(0f, 1f)] public float wallShade = .5f;

        [Header("Cooldown gauge colors")]
        public Color gaugeWaitColor = new(.310f, .682f, .486f);   // #4FAE7C preparing (grows left to right from empty)
        public Color gaugeReadyColor = new(.231f, .525f, .839f);  // #3B86D6 ready: sweeps in left to right
        public Color gaugeFlashColor = new(.361f, .635f, .910f);  // #5CA2E8 the two blinks after the sweep
        public Color readyLabelColor = new(.122f, .373f, .659f);  // #1F5FA8 "치료 가능"
        [Min(.05f)] public float readySweepSeconds = .6f;
        [Min(.05f)] public float readyBlinkSeconds = .9f;

        [Header("Dialogue (unscaled seconds)")]
        [Min(.005f)] public float characterInterval = .04f;
        [Min(0f)] public float lineHoldSeconds = 2.4f;
        [Min(.05f)] public float dialogueFadeSeconds = .35f;

        [Header("Motion (unscaled seconds)")]
        [Range(0f, .05f)] public float breathAmplitude = .012f;
        [Min(.5f)] public float breathPeriod = 3.4f;
        [Min(0f)] public float talkBob = 3f;
        [Min(.05f)] public float introSeconds = .45f;
        [Min(.05f)] public float gearTurnSeconds = .6f;

        [Header("Downgrade (-)")]
        [Tooltip("The first press of - arms it for this long; a second press inside the window lowers the level.")]
        [Min(.5f)] public float downgradeConfirmSeconds = 3f;

        [Header("Cooldown gauge")]
        [Tooltip("Remaining seconds at which the countdown starts to blink.")]
        [Min(0f)] public float urgentSeconds = 10f;
        [Min(.1f)] public float fillCatchUpSpeed = 2.5f;
        [Min(.05f)] public float statusRefreshInterval = .25f;
        [Min(.5f)] public float systemMessageSeconds = 4f;

        public HideOutPlayerBridge Player { get; private set; }

        private enum TimerState { Cooldown, Ready, FullHealth, Blocked }

        private const int GearTeeth = 14;
        private const float ResourceGaugeWidth = 65f; // Same as HideOutScene.

        private HideOutScene baseView;
        private ITreatmentDialogueSource lines;
        private TreatmentRuleLines fallbackLines;
        private PlayerControlLock controlLock;
        private bool bound, leaving, ownsControlLock;

        // Runtime UI
        private Canvas screen;
        private readonly System.Collections.Generic.List<UnityEngine.Object> generated = new();
        private Sprite chamfer, chamferTop, gear, softGlow, vignette, circle, coinIcon, apIcon, heartIcon, clockIcon, materialFallback;
        private RectTransform portraitImage, chart, gaugeFill, gaugeReady, healFlash;
        private Vector2 portraitBase, chartBase;
        private CanvasGroup chartGroup, portraitGroup, dialogueGroup, systemGroup;
        private Image gaugeFillImage, gaugeReadyImage, healButtonImage, healCross, healFlashImage, continueMark;
        private Color continueMarkBaseColor;
        private TMP_Text clockLabel, stateLabel, healButtonLabel, systemLabel;
        private TMP_Text bodyLabel;
        private Button tutorialHealButton, tutorialBackButton, tutorialAdvanceButton;
        private TutorialButtonHighlight treatmentTutorialHint;
        // Same HP / MP / coin / AP cards as the base's top-right HUD (HideOutScene.BuildResourceHud).
        private readonly TMP_Text[] resourceValues = new TMP_Text[4];
        private readonly RectTransform[] resourceBars = new RectTransform[2];
        private StatCard healCard, speedCard;

        // One upgradeable stat of the booth: gear dial with the level, +/- buttons, level pips, effect and cost chips.
        private sealed class StatCard
        {
            public Color color;
            public RectTransform dial;
            public TMP_Text level, levelText, effect, maxed;
            public Image[] pips = Array.Empty<Image>();
            public string title;
            public Button plus, minus;
            public CanvasGroup plusGroup, minusGroup;
            public Image minusInset, minusBar;
            public float minusArmedUntil;
            public CostChip[] chips;
            public float gearFrom, gearTo, gearTime = -1f;
        }

        private sealed class CostChip
        {
            public RectTransform root;
            public Image back, icon;
            public TMP_Text text;
        }

        // Runtime state
        private float breathTime, talkTime, bob, displayedFill = -1f, refreshTimer, flashRemaining, systemRemaining;
        private float introTime = -1f, readySweep = -1f, readyBlink = -1f;
        private bool gaugeWasCooling;
        private int shownSeconds = -1, talkTicket;
        private TimerState shownState = (TimerState)(-1);
        private bool wasOnCooldown, typing, holding, skipRequested;
        private Coroutine lineRoutine, enterRoutine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState() => IsActive = false;

        private void Awake()
        {
            roomCamera.gameObject.SetActive(false);
            inputContext.SetActive(false);
            if (legacyHud != null) legacyHud.gameObject.SetActive(false);
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

            if (treatmentSpot != null) Player.transform.position = treatmentSpot.position;
            if (Player.TryGetComponent<Rigidbody2D>(out var body)) { body.linearVelocity = Vector2.zero; body.angularVelocity = 0; }
            // Freeze/hide with the base's own policy BEFORE this screen's canvas exists:
            // SuspendPlayer hides every active root canvas outside the base scene (field HUD etc.).
            if (baseView != null) baseView.SuspendPlayer();
            else
            {
                controlLock = Player.GetComponent<PlayerControlLock>();
                if (controlLock != null) { controlLock.Acquire(); ownsControlLock = true; }
            }

            if (legacyHud != null) legacyHud.gameObject.SetActive(false);
            roomCamera.gameObject.SetActive(true);
            inputContext.SetActive(true);

            fallbackLines = new TreatmentRuleLines();
            lines = dialogueSource as ITreatmentDialogueSource;
            if (dialogueSource != null && lines == null)
                Debug.LogWarning("[Treatment] dialogueSource does not implement ITreatmentDialogueSource; using rule-based lines.", this);
            // Default: reviewed Selena line pool; events it does not cover fall back to the rule-based lines.
            lines ??= new SelenaPoolTreatmentLines(Player, fallbackLines);

            BuildScreen();
            Player.Changed += OnPlayerChanged;
            if (Player.Inventory != null) Player.Inventory.Changed += OnPlayerChanged;
            wasOnCooldown = Remaining() > 0;
            RefreshAll();
            enterRoutine = StartCoroutine(GreetAfterFade());
        }

        private IEnumerator GreetAfterFade()
        {
            int greetingTicket = talkTicket;
            // The chart still starts at reveal; its animation length does not control dialogue timing.
            while (SceneFadeTransition.IsRunning) yield return null;
            introTime = 0f;
            yield return BaseArrivalTutorial.WaitForArrivalDialogue();
            enterRoutine = null;
            if (!bound || leaving || !isActiveAndEnabled || greetingTicket != talkTicket
                || BaseArrivalTutorial.IsShowing) yield break;
            if (BaseArrivalTutorial.TryShowAfterTreatment(this, Player, transform)) yield break;
            Say(TreatmentTalkEvent.Enter, null);
        }

        private void Update()
        {
            UpdateTreatmentTutorialHint();
            if (!bound || leaving || BaseArrivalTutorial.IsShowing) return;
            float dt = Time.unscaledDeltaTime;
            AnimateIntro(dt);
            AnimatePortrait(dt);
            AnimateEffects(dt);
            UpdateTimer(dt);
            refreshTimer -= dt;
            if (refreshTimer <= 0f) { refreshTimer = statusRefreshInterval; RefreshSlots(); RefreshHealButton(); RefreshResources(); }

            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.escapeKey.wasPressedThisFrame && !InventoryUI.EscapeHandledThisFrame) { ReturnToBase(); return; }
            if (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame) AdvanceDialogue();
        }

        // ---------- Actions ----------

        private void UpdateTreatmentTutorialHint()
        {
            Button target = null;
            if (CanAct && screen != null && screen.gameObject.activeInHierarchy && introTime >= introSeconds
                && enterRoutine == null && Player.State.baseTutorialComplete)
            {
                if (!Player.State.baseFirstTreatmentComplete)
                    target = typing || holding ? tutorialAdvanceButton
                        : CurrentTimerState(Remaining()) == TimerState.Ready ? tutorialHealButton : null;
                else if (Player.State.baseTreatmentDialogueComplete && Player.State.potionsReadyAt <= 0)
                    target = tutorialBackButton;
            }
            if (target != null && treatmentTutorialHint == null) treatmentTutorialHint = gameObject.AddComponent<TutorialButtonHighlight>();
            if (treatmentTutorialHint != null) treatmentTutorialHint.SetTarget(target);
        }

        private bool CanAct => bound && !leaving && Player != null && Player.Initialized && !SceneFadeTransition.IsRunning && !BaseArrivalTutorial.IsShowing;

        // Temporary heal button (final trigger not designed yet). Kept public: old scene listeners call UseBooth.
        public void Heal()
        {
            if (!CanAct) return;
            PlayerInputReader.SuppressPointerForCurrentFrame();
            int before = Player.Health.CurrentHealth;
            double readyBefore = Player.State.clinicReadyAt;
            Player.Heal();
            bool healed = Player.State.clinicReadyAt != readyBefore;
            int restored = Math.Max(0, Player.Health.CurrentHealth - before);
            TreatmentTalkEvent result;
            if (healed) { result = TreatmentTalkEvent.HealDone; flashRemaining = .55f; wasOnCooldown = true; }
            else if (Remaining() > 0) result = TreatmentTalkEvent.HealOnCooldown;
            else if (Player.Health.CurrentHealth >= Player.Health.MaxHealth) result = TreatmentTalkEvent.HealNotNeeded;
            else result = TreatmentTalkEvent.HealUnavailable;
            ShowSystemMessage(Player.Message);
            if (healed && BaseArrivalTutorial.TryShowAfterTreatment(this, Player, transform))
            {
                // The story conversation owns the first successful treatment's dialogue.
                if (enterRoutine != null) StopCoroutine(enterRoutine);
                if (lineRoutine != null) StopCoroutine(lineRoutine);
                enterRoutine = lineRoutine = null;
                ++talkTicket;
                typing = holding = false;
                dialogueGroup.alpha = 0;
                dialogueGroup.blocksRaycasts = false;
            }
            else Say(result, Player.Message, restored);
            RefreshAll();
        }

        public void UseBooth() => Heal();

        public void UpgradeClinic()
        {
            if (!CanAct) return;
            PlayerInputReader.SuppressPointerForCurrentFrame();
            int before = ClinicLevel;
            bool hasNext = HideOutModel.Next(Player.State, Player.Settings.rules, Facility.Clinic) != null;
            Player.Upgrade(Facility.Clinic); // Existing transaction: validate, pay once, level +1, save model.
            FinishUpgrade(healCard, ClinicLevel > before, hasNext, TreatmentTalkEvent.UpgradeDone);
        }

        public void UpgradeClinicSpeed()
        {
            if (!CanAct) return;
            PlayerInputReader.SuppressPointerForCurrentFrame();
            int before = Player.State.clinicSpeedLevel;
            bool hasNext = HideOutModel.NextClinicSpeed(Player.State, Player.Settings.rules) != null;
            Player.UpgradeClinicSpeed(); // Same transaction shape; shortens the cooldown from the next heal.
            FinishUpgrade(speedCard, Player.State.clinicSpeedLevel > before, hasNext, TreatmentTalkEvent.SpeedUpgradeDone);
        }

        private void FinishUpgrade(StatCard card, bool upgraded, bool hadNext, TreatmentTalkEvent doneEvent)
        {
            TreatmentTalkEvent result;
            if (upgraded) { result = doneEvent; TurnDial(card, -1f); }
            else result = hadNext ? TreatmentTalkEvent.UpgradeShort : TreatmentTalkEvent.UpgradeMaxed;
            ShowSystemMessage(Player.Message);
            Say(result, Player.Message);
            RefreshAll();
        }

        public void DowngradeClinic() => TryDowngrade(healCard, true);

        public void DowngradeClinicSpeed() => TryDowngrade(speedCard, false);

        // "-" asks twice: the first press arms the button and says what will happen; a second press inside
        // downgradeConfirmSeconds lowers one level through HideOutPlayerBridge (partial refund of that level's cost).
        private void TryDowngrade(StatCard card, bool recovery)
        {
            if (!CanAct || card == null) return;
            PlayerInputReader.SuppressPointerForCurrentFrame();
            int level = recovery ? ClinicLevel : Player.State.clinicSpeedLevel;
            if (level <= 0)
            {
                ShowSystemMessage("더 낮출 강화 단계가 없습니다.");
                Say(TreatmentTalkEvent.DowngradeBlocked, null);
                return;
            }
            if (Time.unscaledTime > card.minusArmedUntil)
            {
                float rate = Mathf.Clamp01(Player.Settings.rules.downgradeRefundRate);
                card.minusArmedUntil = Time.unscaledTime + downgradeConfirmSeconds;
                ShowSystemMessage($"한 번 더 누르면 {card.title} 단계가 1 내려갑니다. "
                    + (rate > 0f ? $"코인·재료 {rate:P0} 반환" : "반환 없음"));
                RefreshSlots();
                return;
            }
            card.minusArmedUntil = 0f;
            bool lowered = recovery ? Player.DowngradeClinic() : Player.DowngradeClinicSpeed();
            if (lowered) TurnDial(card, 1f); // Back by one tooth.
            ShowSystemMessage(Player.Message);
            Say(lowered ? recovery ? TreatmentTalkEvent.HealDowngraded : TreatmentTalkEvent.SpeedDowngraded
                : TreatmentTalkEvent.DowngradeBlocked, Player.Message);
            RefreshAll();
        }

        // The dial turns by one tooth per level: -1 clockwise (up), +1 back (down).
        private static void TurnDial(StatCard card, float direction)
        {
            if (card?.dial == null) return;
            card.gearFrom = card.dial.localEulerAngles.z;
            card.gearTo = card.gearFrom + direction * 360f / GearTeeth;
            card.gearTime = 0f;
        }

        public void ReturnToBase()
        {
            if (!bound || leaving || SceneFadeTransition.IsRunning || BaseArrivalTutorial.IsShowing) return;
            leaving = true;
            Player.StartCoroutine(HideOutTravel.ExitTreatment(this));
        }

        // Called by HideOutTravel while the screen is black. The base re-applies SuspendPlayer itself.
        public void PrepareToLeave()
        {
            StopAllCoroutines();
            lineRoutine = enterRoutine = null;
            typing = holding = false;
            if (Player != null && Player.TryGetComponent<Rigidbody2D>(out var body)) body.linearVelocity = Vector2.zero;
            ReleaseControlLock(); // Direct Play only; travel entry froze the player through the base.
            if (screen != null) screen.gameObject.SetActive(false);
            roomCamera.gameObject.SetActive(false);
            inputContext.SetActive(false);
        }

        // Player bounds are not touched by this screen (the player is frozen and hidden), so nothing to restore.
        public void RestorePlayerBounds() { }

        private void ReleaseControlLock()
        {
            if (!ownsControlLock) return;
            ownsControlLock = false;
            if (controlLock != null) controlLock.Release();
        }

        // ---------- Dialogue ----------

        private bool SelenaPresent => Player != null && Player.Initialized && Player.State.selenaRecruited;

        private void Say(TreatmentTalkEvent talkEvent, string systemMessage, int healAmount = 0)
        {
            if (!SelenaPresent || dialogueGroup == null) return;
            var context = new TreatmentTalkContext(talkEvent, Player.Health.CurrentHealth, Player.Health.MaxHealth,
                ClinicLevel, healAmount, Remaining(), systemMessage);
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

        private int ClinicLevel => Player.State.levels[(int)Facility.Clinic];

        private double Remaining() => Player == null || !Player.Initialized ? 0 : Math.Max(0, Player.State.clinicReadyAt - Player.Now);

        private void OnPlayerChanged() { if (bound && !leaving) RefreshAll(); }

        private void RefreshAll()
        {
            if (screen == null || Player == null || !Player.Initialized) return;
            RefreshSlots();
            RefreshHealButton();
            RefreshResources();
            shownSeconds = -1; // Force the clock to rebuild.
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

        private TimerState CurrentTimerState(double remaining)
        {
            int health = Player.Health.CurrentHealth;
            if (remaining > 0) return TimerState.Cooldown;
            if (health <= 0) return TimerState.Blocked;
            return health >= Player.Health.MaxHealth ? TimerState.FullHealth : TimerState.Ready;
        }

        private void UpdateTimer(float dt)
        {
            if (Player == null || !Player.Initialized || gaugeFill == null) return;
            double remaining = Remaining();
            // The deadline was set with the cooldown at heal time; a later speed upgrade only shortens the next one.
            double total = Math.Max(Math.Max(1d, HideOutModel.ClinicCooldown(Player.State, Player.Settings.rules)), remaining);
            // Green = preparation progress: a heal empties the bar to the gray track, then green grows from
            // the left edge to the right until the booth is usable (then blue takes over, see UpdateReadyFill).
            float target = 1f - Mathf.Clamp01((float)(remaining / total));
            var state = CurrentTimerState(remaining);
            bool cooling = state == TimerState.Cooldown;
            // Snap on the first frame and when a new cooldown starts (back to gray); otherwise glide.
            displayedFill = displayedFill < 0f || cooling && !gaugeWasCooling ? target
                : Mathf.MoveTowards(displayedFill, target, dt * fillCatchUpSpeed);
            gaugeWasCooling = cooling;
            gaugeFill.anchorMax = new Vector2(cooling ? displayedFill : 0f, 1f);
            UpdateReadyFill(state, dt);
            int seconds = (int)Math.Ceiling(remaining);
            if (seconds != shownSeconds || state != shownState)
            {
                shownSeconds = seconds; shownState = state;
                clockLabel.text = FormatClock(seconds);
                stateLabel.text = state switch
                {
                    TimerState.Cooldown => "회복 준비 중",
                    TimerState.Ready => "치료 가능",
                    TimerState.FullHealth => "HP 가득 참",
                    _ => "치료 불가",
                };
                stateLabel.color = state == TimerState.Ready ? readyLabelColor : deepSageColor;
            }

            // Last seconds: the countdown blinks softly so the end is noticeable.
            clockLabel.alpha = state == TimerState.Cooldown && remaining <= urgentSeconds
                ? .5f + .5f * Mathf.Abs(Mathf.Cos(Time.unscaledTime * Mathf.PI * 1.5f)) : 1f;

            if (remaining > 0) wasOnCooldown = true;
            else if (wasOnCooldown)
            {
                wasOnCooldown = false;
                flashRemaining = Mathf.Max(flashRemaining, .35f);
                readySweep = 0f; // Time is up while watching: blue sweeps in from the left, then blinks twice.
                Say(TreatmentTalkEvent.CooldownReady, null);
                RefreshHealButton();
            }
        }

        // Blue = usable. During the cooldown it is empty; when the cooldown ends in the room it sweeps in
        // (readySweepSeconds, ease-out) and blinks twice; when the room opens already ready it is simply full.
        private void UpdateReadyFill(TimerState state, float dt)
        {
            float amount;
            if (state == TimerState.Cooldown) { amount = 0f; readySweep = readyBlink = -1f; }
            else if (readySweep >= 0f)
            {
                readySweep += dt;
                float k = Mathf.Clamp01(readySweep / readySweepSeconds);
                amount = 1f - (1f - k) * (1f - k) * (1f - k);
                if (k >= 1f) { readySweep = -1f; readyBlink = 0f; }
            }
            else amount = 1f;
            gaugeReady.anchorMax = new Vector2(amount, 1f);

            bool lit = false;
            if (readyBlink >= 0f)
            {
                readyBlink += dt;
                float k = readyBlink / readyBlinkSeconds;
                if (k >= 1f) readyBlink = -1f;
                else lit = Mathf.Abs(Mathf.Sin(k * Mathf.PI * 2f)) > .5f;
            }
            gaugeReadyImage.color = lit ? gaugeFlashColor : gaugeReadyColor;
        }

        private static string FormatClock(int totalSeconds)
        {
            totalSeconds = Math.Max(0, totalSeconds);
            return $"{totalSeconds / 3600:00}:{totalSeconds / 60 % 60:00}:{totalSeconds % 60:00}";
        }

        private void RefreshHealButton()
        {
            if (healButtonLabel == null || Player == null || !Player.Initialized) return;
            var state = CurrentTimerState(Remaining());
            bool ready = state == TimerState.Ready;
            healButtonLabel.text = state switch
            {
                TimerState.Ready => $"치료 받기 (+{HideOutModel.Healing(Player.State, Player.Settings.rules)} HP)",
                TimerState.Cooldown => "치료 받기 (대기 중)",
                TimerState.FullHealth => "치료 받기 (HP 가득)",
                _ => "치료 받기 (불가)",
            };
            healButtonImage.color = ready ? readyColor : WithAlpha(slateColor, .12f);
            healButtonLabel.color = ready ? Color.white : WithAlpha(slateColor, .5f);
            healCross.gameObject.SetActive(ready);
        }

        private void RefreshSlots()
        {
            if (healCard == null || Player == null || !Player.Initialized) return;
            var rules = Player.Settings.rules;
            var state = Player.State;

            // Effects are computed by HideOutModel on a probe one level higher, so the preview always matches the rules.
            var probe = JsonUtility.FromJson<HideOutSave>(JsonUtility.ToJson(state));
            int healNow = HideOutModel.Healing(state, rules);
            var healCost = HideOutModel.Next(state, rules, Facility.Clinic);
            probe.levels[(int)Facility.Clinic]++;
            string healEffect = healCost == null ? $"회복량 {healNow} HP"
                : $"회복량 {healNow} HP  <color=#{Hex(healStatColor)}>→  {HideOutModel.Healing(probe, rules)} HP</color>";
            RefreshCard(healCard, ClinicLevel, MaxLevel(rules, Facility.Clinic), healEffect, healCost);

            double speedNow = HideOutModel.ClinicCooldown(state, rules);
            var speedCost = HideOutModel.NextClinicSpeed(state, rules);
            probe.levels[(int)Facility.Clinic]--;
            probe.clinicSpeedLevel++;
            string speedEffect = speedCost == null ? $"치료 대기 {FormatShort(speedNow)}"
                : $"치료 대기 {FormatShort(speedNow)}  <color=#{Hex(speedStatColor)}>→  {FormatShort(HideOutModel.ClinicCooldown(probe, rules))}</color>";
            RefreshCard(speedCard, state.clinicSpeedLevel, rules.clinicSpeedUpgrades?.Length ?? 0, speedEffect, speedCost);
        }

        private void RefreshCard(StatCard card, int level, int max, string effect, UpgradeCost cost)
        {
            card.level.text = HideOutModel.DisplayLevel(level).ToString(); // Shown from Lv 1; pips still count upgrades.
            card.levelText.text = max > 0 ? $"Lv {HideOutModel.DisplayLevel(level)} / {HideOutModel.DisplayLevel(max)}" : $"Lv {HideOutModel.DisplayLevel(level)}";
            for (int i = 0; i < card.pips.Length; i++)
                card.pips[i].color = i < level ? card.color : WithAlpha(slateColor, .12f);
            card.effect.text = effect;
            card.plus.interactable = cost != null;
            card.plusGroup.alpha = cost != null ? 1f : .35f;
            bool canLower = level > 0, armed = canLower && Time.unscaledTime <= card.minusArmedUntil;
            card.minus.interactable = canLower;
            card.minusGroup.alpha = canLower ? 1f : .35f;
            card.minusInset.color = armed ? crossColor : linenColor;   // Armed: filled red, waiting for the second press.
            card.minusBar.color = armed ? Color.white : brassColor;
            card.maxed.gameObject.SetActive(cost == null);

            // Cost chips: coins, action points, then each material with its item icon. Short ones turn red.
            var state = Player.State;
            int used = 0;
            float x = 0f;
            if (cost != null)
            {
                x = SetChip(card.chips[used++], x, coinIcon, new Color(.82f, .67f, .38f), state.coins, cost.coins);
                x = SetChip(card.chips[used++], x, apIcon, new Color(.83f, .79f, .66f), state.actionPoints, cost.actionPoints, "AP ");
                foreach (var material in cost.materials ?? Array.Empty<MaterialCost>())
                {
                    if (material == null || string.IsNullOrEmpty(material.itemId) || used >= card.chips.Length) continue;
                    var item = Player.Settings.Find(material.itemId);
                    var icon = item != null && item.Icon != null ? item.Icon : materialFallback;
                    x = SetChip(card.chips[used++], x, icon, item != null && item.Icon != null ? Color.white : brassColor,
                        HideOutModel.Count(state.inventory, material.itemId), material.amount, Player.Settings.Name(material.itemId) + " ");
                }
            }
            for (int i = used; i < card.chips.Length; i++) card.chips[i].root.gameObject.SetActive(false);
        }

        private float SetChip(CostChip chip, float x, Sprite icon, Color iconTint, int owned, int required, string label = "")
        {
            bool short_ = owned < required;
            chip.root.gameObject.SetActive(true);
            chip.icon.sprite = icon; chip.icon.color = iconTint;
            chip.text.text = $"{label}{owned}/{required}";
            chip.text.color = short_ ? crossColor : slateColor;
            chip.back.color = short_ ? WithAlpha(crossColor, .13f) : WithAlpha(Color.white, .85f);
            float width = Mathf.Ceil(36f + chip.text.GetPreferredValues(chip.text.text).x + 12f);
            chip.root.anchoredPosition = new Vector2(x, chip.root.anchoredPosition.y);
            chip.root.sizeDelta = new Vector2(width, chip.root.sizeDelta.y);
            chip.text.rectTransform.sizeDelta = new Vector2(width - 36f, chip.text.rectTransform.sizeDelta.y);
            return x + width + 8f;
        }

        private static string Hex(Color color) => ColorUtility.ToHtmlStringRGB(color);

        private static string FormatShort(double seconds)
        {
            int total = Math.Max(0, (int)Math.Ceiling(seconds));
            return $"{total / 60}:{total % 60:00}";
        }

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
            if (chart == null) return;
            if (introTime < 0f) { chartGroup.alpha = 0f; portraitGroup.alpha = 0f; return; }
            if (introTime > introSeconds) return;
            introTime += dt;
            float t = Mathf.Clamp01(introTime / introSeconds), eased = 1f - (1f - t) * (1f - t) * (1f - t);
            portraitGroup.alpha = Mathf.Clamp01(t * 1.6f);
            chartGroup.alpha = eased;
            chart.anchoredPosition = chartBase + new Vector2(32f * (1f - eased), 0f);
        }

        private void AnimatePortrait(float dt)
        {
            if (portraitImage == null) return;
            breathTime += dt;
            float breath = 1f + breathAmplitude * Mathf.Sin(breathTime * 2f * Mathf.PI / breathPeriod);
            portraitImage.localScale = new Vector3(breath, breath, 1f); // Pivot at the bottom: the picture breathes inside its frame.
            if (typing) { talkTime += dt; bob = Mathf.Abs(Mathf.Sin(talkTime * 9f)) * talkBob; }
            else { talkTime = 0f; bob = Mathf.MoveTowards(bob, 0f, dt * talkBob * 6f); }
            portraitImage.anchoredPosition = portraitBase + new Vector2(0f, bob);
        }

        private void TurnGear(StatCard card, float dt)
        {
            if (card?.dial == null || card.gearTime < 0f) return;
            card.gearTime += dt;
            float t = Mathf.Clamp01(card.gearTime / gearTurnSeconds), eased = 1f - Mathf.Pow(1f - t, 3f);
            card.dial.localEulerAngles = new Vector3(0f, 0f, Mathf.LerpAngle(card.gearFrom, card.gearTo, eased));
            if (t >= 1f) card.gearTime = -1f;
        }

        private void AnimateEffects(float dt)
        {
            if (healFlashImage != null)
            {
                flashRemaining = Mathf.Max(0f, flashRemaining - dt);
                healFlashImage.color = WithAlpha(sageColor, .3f * Mathf.Clamp01(flashRemaining / .55f));
            }
            TurnGear(healCard, dt);
            TurnGear(speedCard, dt);
            if (continueMark != null && continueMark.gameObject.activeSelf)
                continueMark.color = WithAlpha(continueMarkBaseColor, continueMarkBaseColor.a * (.35f + .65f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f))));
            if (systemGroup != null && systemRemaining > 0f)
            {
                systemRemaining -= dt;
                systemGroup.alpha = Mathf.Clamp01(systemRemaining / .6f);
            }
        }

        // ---------- Layout (1600x900 reference, top-left coordinates; see treatment_design_mock.png) ----------
        //   [back]                                              [HP][MP][코인][AP]
        //   +-- portrait (brass) --+   +-- clinic chart (clipped) ---------------------+
        //   |                      |   | + 치료 부스                                    |
        //   |                      |   | 남은시간 00:09:43  [gauge ......]  회복 준비 % |
        //   |                      |   | [치료 받기]  system message                    |
        //   |                      |   | [(gear) + -  ♥ 회복량 강화   Lv ▮▮▯▯ / effect / cost chips] |
        //   +----------------------+   | [(gear) + -  ◷ 시간 단축     Lv ▮▯▯▯ / effect / cost chips] |
        //   [Selena]                   +-----------------------------------------------+
        //   +-- dialogue glass (brass rule) ----------------------------------------------+

        private void BuildScreen()
        {
            chamfer = MakeSprite(CreateChamferTexture(24, 6, true), new Vector4(8, 8, 8, 8));
            chamferTop = MakeSprite(CreateChamferTexture(24, 6, false), new Vector4(8, 8, 8, 8));
            gear = MakeSprite(CreateGearTexture(256, GearTeeth), Vector4.zero);
            softGlow = MakeSprite(CreateRadialTexture(128, false), Vector4.zero);
            vignette = MakeSprite(CreateRadialTexture(128, true), Vector4.zero);
            circle = MakeSprite(CreateCircleTexture(64), Vector4.zero);
            heartIcon = MakeSprite(CreateResourceTexture(0), Vector4.zero);
            coinIcon = MakeSprite(CreateResourceTexture(2), Vector4.zero);
            apIcon = MakeSprite(CreateResourceTexture(3), Vector4.zero);
            clockIcon = MakeSprite(CreateClockTexture(40), Vector4.zero);
            materialFallback = MakeSprite(CreateChamferTexture(24, 6, true), Vector4.zero);

            var root = new GameObject("Treatment Screen (runtime)", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            screen = root.GetComponent<Canvas>();
            screen.renderMode = RenderMode.ScreenSpaceOverlay;
            screen.sortingOrder = 300; // Above inventory/base UI; below story dialogue, fades and the cursor.
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = .5f;
            var t = root.transform;

            BuildBackdrop(t);
            BuildPortrait(t);
            var back = MakeButton(t, "Back to base", 40, 28, 250, 42, ReturnToBase, WithAlpha(slateColor, .85f), chamfer);
            tutorialBackButton = back;
            Label(back.transform, "← 기지로 돌아가기", 16, 0, 180, 42, 18, linenColor, TextAlignmentOptions.MidlineLeft);
            var key = Box(back.transform, "ESC key", 194, 11, 42, 20, WithAlpha(linenColor, .16f));
            Label(key.transform, "ESC", 0, 0, 42, 20, 13, WithAlpha(linenColor, .8f), TextAlignmentOptions.Center);
            BuildChart(t);
            BuildResourceHud(t);
            BuildDialogue(t);
            continueMarkBaseColor = continueMark != null ? continueMark.color : brassColor;

            healFlashImage = Box(t, "Heal flash", 0, 0, 0, 0, Color.clear);
            healFlash = healFlashImage.rectTransform;
            Stretch(healFlash);
        }

        private void BuildBackdrop(Transform t)
        {
            var wall = Box(t, "Wall", 0, 0, 1600, 900, wallColor);
            if (wallSprite != null)
            {
                wall.sprite = wallSprite; wall.color = Color.white;
                // Cover 1600x900 without stretching the pixel art.
                float aspect = wallSprite.rect.width / Mathf.Max(1f, wallSprite.rect.height);
                var size = aspect >= 1600f / 900f ? new Vector2(900f * aspect, 900f) : new Vector2(1600f, 1600f / aspect);
                wall.rectTransform.sizeDelta = size;
                wall.rectTransform.anchoredPosition = new Vector2((1600f - size.x) * .5f, -(900f - size.y) * .5f);
            }
            Stretch(Box(t, "Wall shade", 0, 0, 0, 0, WithAlpha(slateColor, wallShade)).rectTransform);
            var glow = Box(t, "Lamp glow", -110, -90, 900, 900, WithAlpha(sageColor, .3f));
            glow.sprite = glowSprite != null ? glowSprite : softGlow;
            var shade = Box(t, "Vignette", 0, 0, 0, 0, WithAlpha(Color.black, .7f));
            shade.sprite = vignette;
            Stretch(shade.rectTransform);
        }

        private void BuildPortrait(Transform t)
        {
            var group = new GameObject("Selena portrait", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
            group.SetParent(t, false); Stretch(group);
            portraitGroup = group.GetComponent<CanvasGroup>(); portraitGroup.blocksRaycasts = false;
            group.gameObject.SetActive(SelenaPresent);

            var frame = Box(group, "Brass frame", 64, 88, 556, 556, brassColor); frame.sprite = chamfer; frame.type = Image.Type.Sliced;
            var window = Box(group, "Portrait window - replaceable", 70, 94, 544, 544, deepSageColor);
            window.gameObject.AddComponent<RectMask2D>();
            if (portraitPrefab != null) { Instantiate(portraitPrefab, window.transform, false); return; }

            var sprite = selenaPortrait;
            if (sprite == null)
            {
                var library = Resources.Load<DialoguePortraitLibrary>("Dialogue/Portraits");
                if (library != null) sprite = library.selena;
            }
            var image = Box(window.transform, "Selena", 0, 0, 544, 544, Color.white);
            portraitImage = image.rectTransform;
            portraitImage.pivot = new Vector2(.5f, 0f); // Breathing grows from the bottom edge.
            if (sprite != null)
            {
                image.sprite = sprite;
                DialoguePortraitOutline.Apply(window, image);
                // Cover the square window, keeping the picture's aspect.
                float aspect = sprite.rect.width / Mathf.Max(1f, sprite.rect.height);
                portraitImage.sizeDelta = aspect >= 1f ? new Vector2(544f * aspect, 544f) : new Vector2(544f, 544f / aspect);
            }
            else
            {
                image.color = WithAlpha(linenColor, .25f);
                Label(window.transform, "셀레나 일러스트", 0, 250, 544, 44, 22, linenColor, TextAlignmentOptions.Center);
            }
            portraitBase = portraitImage.anchoredPosition = new Vector2(272f, -544f);
        }

        private void BuildChart(Transform t)
        {
            chart = new GameObject("Clinic chart", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
            chart.SetParent(t, false); Rect(chart, 668, 88, 872, 580);
            chartBase = chart.anchoredPosition;
            chartGroup = chart.GetComponent<CanvasGroup>();
            var c = chart.transform;

            var paper = Box(c, "Paper", 0, 0, 872, 580, linenColor); paper.sprite = chamfer; paper.type = Image.Type.Sliced;
            var clip = Box(c, "Clip", 376, -14, 120, 26, brassColor); clip.sprite = chamfer; clip.type = Image.Type.Sliced;
            Box(clip.transform, "Clip slot", 36, 9, 48, 8, Color.Lerp(brassColor, slateColor, .45f));

            // Header (HP / MP are read from the top-right resource cards, as in the base).
            Cross(c, 28, 24, 26, crossColor);
            Label(c, "치료 부스", 66, 14, 400, 44, 30, slateColor, TextAlignmentOptions.MidlineLeft, true);
            Rule(c, 66);

            // Cooldown: clock, readiness and a gauge that fills as the medicine gets ready.
            Label(c, "남은시간", 28, 84, 200, 24, 18, deepSageColor, TextAlignmentOptions.MidlineLeft);
            clockLabel = Label(c, "00:00:00", 26, 104, 500, 56, 42, slateColor, TextAlignmentOptions.MidlineLeft, true);
            clockLabel.characterSpacing = 4f;
            stateLabel = Label(c, "", 444, 122, 400, 30, 18, deepSageColor, TextAlignmentOptions.MidlineRight);
            var track = Box(c, "Gauge", 28, 164, 816, 22, WithAlpha(slateColor, .16f));
            gaugeFillImage = Box(track.transform, "Remaining fill", 0, 0, 0, 0, gaugeWaitColor);
            gaugeFill = gaugeFillImage.rectTransform;
            gaugeFill.anchorMin = Vector2.zero; gaugeFill.anchorMax = new Vector2(0f, 1f); gaugeFill.pivot = new Vector2(0f, .5f);
            gaugeFill.offsetMin = gaugeFill.offsetMax = Vector2.zero;
            gaugeReadyImage = Box(track.transform, "Ready fill", 0, 0, 0, 0, gaugeReadyColor);
            gaugeReady = gaugeReadyImage.rectTransform;
            gaugeReady.anchorMin = Vector2.zero; gaugeReady.anchorMax = new Vector2(0f, 1f); gaugeReady.pivot = new Vector2(0f, .5f);
            gaugeReady.offsetMin = gaugeReady.offsetMax = Vector2.zero;
            for (int i = 1; i < 10; i++) Box(track.transform, "Tick", 816f * i / 10f - 1f, 0, 2, 22, WithAlpha(linenColor, .55f));
            Box(track.transform, "Cap L", -6, -4, 6, 30, brassColor);
            Box(track.transform, "Cap R", 816, -4, 6, 30, brassColor);

            // Temporary heal trigger + the bridge's exact result (numbers live here, not in Selena's lines).
            var heal = MakeButton(c, "Heal", 28, 204, 280, 50, Heal, WithAlpha(slateColor, .18f), chamfer);
            tutorialHealButton = heal;
            healButtonImage = heal.GetComponent<Image>();
            healCross = Cross(heal.transform, 20, 16, 18, linenColor);
            healButtonLabel = Label(heal.transform, "치료 받기", 0, 0, 280, 50, 20, slateColor, TextAlignmentOptions.Center);
            var systemRoot = new GameObject("System message", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
            systemRoot.SetParent(c, false); Rect(systemRoot, 328, 204, 516, 50);
            systemGroup = systemRoot.GetComponent<CanvasGroup>(); systemGroup.alpha = 0f; systemGroup.blocksRaycasts = false;
            systemLabel = Label(systemRoot, "", 0, 0, 516, 50, 17, Color.Lerp(slateColor, linenColor, .45f), TextAlignmentOptions.MidlineLeft);

            // Stat cards: recovery amount (existing clinic level) and time reduction (clinicSpeedLevel).
            healCard = BuildStatCard(c, "Recovery card", 270, "회복량 강화", heartIcon, healStatColor, UpgradeClinic, DowngradeClinic,
                MaxLevel(Player.Settings.rules, Facility.Clinic));
            speedCard = BuildStatCard(c, "Time reduction card", 424, "시간 단축", clockIcon, speedStatColor, UpgradeClinicSpeed, DowngradeClinicSpeed,
                Player.Settings.rules.clinicSpeedUpgrades?.Length ?? 0);
        }

        // 832x142 card at (20, y) of the chart. + upgrades; - lowers one level after a second press (partial refund).
        private StatCard BuildStatCard(Transform chartRoot, string name, float y, string title, Sprite icon, Color color, Action upgrade, Action downgrade, int maxLevel)
        {
            var card = new StatCard { color = color, title = title };
            var back = Box(chartRoot, name, 20, y, 832, 142, WithAlpha(Color.white, .55f)); back.sprite = chamfer; back.type = Image.Type.Sliced;
            var r = back.transform;
            Box(r, "Stripe", 0, 12, 5, 118, color);

            card.dial = Gear(r, "Dial", 22, 15, 112, color);
            card.level = Label(r, "0", 22, 15, 112, 112, 30, slateColor, TextAlignmentOptions.Center, true);
            card.plus = SignButton(r, "Upgrade +", 150, 22, 44, true, true, upgrade);
            card.plusGroup = card.plus.gameObject.AddComponent<CanvasGroup>();
            card.minus = SignButton(r, "Downgrade -", 150, 76, 44, false, false, downgrade);
            card.minusGroup = card.minus.gameObject.AddComponent<CanvasGroup>();
            card.minusInset = card.minus.transform.Find("Inset").GetComponent<Image>();
            card.minusBar = card.minus.transform.Find("Bar H").GetComponent<Image>();

            var badge = Box(r, "Icon", 212, 17, 26, 26, color); badge.sprite = icon; badge.preserveAspect = true;
            Label(r, title, 246, 12, 280, 36, 24, slateColor, TextAlignmentOptions.MidlineLeft, true);
            int pips = Mathf.Clamp(maxLevel, 0, 12);
            float pipsWidth = pips > 0 ? pips * 16f + (pips - 1) * 4f : 0f, pipsX = 832f - 22f - pipsWidth;
            card.pips = new Image[pips];
            for (int i = 0; i < pips; i++)
            {
                card.pips[i] = Box(r, "Level pip", pipsX + i * 20f, 22, 16, 14, WithAlpha(slateColor, .12f));
                card.pips[i].sprite = chamfer; card.pips[i].type = Image.Type.Sliced; card.pips[i].pixelsPerUnitMultiplier = 2f;
            }
            card.levelText = Label(r, "", pipsX - 110f, 14, 100, 30, 17, deepSageColor, TextAlignmentOptions.MidlineRight);
            card.effect = Label(r, "", 212, 52, 600, 30, 20, slateColor, TextAlignmentOptions.MidlineLeft);

            var chipRow = new GameObject("Cost chips", typeof(RectTransform)).GetComponent<RectTransform>();
            chipRow.SetParent(r, false); Rect(chipRow, 212, 94, 600, 32);
            card.chips = new CostChip[4];
            for (int i = 0; i < card.chips.Length; i++)
            {
                var chipBack = Box(chipRow, "Cost chip", 0, 0, 100, 32, Color.white); chipBack.sprite = chamfer; chipBack.type = Image.Type.Sliced;
                chipBack.pixelsPerUnitMultiplier = 2f;
                var chipIcon = Box(chipBack.transform, "Icon", 9, 6, 20, 20, Color.white); chipIcon.preserveAspect = true;
                var chipText = Label(chipBack.transform, "", 36, 0, 64, 32, 17, slateColor, TextAlignmentOptions.MidlineLeft);
                chipText.textWrappingMode = TextWrappingModes.NoWrap;
                card.chips[i] = new CostChip { root = chipBack.rectTransform, back = chipBack, icon = chipIcon, text = chipText };
            }
            card.maxed = Label(r, "최대 단계까지 강화했습니다.", 212, 94, 600, 32, 18, deepSageColor, TextAlignmentOptions.MidlineLeft);
            card.maxed.gameObject.SetActive(false);
            return card;
        }

        // Same cards, position and colors as HideOutScene.BuildResourceHud (x 1095..1572, y 14..62), so the
        // HP / MP / coin / AP readout stays in the top-right corner while the player is in the treatment room.
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

        private void BuildDialogue(Transform t)
        {
            var prefab = Resources.Load<DialogueCanvasPrefabs>("Dialogue/CanvasPrefabs")?.treatmentCanvas;
            var template = prefab != null ? prefab.GetComponent<TreatmentDialogueView>() : null;
            if (template != null && template.IsValid)
            {
                var root = Instantiate(prefab, t, false);
                root.name = "TreatmentDialogueCanvas";
                // Root Canvas values are driven by Unity when the prefab is authored. As a nested
                // Canvas, fill the existing treatment screen instead of retaining a driven zero scale.
                var canvasRect = (RectTransform)root.transform;
                canvasRect.localScale = Vector3.one;
                canvasRect.localRotation = Quaternion.identity;
                Stretch(canvasRect);
                var view = root.GetComponent<TreatmentDialogueView>();
                dialogueGroup = view.group;
                bodyLabel = view.body;
                continueMark = view.continueMark;
                tutorialAdvanceButton = view.advance;
                view.speaker.text = speakerName;
                dialogueGroup.alpha = 0f;
                dialogueGroup.blocksRaycasts = false;
                continueMark.gameObject.SetActive(false);
                tutorialAdvanceButton.onClick.AddListener(AdvanceDialogue);
                return;
            }

            // Name tab above a dark glass box along the bottom; click / Space / Enter advances.
            var dialogueRoot = new GameObject("Selena dialogue", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
            dialogueRoot.SetParent(t, false); Stretch(dialogueRoot);
            dialogueGroup = dialogueRoot.GetComponent<CanvasGroup>(); dialogueGroup.alpha = 0f; dialogueGroup.blocksRaycasts = false;
            var tab = Box(dialogueRoot, "Name tab", 64, 648, 190, 44, sageColor); tab.sprite = chamferTop; tab.type = Image.Type.Sliced;
            Label(tab.transform, speakerName, 18, 0, 170, 44, 26, slateColor, TextAlignmentOptions.MidlineLeft, true);
            var box = Box(dialogueRoot, "Dialogue box", 40, 692, 1520, 176, WithAlpha(slateColor, .94f));
            box.sprite = chamfer; box.type = Image.Type.Sliced; box.raycastTarget = true;
            Box(box.transform, "Brass rule", 8, 0, 1504, 3, brassColor);
            var advance = box.gameObject.AddComponent<Button>(); advance.transition = Selectable.Transition.None;
            tutorialAdvanceButton = advance;
            advance.onClick.AddListener(AdvanceDialogue);
            bodyLabel = Label(box.transform, "", 32, 26, 1456, 130, 30, linenColor, TextAlignmentOptions.TopLeft);
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

        private void Rule(Transform parent, float y) => Box(parent, "Rule", 28, y, 816, 2, WithAlpha(slateColor, .14f));

        private Image Cross(Transform parent, float x, float y, float size, Color color)
        {
            var root = Box(parent, "Cross", x, y, size, size, Color.clear);
            float bar = Mathf.Round(size * .3f), offset = (size - bar) * .5f;
            Box(root.transform, "V", offset, 0, bar, size, color);
            Box(root.transform, "H", 0, offset, size, bar, color);
            return root;
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

        // +/- drawn with bars (font-independent). Filled brass = usable action; outline = not available.
        private Button SignButton(Transform parent, string name, float x, float y, float size, bool plus, bool filled, Action onClick)
        {
            var button = MakeButton(parent, name, x, y, size, size, onClick, brassColor, null);
            var ink = filled ? linenColor : brassColor;
            if (!filled) Box(button.transform, "Inset", 3, 3, size - 6, size - 6, linenColor);
            float bar = Mathf.Max(3f, Mathf.Round(size * .08f)), length = Mathf.Round(size * .45f);
            Box(button.transform, "Bar H", (size - length) * .5f, (size - bar) * .5f, length, bar, ink);
            if (plus) Box(button.transform, "Bar V", (size - bar) * .5f, (size - length) * .5f, bar, length, ink);
            if (onClick == null)
            {
                button.interactable = false;
                button.gameObject.AddComponent<CanvasGroup>().alpha = .35f;
            }
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

        // Clock face (ring + two hands) for the time-reduction stat.
        private static Texture2D CreateClockTexture(int size)
        {
            var texture = NewTexture(size);
            var pixels = new Color32[size * size];
            float center = (size - 1) * .5f, outer = size * .5f - 1f, inner = outer - size * .14f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - center, dy = y - center, r = Mathf.Sqrt(dx * dx + dy * dy);
                float ring = Mathf.Clamp01(outer - r + .5f) * Mathf.Clamp01(r - inner + .5f);
                bool minute = Mathf.Abs(dx) <= size * .05f && dy >= -size * .05f && dy <= inner * .78f;   // 12 o'clock
                bool hour = Mathf.Abs(dy) <= size * .05f && dx >= -size * .05f && dx <= inner * .55f;     // 3 o'clock
                float hub = Mathf.Clamp01(size * .09f - r + .5f);
                float alpha = Mathf.Max(ring, Mathf.Max(minute || hour ? 1f : 0f, hub));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * alpha));
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
                // 0..1 inside one tooth period: flat top in the middle, sloped flanks, gap at the ends.
                float profile = Mathf.Clamp01(Mathf.Min(phase - .12f, .88f - phase) * 6f);
                float edge = Mathf.Lerp(root, outer, profile);
                float body = Mathf.Clamp01(edge - r + .5f);
                float hole = Mathf.Clamp01(r - hub + .5f);
                float groove = Mathf.Clamp01(Mathf.Min(r - ringIn, ringOut - r) + .5f);
                float alpha = body * hole * (1f - groove);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * alpha));
            }
            Finish(texture, pixels);
            return texture;
        }

        // Soft radial falloff: bright centre (glow) or dark edges (vignette).
        private static Texture2D CreateRadialTexture(int size, bool edges)
        {
            var texture = NewTexture(size);
            var pixels = new Color32[size * size];
            float center = (size - 1) * .5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center)) / center;
                float a = edges ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.55f, 1.25f, d)) : Mathf.Pow(Mathf.Clamp01(1f - d), 2f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * a));
            }
            Finish(texture, pixels);
            return texture;
        }

        private void OnDestroy()
        {
            if (bound)
            {
                ReleaseControlLock();
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
