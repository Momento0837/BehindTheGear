using System;
using System.IO;
using BehindTheGear.HideOut;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ESC pause for gameplay scenes: 계속하기 · 메인 메뉴로 · 게임 종료.
/// Leaving warns "저장이 되지 않았습니다" when the last save-point save (or the start of this play
/// session, if nothing was saved since) is more than <see cref="UnsavedWarningSeconds"/> old.
/// Built at runtime like the save menu; no scene or prefab changes. Screens that already use ESC
/// (inventory, dialogue, facility rooms, cutscenes, fades) keep it: the pause only opens when none is active.
/// </summary>
[DefaultExecutionOrder(-500)] // Update runs before player input so a paused frame never reaches gameplay.
public sealed class GamePauseMenu : MonoBehaviour
{
    public const float UnsavedWarningSeconds = 30f;
    private const string MenuSceneName = "ControlSchemeSelect";
    private enum View { Main, Warning }
    private enum Leave { Menu, Quit }

    private static GamePauseMenu instance;
    private static DateTime sessionStartUtc = DateTime.UtcNow;
    private static bool sessionNeedsStart = true;

    private PlayerMovement2D player;
    private float nextPlayerSearch;
    private bool open;
    private View view;
    private Leave pendingLeave;
    private IDisposable pauseLease;
    private PlayerControlLock heldLock;
    private EventSystem ownEventSystem;
    private GameObject lastSelection;

    private Canvas canvas;
    private SaveConfirmGraphic dialogGraphic;
    private TMP_Text title, body;
    private GameObject mainGroup, warningGroup;
    private Button continueButton, menuButton, quitButton, leaveButton, backButton;
    private TMP_Text leaveLabel;

    public static bool IsOpen => instance != null && instance.open;

    /// <summary>A player is in a gameplay scene (not the start menu). WindowsQuitOnEscape stops its
    /// hold-to-quit here so leaving always goes through this menu's unsaved-progress warning.</summary>
    public static bool InGameplay => instance != null && instance.PlayerPresent()
        && SceneManager.GetActiveScene().name != MenuSceneName;

    // ---------- Lifetime (survives scene loads; recreated after SaveGameSession disposes the player scene) ----------
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null; sessionNeedsStart = true; sessionStartUtc = DateTime.UtcNow;
        SceneManager.sceneLoaded -= SceneLoaded; SceneManager.sceneLoaded += SceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot() => Ensure();

