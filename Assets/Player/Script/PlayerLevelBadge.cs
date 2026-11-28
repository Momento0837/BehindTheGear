using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The EXP gauge: a thin bar along the bottom edge of the screen with the level on the left ("Lv 3") and the EXP
/// numbers in the middle ("34 / 49"). It fills as EXP is collected; when it is full it flashes, starts again from empty
/// and "LEVEL UP!" / "AP +1" rise above the player. (It replaced the round badge next to the player's head; the class
/// keeps its name so the script reference stays valid.)
/// Built in code on its own overlay canvas, below the inventory and menus. Hidden while the player is not drawn
/// (inside the base, during scene travel). Presentation only: the numbers live in PlayerLevel.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerLevelBadge : MonoBehaviour
{
    private const float BarHeight = 18f;           // in 1920x1080 reference units
    private const int CanvasOrder = 5;             // above the world, below item notices (20) and the inventory (50)
    private const int Ticks = 9;                   // marks at every 10 %
    private const float FillPerSecond = .9f;       // slowest fill speed, in bars per second
    private const float FlashSeconds = .22f;

    private static readonly Color Frame = new(.07f, .045f, .05f, .92f);
    private static readonly Color Track = new(.16f, .12f, .2f, .92f);
    private static readonly Color Fill = new(.62f, .5f, .96f);
    private static readonly Color FillTop = new(.82f, .74f, 1f);
    private static readonly Color Head = new(.96f, .92f, 1f);
    private static readonly Color Tick = new(.05f, .03f, .06f, .55f);
    private static readonly Color TextColor = new(1f, .95f, .85f);
    private static readonly Color LevelUpColor = new(1f, .93f, .62f);
    private static readonly Color ActionPointColor = new(.55f, .86f, 1f);

    private PlayerLevel level;
    private SpriteRenderer body;
    private GameObject canvasObject;
    private RectTransform fill, head;
    private Image fillImage, shine;
    private TMP_Text levelLabel, amountLabel;
    private int shownLevel = 1, labelledLevel = -1, labelledExperience = -1;
    private float shownProgress, flashUntil;
    private int pendingActionPoints;
    private float actionPointLabelAt = -1f;

    private void Awake()
    {
        level = GetComponent<PlayerLevel>();
        body = GetComponent<SpriteRenderer>();
        if (level == null) { enabled = false; return; }
        Build();
        shownLevel = level.Level;
        shownProgress = level.Progress;
        level.Changed += OnLevelDataChanged;
        level.Restored += ShowCurrent;
        level.LeveledUp += OnLeveledUp;
        Redraw();
    }

    private void OnDestroy()
    {
        if (level != null)
        {
            level.Changed -= OnLevelDataChanged;
            level.Restored -= ShowCurrent;
            level.LeveledUp -= OnLeveledUp;
        }
        if (canvasObject != null) Destroy(canvasObject);
    }

    private void OnEnable() { if (canvasObject != null) canvasObject.SetActive(true); }
    private void OnDisable() { if (canvasObject != null) canvasObject.SetActive(false); }

    private void Build()
    {
        // Not a child of the player: the player object moves between scenes and may be scaled or hidden.
        canvasObject = new GameObject("EXP Gauge", typeof(Canvas), typeof(CanvasScaler));
        DontDestroyOnLoad(canvasObject);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = CanvasOrder;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;

        var frame = Box("Frame", canvasObject.transform, Frame);
        frame.anchorMin = new Vector2(0, 0); frame.anchorMax = new Vector2(1, 0); frame.pivot = new Vector2(.5f, 0);
        frame.anchoredPosition = Vector2.zero; frame.sizeDelta = new Vector2(0, BarHeight);

        var track = Box("Track", frame, Track);
        track.anchorMin = Vector2.zero; track.anchorMax = Vector2.one;
        track.offsetMin = new Vector2(2, 2); track.offsetMax = new Vector2(-2, -2);

        fill = Box("Fill", track, Fill);
        fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(0, 1); fill.offsetMin = fill.offsetMax = Vector2.zero;
        fillImage = fill.GetComponent<Image>();
        var top = Box("Fill Highlight", fill, FillTop);
        top.anchorMin = new Vector2(0, 1); top.anchorMax = new Vector2(1, 1); top.pivot = new Vector2(.5f, 1);
        top.anchoredPosition = Vector2.zero; top.sizeDelta = new Vector2(0, 3);
        shine = top.GetComponent<Image>();
        head = Box("Fill Head", fill, Head);
        head.anchorMin = new Vector2(1, 0); head.anchorMax = new Vector2(1, 1); head.pivot = new Vector2(1, .5f);
        head.anchoredPosition = Vector2.zero; head.sizeDelta = new Vector2(3, 0);

        for (int i = 1; i <= Ticks; i++)
        {
            var tick = Box("Tick", track, Tick);
            float x = i / (Ticks + 1f);
            tick.anchorMin = new Vector2(x, 0); tick.anchorMax = new Vector2(x, 1);
            tick.anchoredPosition = Vector2.zero; tick.sizeDelta = new Vector2(2, 0);
        }

        levelLabel = Text("Level", frame, TextAlignmentOptions.Left);
        levelLabel.rectTransform.offsetMin = new Vector2(14, 0);
        amountLabel = Text("Amount", frame, TextAlignmentOptions.Center);
    }

    private static RectTransform Box(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image)); // an Image without a sprite is a plain box
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image.rectTransform;
    }

    private static TMP_Text Text(string name, Transform parent, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TMP_Text>();
        UIFontTheme.Apply(text, UIFontTheme.Normal);
        text.fontSize = 14; text.color = TextColor; text.alignment = alignment; text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.outlineWidth = .2f; text.outlineColor = new Color32(12, 8, 12, 255);
        var rect = text.rectTransform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        return text;
    }

    private void OnLevelDataChanged()
    {
        // Anything that lowers the numbers is shown at once; gains animate in Update.
        if (level.Level < shownLevel || level.Level == shownLevel && level.Progress < shownProgress) ShowCurrent();
    }

    // A loaded save jumps straight to its level: no fill animation and no "LEVEL UP!".
    private void ShowCurrent()
    {
        shownLevel = level.Level;
        shownProgress = level.Progress;
        pendingActionPoints = 0;
        actionPointLabelAt = -1f;
        Redraw();
    }

    private void OnLeveledUp(int newLevel, int actionPoints) => pendingActionPoints += Mathf.Max(0, actionPoints);

    private void Update()
    {
        bool levelPending = shownLevel < level.Level;
        float target = levelPending ? 1f : level.Progress;
        if (levelPending || !Mathf.Approximately(shownProgress, target))
        {
            // Several levels at once (a boss) should not take seconds: speed up with the amount still to show.
            float backlog = level.Level - shownLevel + Mathf.Abs(target - shownProgress);
            shownProgress = Mathf.MoveTowards(shownProgress, target, FillPerSecond * Mathf.Max(1f, backlog * 1.6f) * Time.deltaTime);
            if (levelPending && shownProgress >= 1f)
            {
                shownLevel++;
                shownProgress = 0f;
                PlayLevelUp();
            }
        }
        Redraw();

        if (actionPointLabelAt > 0f && Time.time >= actionPointLabelAt)
        {
            actionPointLabelAt = -1f;
            if (pendingActionPoints > 0)
                MonsterDamageNumbers.ShowLabel(AbovePlayer(.55f), "AP +" + pendingActionPoints, ActionPointColor, .95f);
            pendingActionPoints = 0;
        }
    }

    private void LateUpdate()
    {
        // Follow the player sprite: hidden while the body is not drawn (inside the base, portal travel, ...).
        bool visible = body == null || body.enabled && !body.forceRenderingOff;
        if (canvasObject.activeSelf != visible) canvasObject.SetActive(visible);
    }

    private void PlayLevelUp()
    {
        flashUntil = Time.time + FlashSeconds;
        MonsterDamageNumbers.ShowLabel(AbovePlayer(.2f), "LEVEL UP!", LevelUpColor, 1.15f);
        // One "AP +n" for everything gained in this burst, after "LEVEL UP!" (0.8 s label) has faded.
        if (shownLevel >= level.Level) actionPointLabelAt = Time.time + .8f;
    }

    private Vector3 AbovePlayer(float above) => transform.position + new Vector3(0f, 1.5f + above, 0f);

    private void Redraw()
    {
        fill.anchorMax = new Vector2(Mathf.Clamp01(shownProgress), 1f);
        bool flash = Time.time < flashUntil;
        fillImage.color = flash ? Color.white : Fill;
        shine.color = flash ? Color.white : FillTop;
        head.gameObject.SetActive(shownProgress > .004f && shownProgress < .999f);

        // While the gauge is still catching up, the numbers follow the gauge so both tell the same story.
        bool caughtUp = shownLevel >= level.Level;
        int need = PlayerLevel.Table.ExperienceToNext(shownLevel);
        int experience = need <= 0 ? 0 : caughtUp && Mathf.Approximately(shownProgress, level.Progress)
            ? level.Experience : Mathf.Clamp(Mathf.RoundToInt(shownProgress * need), 0, need);
        if (shownLevel == labelledLevel && experience == labelledExperience) return;
        labelledLevel = shownLevel; labelledExperience = experience;
        levelLabel.text = "Lv " + shownLevel;
        amountLabel.text = need <= 0 ? "MAX" : experience + " / " + need;
    }
}
