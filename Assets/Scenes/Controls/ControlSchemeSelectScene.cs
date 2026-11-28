using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Entry menu and direct new-game transition; controls are fixed to WASD/mouse.</summary>
public sealed class ControlSchemeSelectScene : MonoBehaviour
{
    [SerializeField] private string gameSceneName = "Forest";
    [SerializeField, Min(.01f)] private float fadeDuration = .8f;
    [SerializeField] private MainMenuController mainMenuPrefab;
    [SerializeField] private GameObject controlSelectionCanvas;
    [Header("First launch presentation")]
    [SerializeField] private GameObject warningPrefab;
    [SerializeField] private GameObject teamLogoPrefab;
    [SerializeField] private AudioClip mainBgm;
    private static bool introShown;
    public static bool IsWarningVisible { get; private set; }
    private GameObject introRoot;
    private Coroutine startupRoutine;
    private AudioSource music;
    private BackgroundMusicTrack.ScreenFadeLease menuMusicFade;
    private MainMenuController mainMenu;
    private bool showingMainMenu;
    private bool selecting;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetIntro() { introShown = false; IsWarningVisible = false; }

    private void Awake()
    {
        if (controlSelectionCanvas != null)
        {
            controlSelectionCanvas.SetActive(false);
            Destroy(controlSelectionCanvas);
        }
        if (mainMenuPrefab == null)
        {
            Debug.LogError("[Startup] Assign the main menu prefab.", this);
            return;
        }

        GetComponent<ManualSaveMenu>()?.SetVisible(false);
        showingMainMenu = true;
        music = gameObject.AddComponent<AudioSource>();
        music.playOnAwake = false;
        music.loop = true;
        music.spatialBlend = 0;
        music.clip = mainBgm;
        BackgroundMusicTrack.Attach(music);
        bool playIntro = !introShown;
        introShown = true;
        startupRoutine = StartCoroutine(PlayStartup(playIntro));
    }

    private void ShowMainMenu()
    {
        mainMenu = Instantiate(mainMenuPrefab);
        mainMenu.StartRequested += ShowControls;
        mainMenu.QuitRequested += StopMusic;
        if (mainBgm != null) music.Play();
    }