    private static void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single)
        {
            // A play session starts when gameplay is entered from the start menu (새 게임 / 이어하기),
            // or at the first gameplay scene when the editor starts directly in one.
            if (scene.name == MenuSceneName) sessionNeedsStart = true;
            else if (sessionNeedsStart) { sessionNeedsStart = false; sessionStartUtc = DateTime.UtcNow; }
            if (instance != null && instance.open) instance.Close(); // Never carry a pause into a new scene.
        }
        Ensure();
    }

    private static void Ensure()
    {
        if (instance != null) return;
        var host = new GameObject("Game Pause Menu");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<GamePauseMenu>();
    }

    private void OnDisable() => Close(); // Disposed with the player (메인 메뉴로) or the play session ending.
    private void OnDestroy() { if (instance == this) instance = null; }

    // ---------- Save recency ----------
    /// <summary>Seconds since progress was last safe on disk: the last save-point save, or the start of this
    /// play session when nothing has been saved since (new game / continue start out safe).</summary>
    public static double SecondsSinceSafe => (DateTime.UtcNow - LastSafeUtc(out _)).TotalSeconds;

    private static DateTime LastSafeUtc(out bool savedThisSession)
    {
        DateTime safe = sessionStartUtc;
        savedThisSession = false;
        try
        {
            // The bridge knows the real slot (sandbox runs save elsewhere); before the hideout, the menu slot.
            var bridge = FindFirstObjectByType<HideOutPlayerBridge>();
            string path = bridge != null ? bridge.SavePath : ManualSaveStore.SlotPath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                DateTime written = File.GetLastWriteTimeUtc(path);
                if (written >= sessionStartUtc) { safe = written; savedThisSession = true; }
            }
        }
        catch (Exception) { /* Unreadable slot: fall back to the session start. */ }
        return safe;
    }

    // ---------- Per-frame ----------
    private void Update()
    {
        if (!open) return;
        // Controls are locked, but suppress this frame too in case the lock does not cover every input.
        if (player != null && player.TryGetComponent(out PlayerInputReader reader)) reader.SuppressGameplayForCurrentFrame();
        KeepFocusInDialog();
    }

    private void LateUpdate()
    {
        // Runs after every Update, so ESC already consumed this frame (e.g. closing the inventory) is visible.
        var keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return;
        if (open)
        {
            if (view == View.Warning) ShowView(View.Main);
            else Close();
            return;
        }
        if (CanOpen()) Open();
    }

    private bool PlayerPresent()
    {
        if (player != null && player.gameObject.activeInHierarchy) return true;
        if (Time.unscaledTime < nextPlayerSearch) return false;
        nextPlayerSearch = Time.unscaledTime + .5f;
        player = FindFirstObjectByType<PlayerMovement2D>();
        return player != null && player.gameObject.activeInHierarchy;
    }

    /// <summary>The pause only takes ESC when no other screen is using it and nothing else owns the player.</summary>
    private bool CanOpen()
    {
        if (!InGameplay || Time.timeScale == 0f) return false; // Another pause (fade, cutscene) is active.
        if (SceneFadeTransition.IsRunning || CutsceneVideoSequence.IsAnyVisible || DialoguePresentation.IsActive) return false;
        if (InventoryUI.EscapeHandledThisFrame || PlayerSkillDevelopmentMenu.IsOpen || BaseArrivalTutorial.IsShowing) return false;
        if (BedRoomScene.IsActive || TreatmentRoomScene.IsActive || ScareCrowTrainingScene.IsActive || SelenaBaseDialogue.IsShowing) return false;
        if (PortalArrival.IsEntering || HideOutTravel.IsTravelling) return false;
        var inventory = player.GetComponent<InventoryUI>();
        if (inventory != null && inventory.IsOpen) return false;
        var controlLock = player.GetComponent<PlayerControlLock>();
        if (controlLock != null && controlLock.IsLocked) return false; // A story event or UI already holds the player.
        var health = player.GetComponent<PlayerHealth>();
        return health == null || !health.IsDead;
    }

    // ---------- Open / close ----------
    private void Open()
    {
        BuildUi();
        EnsureEventSystem();
        open = true;
        pauseLease = GameplayTimePause.Acquire();
        heldLock = player.TryGetComponent(out PlayerControlLock controlLock) ? controlLock : null;
        if (heldLock != null) heldLock.Acquire();
        canvas.gameObject.SetActive(true);
        dialogGraphic.PlayIntro();
        ShowView(View.Main);
    }

    private void Close()
    {
        if (!open) return;
        open = false;
        pauseLease?.Dispose(); pauseLease = null;
        if (heldLock != null) heldLock.Release();
        heldLock = null;
        if (canvas != null) canvas.gameObject.SetActive(false);
        if (EventSystem.current != null && lastSelection != null) EventSystem.current.SetSelectedGameObject(null);
        lastSelection = null;
        if (ownEventSystem != null) { Destroy(ownEventSystem.gameObject); ownEventSystem = null; }
    }

    private void RequestLeave(Leave leave)
    {
        pendingLeave = leave;
        if (SecondsSinceSafe > UnsavedWarningSeconds) ShowView(View.Warning);
        else LeaveNow();
    }

    private void LeaveNow()
    {
        if (pendingLeave == Leave.Menu)
        {
            // Stays paused until the fade takes over; disposing the player scene closes this menu.
            SaveGameSession.ReturnToMenu();
            return;
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        GameSoundService.QuitAfterMenuSound();
#endif
    }

    private void ShowView(View next)
    {
        view = next;
        mainGroup.SetActive(next == View.Main);
        warningGroup.SetActive(next == View.Warning);
        dialogGraphic.SetTone(next == View.Warning ? SaveConfirmGraphic.Tone.Danger : SaveConfirmGraphic.Tone.Caution);
        lastSelection = null;
        if (next == View.Main)
        {
            title.text = "일시정지";
            body.text = "";
            Select(continueButton);
            return;
        }
        title.text = "저장이 되지 않았습니다";
        double seconds = (DateTime.UtcNow - LastSafeUtc(out bool savedThisSession)).TotalSeconds;
        body.text = (savedThisSession
                ? $"마지막 저장 후 {ElapsedWithParticle(seconds)} 지났습니다.\n지금 나가면 그 뒤의 진행은 사라집니다."
                : "이번 플레이에서 아직 저장하지 않았습니다.\n지금 나가면 진행한 내용이 사라집니다.")
            + "\n<size=80%>세이브 포인트에서 저장할 수 있습니다.</size>";
        leaveLabel.text = pendingLeave == Leave.Menu ? "메인 메뉴로" : "게임 종료";
        Select(backButton);
    }

    /// <summary>"45초가" / "3분이" / "1시간 5분이" — the subject particle follows the last unit.</summary>
    public static string ElapsedWithParticle(double seconds)
    {
        int total = Math.Max(0, (int)seconds);
        if (total < 60) return $"{total}초가";
        if (total < 3600) return $"{total / 60}분이";
        return total % 3600 / 60 == 0 ? $"{total / 3600}시간이" : $"{total / 3600}시간 {total % 3600 / 60}분이";
    }

    private void KeepFocusInDialog()
    {
        if (EventSystem.current == null || canvas == null) return;
        var selected = EventSystem.current.currentSelectedGameObject;
        if (selected != null && selected.activeInHierarchy && selected.transform.IsChildOf(canvas.transform)) { lastSelection = selected; return; }
        var previous = lastSelection != null && lastSelection.activeInHierarchy ? lastSelection.GetComponent<Selectable>() : null;
        Select(previous != null ? previous : view == View.Main ? continueButton : backButton);
    }

    private static void Select(Selectable target)
    {
        if (EventSystem.current != null && target != null && target.gameObject.activeInHierarchy)
            EventSystem.current.SetSelectedGameObject(target.gameObject);
    }

    private void EnsureEventSystem()
    {
        if (EventSystem.current != null || FindFirstObjectByType<EventSystem>() != null) return;
        // Only while paused, and as a child of this host, so scenes that bring their own never see two.
        var events = new GameObject("Pause EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        events.transform.SetParent(transform, false);
        events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        ownEventSystem = events.GetComponent<EventSystem>();
    }

    // ---------- Construction (same dialog/button styles as the save menu) ----------
    private void BuildUi()
    {
        if (canvas != null) return;
        var root = new GameObject("Pause Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000; // Above HUD and inventory; the game cursor stays on top.
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 900);
        scaler.matchWidthOrHeight = .5f;

        var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image));
        dim.transform.SetParent(root.transform, false);
        Stretch((RectTransform)dim.transform);
        dim.GetComponent<Image>().color = new Color(.022f, .012f, .009f, .78f); // Also blocks clicks to the world.

        var dialog = new GameObject("Pause Panel", typeof(RectTransform), typeof(CanvasGroup), typeof(SaveConfirmGraphic));
        dialog.transform.SetParent(root.transform, false);
        var rect = (RectTransform)dialog.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(620, 390);
        dialogGraphic = dialog.GetComponent<SaveConfirmGraphic>();
        dialogGraphic.Configure(SaveConfirmGraphic.Mode.Panel);

        title = Label(dialog.transform, "일시정지", 30);
        Place(title.rectTransform, 0, -42, 540, 46);
        title.color = new Color(.93f, .86f, .74f);
        body = Label(dialog.transform, "", 21);
        Place(body.rectTransform, 0, -100, 560, 170);
        body.color = new Color(.86f, .80f, .70f);
        body.lineSpacing = 12f;
        body.textWrappingMode = TextWrappingModes.Normal;

        mainGroup = Group(dialog.transform, "Main");
        continueButton = MenuButton(mainGroup.transform, "계속하기", -116, Close, true, 300);
        menuButton = MenuButton(mainGroup.transform, "메인 메뉴로", -194, () => RequestLeave(Leave.Menu), false, 300);
        quitButton = MenuButton(mainGroup.transform, "게임 종료", -272, () => RequestLeave(Leave.Quit), false, 300);
        Column(continueButton, menuButton, quitButton);

        warningGroup = Group(dialog.transform, "Warning");
        leaveButton = MenuButton(warningGroup.transform, "게임 종료", -290, LeaveNow, true, 230, -130);
        leaveButton.GetComponent<SaveConfirmButtonStyle>().SetTone(SaveConfirmGraphic.Tone.Danger);
        leaveLabel = leaveButton.GetComponentInChildren<TMP_Text>();
        backButton = MenuButton(warningGroup.transform, "돌아가기", -290, () => ShowView(View.Main), false, 230, 130);
        Row(leaveButton, backButton);

        root.SetActive(false);
    }

    private static TMP_Text Label(Transform parent, string value, int size)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var label = go.GetComponent<TMP_Text>();
        UIFontTheme.Apply(label, UIFontTheme.Normal);
        label.text = value; label.fontSize = size; label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false; label.richText = true;
        Stretch(label.rectTransform);
        return label;
    }

    private static GameObject Group(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Stretch((RectTransform)go.transform);
        return go;
    }

    private static Button MenuButton(Transform parent, string text, float top, UnityEngine.Events.UnityAction action,
        bool confirm, float width, float x = 0)
    {
        var go = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        Place((RectTransform)go.transform, x, top, width, 60);
        var label = Label(go.transform, text, 23);
        var button = go.GetComponent<Button>();
        button.onClick.AddListener(action);
        go.AddComponent<SaveConfirmButtonStyle>().Configure(label, confirm);
        return button;
    }

    private static void Place(RectTransform rect, float x, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1); rect.pivot = new Vector2(.5f, 1);
        rect.anchoredPosition = new Vector2(x, top); rect.sizeDelta = new Vector2(width, height);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static void Column(params Selectable[] items)
    {
        for (int i = 0; i < items.Length; i++)
            items[i].navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = items[(i + items.Length - 1) % items.Length],
                selectOnDown = items[(i + 1) % items.Length],
            };
    }

    private static void Row(Selectable left, Selectable right)
    {
        left.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = right, selectOnRight = right };
        right.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = left, selectOnRight = left };
    }
}
