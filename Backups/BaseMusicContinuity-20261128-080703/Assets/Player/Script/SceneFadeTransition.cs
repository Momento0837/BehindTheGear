using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Fades to black before a scene load and reveals the new scene afterward.</summary>
public sealed class SceneFadeTransition : MonoBehaviour
{
    private static SceneFadeTransition instance;
    [SerializeField] private Image fadeImage;
    private bool running;
    private IDisposable worldPause;
    private Camera transitionCamera;
    private BackgroundMusicTrack.ScreenFadeLease musicFade;
    private void SetTransitionCamera(bool active)
    {
        if (active && transitionCamera == null)
        {
            var root = new GameObject("Transition Black Camera");
            root.transform.SetParent(transform, false);
            transitionCamera = root.AddComponent<Camera>();
            transitionCamera.depth = -10000;
            transitionCamera.clearFlags = CameraClearFlags.SolidColor;
            transitionCamera.backgroundColor = Color.black;
            transitionCamera.cullingMask = 0;
            transitionCamera.allowHDR = false;
            transitionCamera.allowMSAA = false;
        }
        if (transitionCamera != null) transitionCamera.enabled = active;
    }
    private void FreezeWorld() { worldPause ??= GameplayTimePause.Acquire(); if (fadeImage != null) fadeImage.raycastTarget = true; }
    private void RestoreWorld() { worldPause?.Dispose(); worldPause = null; if (fadeImage != null) fadeImage.raycastTarget = false; }
    private void LateUpdate() { if (running && worldPause != null) Time.timeScale = 0f; }
    public static bool IsRunning => instance != null && instance.running;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        if (fadeImage == null) fadeImage = GetComponentInChildren<Image>(true);
        if (fadeImage == null)
        {
            Debug.LogError("[Scene Fade] Assign a scene Image to the transition component.", this);
            return;
        }