    private IEnumerator PlayStartup(bool playIntro)
    {
        introRoot = new GameObject("Startup Black Screen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        introRoot.transform.SetParent(transform, false);
        var canvas = introRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;
        var scaler = introRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        var black = new GameObject("Black", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        black.transform.SetParent(introRoot.transform, false);
        black.rectTransform.anchorMin = Vector2.zero;
        black.rectTransform.anchorMax = Vector2.one;
        black.rectTransform.offsetMin = black.rectTransform.offsetMax = Vector2.zero;
        black.color = Color.black;
        black.raycastTarget = true;
        // Present one completely black frame before the first warning fade.
        yield return null;
        if (playIntro)
        {
            if (warningPrefab != null)
            {
                var warning = Instantiate(warningPrefab, introRoot.transform, false);
                // Authored screen-space canvas roots can serialize a zero scale/size.
                // Once nested, they must explicitly fill the parent canvas.
                var warningRect = warning.GetComponent<RectTransform>();
                warningRect.localScale = Vector3.one;
                warningRect.localRotation = Quaternion.identity;
                warningRect.anchorMin = Vector2.zero;
                warningRect.anchorMax = Vector2.one;
                warningRect.pivot = new Vector2(.5f, .5f);
                warningRect.offsetMin = warningRect.offsetMax = Vector2.zero;
                var warningCanvas = warning.GetComponent<Canvas>();
                if (warningCanvas != null) { warningCanvas.overrideSorting = true; warningCanvas.sortingOrder = 32761; }
                var groupRoot = warning.transform.Find("Group");
                var group = (groupRoot != null ? groupRoot.gameObject : warning).AddComponent<CanvasGroup>();
                group.alpha = 0;
                group.interactable = group.blocksRaycasts = false;
                IsWarningVisible = true;
                try { yield return PresentWarning(group); }
                finally { IsWarningVisible = false; }
                warning.SetActive(false);
                Destroy(warning);
            }
            yield return new WaitForSecondsRealtime(1.5f);
            if (teamLogoPrefab != null)
            {
                var logo = Instantiate(teamLogoPrefab, introRoot.transform, false);
                var rect = logo.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
                rect.anchoredPosition = new Vector2(5f, 0f);
                var group = logo.AddComponent<CanvasGroup>();
                group.interactable = group.blocksRaycasts = false;
                yield return FadeGroup(group, 1, 0, 1);
                logo.SetActive(false);
                Destroy(logo);
            }
            yield return new WaitForSecondsRealtime(1.5f);
        }
        ShowMainMenu();
        // This visual cover must never intercept menu clicks or pause pointer parallax.
        black.raycastTarget = false;
        introRoot.GetComponent<GraphicRaycaster>().enabled = false;
        var cover = introRoot.AddComponent<CanvasGroup>();
        cover.interactable = cover.blocksRaycasts = false;
        yield return new WaitForSecondsRealtime(1.5f);
        yield return FadeGroup(cover, 1, 0, 10f);
        RemoveIntroCover();
        startupRoutine = null;
    }

    private static bool WarningSkipPressed()
    {
        var keyboard = Keyboard.current;
        return Mouse.current?.leftButton.wasPressedThisFrame == true
            || (keyboard != null && (keyboard.spaceKey.wasPressedThisFrame
                || keyboard.fKey.wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame));
    }

    private static IEnumerator PresentWarning(CanvasGroup group)
    {
        const float fadeSeconds = 2.5f;
        bool skipped = false;
        for (float elapsed = 0f; elapsed < fadeSeconds; elapsed += Time.unscaledDeltaTime)
        {
            if (WarningSkipPressed()) { skipped = true; break; }
            group.alpha = Mathf.Clamp01(elapsed / fadeSeconds);
            yield return null;
        }
        if (!skipped)
        {
            group.alpha = 1f;
            for (float elapsed = 0f; elapsed < 2f; elapsed += Time.unscaledDeltaTime)
            {
                if (WarningSkipPressed()) break;
                yield return null;
            }
        }
        // Fade from the visible opacity without flashing to full white first.
        float currentAlpha = group.alpha;
        yield return FadeGroup(group, currentAlpha, 0f, fadeSeconds * currentAlpha);
    }

    private void RemoveIntroCover()
    {
        if (introRoot == null) return;
        introRoot.SetActive(false);
        Destroy(introRoot);
        introRoot = null;
    }

    private static IEnumerator FadeGroup(CanvasGroup group, float from, float to, float duration)
    {
        group.alpha = from;
        yield return null;
        for (float elapsed = 0; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        {
            group.alpha = Mathf.Lerp(from, to, elapsed / duration);
            yield return null;
        }
        group.alpha = to;
    }

    private void StopMusic() { if (music != null) music.Stop(); }
    public IEnumerator FadeOutMenuMusic()
    {
        RemoveIntroCover();
        introRoot = new GameObject("Save Departure Black Screen", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        introRoot.transform.SetParent(transform, false);
        var canvas = introRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;
        var black = new GameObject("Black", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        black.transform.SetParent(introRoot.transform, false);
        black.rectTransform.anchorMin = Vector2.zero;
        black.rectTransform.anchorMax = Vector2.one;
        black.rectTransform.offsetMin = black.rectTransform.offsetMax = Vector2.zero;
        black.color = new Color(0, 0, 0, 0);
        black.raycastTarget = true;

        float screenDuration = Mathf.Max(.01f, fadeDuration);
        menuMusicFade = BackgroundMusicTrack.BeginScreenFade();
        try
        {
            menuMusicFade.SetProgress(0f);
            for (float elapsed = 0; elapsed < screenDuration; elapsed += Time.unscaledDeltaTime)
            {
                float progress = Mathf.Clamp01(elapsed / screenDuration);
                black.color = new Color(0, 0, 0, progress);
                menuMusicFade.SetProgress(progress);
                yield return null;
            }
            black.color = Color.black;
            menuMusicFade.SetProgress(1f);
            // Present an opaque frame before handing off to the actual scene transition.
            yield return null;
        }
        finally
        {
            menuMusicFade?.Dispose();
            menuMusicFade = null;
        }
        // Keep this cover opaque until the scene transition takes over and unloads the menu.
    }

    public void ResumeMenuMusic()
    {
        RemoveIntroCover();
        if (music == null || mainBgm == null) return;
        music.volume = 1;
        if (!music.isPlaying) music.Play();
    }

    private void ShowControls()
    {
        if (startupRoutine != null) { StopCoroutine(startupRoutine); startupRoutine = null; }
        StartCoroutine(ShowControlsRoutine());
    }

    private IEnumerator ShowControlsRoutine()
    {
        yield return SceneFadeTransition.FadeThrough(() =>
        {
            RemoveIntroCover();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            mainMenu.gameObject.SetActive(false);
            var saveMenu = GetComponent<ManualSaveMenu>();
            if (saveMenu != null) saveMenu.SetVisible(true);
        }, fadeDuration, fadeMusic: false);
        showingMainMenu = false;
        Destroy(mainMenu.gameObject);
        if (GetComponent<ManualSaveMenu>() == null) StartNewGame();
    }

    public void ShowControlsForNewGame() => StartNewGame();

    public void SelectWasdMouseQe() => StartNewGame();

    // Old editor automation entry point; it cannot select an alternate layout.
    public void SelectArrowKeys() => StartNewGame();

    public void StartNewGame()
    {
        if (showingMainMenu || selecting || SceneFadeTransition.IsRunning) return;
        var menu = GetComponent<ManualSaveMenu>();
        if (menu != null && !menu.ChoosingControls) return;
        selecting = true;
        SceneFadeTransition.LoadScene(gameSceneName, fadeDuration, success =>
        {
            if (!success) selecting = false;
        });
    }

    private void OnDestroy()
    {
        IsWarningVisible = false;
        menuMusicFade?.Dispose();
        menuMusicFade = null;
    }
}