        PrepareOverlay();
        SetAlpha(0f);
    }

    private void PrepareOverlay()
    {
        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.enabled = true;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.transform.localScale = Vector3.one;
            canvas.overrideSorting = true;
            canvas.sortingOrder = short.MaxValue;
            if (canvas.GetComponent<GraphicRaycaster>() == null) canvas.gameObject.AddComponent<GraphicRaycaster>();
        }
        if (fadeImage == null) return;
        fadeImage.gameObject.SetActive(true);
        fadeImage.enabled = true;
        fadeImage.sprite = null;
        fadeImage.material = null;
        fadeImage.type = Image.Type.Simple;
        fadeImage.maskable = false;
        fadeImage.canvasRenderer.SetAlpha(1f);
        var rect = fadeImage.rectTransform;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.SetAsLastSibling();
    }

    private IEnumerator HoldOpaqueFrame()
    {
        SetAlpha(1f);
        Canvas.ForceUpdateCanvases();
        // Keep a fully opaque rendered frame between fading and changing scene content.
        yield return null;
    }

    public static void LoadScene(string sceneName, float fadeDuration = .8f, Action<bool> onComplete = null, bool freezeGameplay = true, Action whenBlack = null, Func<bool> readyToReveal = null)
    {
        if (instance == null) CreateRuntimeInstance();

        if (instance.running)
        {
            onComplete?.Invoke(false);
            return;
        }

        instance.StartCoroutine(instance.LoadRoutine(sceneName, fadeDuration, onComplete, freezeGameplay, whenBlack, readyToReveal));
    }

    public static IEnumerator FadeThrough(Action whileBlack, float duration = .8f, bool fadeMusic = true)
        => FadeThrough(InvokeWhileBlack(whileBlack), duration, fadeMusic);

    private static IEnumerator InvokeWhileBlack(Action action)
    {
        action?.Invoke();
        yield break;
    }

    // Additive travel can finish loading and bind its camera while the screen stays opaque.
    public static IEnumerator FadeThrough(IEnumerator whileBlack, float duration = .8f, bool fadeMusic = true)
    {
        if (instance == null) CreateRuntimeInstance();
        while (instance.running) yield return null;
        var transition = instance;
        transition.running = true;
        transition.PrepareOverlay();
        transition.SetTransitionCamera(true);
        transition.FreezeWorld();
        if (fadeMusic) transition.musicFade = BackgroundMusicTrack.BeginScreenFade();
        try
        {
            yield return transition.Fade(0f, 1f, duration, progress => transition.musicFade?.SetProgress(progress));
            yield return transition.HoldOpaqueFrame();
            if (whileBlack != null) yield return whileBlack;
            yield return transition.HoldOpaqueFrame();
            yield return transition.Fade(1f, 0f, duration);
        }
        finally
        {
            if (transition != null)
            {
                transition.SetAlpha(0f);
                transition.running = false;
                transition.RestoreWorld();
                transition.SetTransitionCamera(false);
                transition.musicFade?.Dispose();
                transition.musicFade = null;
            }
        }
    }

    private IEnumerator LoadRoutine(string sceneName, float fadeDuration, Action<bool> onComplete, bool freezeGameplay, Action whenBlack, Func<bool> readyToReveal)
    {
        running = true;
        PrepareOverlay();
        SetTransitionCamera(true);
        if (freezeGameplay) FreezeWorld();
        else if (fadeImage != null) fadeImage.raycastTarget = true;
        musicFade = BackgroundMusicTrack.BeginScreenFade();
        bool succeeded = false;
        try
        {
            yield return Fade(0f, 1f, fadeDuration, progress => musicFade?.SetProgress(progress));
            yield return HoldOpaqueFrame();

            // Keep outgoing video/UI visible until a completely black frame has been presented.
            whenBlack?.Invoke();
            var dropTransition = RoomDropCache.Prepare(RoomDropCache.SceneRoom(sceneName));
            AsyncOperation load = null;
            try
            {
                load = SceneManager.LoadSceneAsync(sceneName);
                if (load != null) load.allowSceneActivation = false;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }

            if (load == null)
            {
                Debug.LogError($"[Scene Fade] Could not load scene '{sceneName}'. Add it to Build Settings.");
                yield return Fade(1f, 0f, fadeDuration);
                yield break;
            }

            while (load.progress < .9f) yield return null;
            yield return HoldOpaqueFrame();
            load.allowSceneActivation = true;
            while (!load.isDone) yield return null;
            // Let scene startup finish before replacing authored room drops.
            yield return null; yield return null;
            if (sceneName == "Forest" || sceneName == "Market") RoomDropCache.Commit(dropTransition, SceneManager.GetActiveScene());
            PrepareOverlay();
            yield return HoldOpaqueFrame();
            if (readyToReveal != null)
            {
                float deadline = Time.unscaledTime + 10f;
                while (!readyToReveal() && Time.unscaledTime < deadline) yield return null;
            }
            yield return Fade(1f, 0f, fadeDuration);
            succeeded = true;
        }
        finally
        {
            SetAlpha(0f);
            running = false;
            RestoreWorld();
            SetTransitionCamera(false);
            musicFade?.Dispose();
            musicFade = null;
            onComplete?.Invoke(succeeded);
        }
    }

    private static void CreateRuntimeInstance()
    {
        GameObject root = new GameObject("Scene Fade Transition");
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = short.MaxValue;

        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        GameObject imageObject = new GameObject("Black Fade", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform imageTransform = imageObject.GetComponent<RectTransform>();
        imageTransform.SetParent(root.transform, false);
        imageTransform.anchorMin = Vector2.zero;
        imageTransform.anchorMax = Vector2.one;
        imageTransform.offsetMin = Vector2.zero;
        imageTransform.offsetMax = Vector2.zero;

        Image image = imageObject.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0f);
        image.raycastTarget = false;

        root.AddComponent<SceneFadeTransition>();
    }

    private IEnumerator Fade(float from, float to, float duration, Action<float> onProgress = null)
    {
        float elapsed = 0f;
        float safeDuration = Mathf.Max(.01f, duration);
        onProgress?.Invoke(0f);
        while (elapsed < safeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / safeDuration);
            SetAlpha(Mathf.Lerp(from, to, progress));
            onProgress?.Invoke(progress);
            yield return null;
        }
        SetAlpha(to);
        onProgress?.Invoke(1f);
    }

    private void SetAlpha(float alpha)
    {
        if (fadeImage == null) return;
        fadeImage.color = new Color(0f, 0f, 0f, Mathf.Clamp01(alpha));
    }

    private void OnDestroy()
    {
        musicFade?.Dispose();
        musicFade = null;
        if (running) RestoreWorld();
        if (instance == this) instance = null;
    }
}
