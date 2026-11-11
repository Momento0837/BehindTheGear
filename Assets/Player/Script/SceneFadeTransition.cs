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
    private void FreezeWorld() { worldPause ??= GameplayTimePause.Acquire(); if (fadeImage != null) fadeImage.raycastTarget = true; }
    private void RestoreWorld() { worldPause?.Dispose(); worldPause = null; if (fadeImage != null) fadeImage.raycastTarget = false; }
    private void LateUpdate() { if (running) Time.timeScale = 0f; }
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

        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder = short.MaxValue;
            if (canvas.GetComponent<GraphicRaycaster>() == null) canvas.gameObject.AddComponent<GraphicRaycaster>();
        }

        SetAlpha(0f);
    }

    public static void LoadScene(string sceneName, float fadeDuration = .8f, Action<bool> onComplete = null)
    {
        if (instance == null) CreateRuntimeInstance();

        if (instance.running)
        {
            onComplete?.Invoke(false);
            return;
        }

        instance.StartCoroutine(instance.LoadRoutine(sceneName, fadeDuration, onComplete));
    }

    public static IEnumerator FadeThrough(Action whileBlack, float duration = .8f)
        => FadeThrough(InvokeWhileBlack(whileBlack), duration);

    private static IEnumerator InvokeWhileBlack(Action action)
    {
        action?.Invoke();
        yield break;
    }

    // Additive travel can finish loading and bind its camera while the screen stays opaque.
    public static IEnumerator FadeThrough(IEnumerator whileBlack, float duration = .8f)
    {
        if (instance == null) CreateRuntimeInstance();
        while (instance.running) yield return null;
        var transition = instance;
        transition.running = true;
        transition.FreezeWorld();
        try
        {
            yield return transition.Fade(0f, 1f, duration);
            if (whileBlack != null) yield return whileBlack;
            yield return null;
            yield return transition.Fade(1f, 0f, duration);
        }
        finally
        {
            if (transition != null)
            {
                transition.SetAlpha(0f);
                transition.running = false;
                transition.RestoreWorld();
            }
        }
    }

    private IEnumerator LoadRoutine(string sceneName, float fadeDuration, Action<bool> onComplete)
    {
        running = true;
        FreezeWorld();
        yield return Fade(0f, 1f, fadeDuration);

        var dropTransition = RoomDropCache.Prepare(RoomDropCache.SceneRoom(sceneName));
        AsyncOperation load = null;
        try
        {
            load = SceneManager.LoadSceneAsync(sceneName);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
        }

        if (load == null)
        {
            Debug.LogError($"[Scene Fade] Could not load scene '{sceneName}'. Add it to Build Settings.");
            yield return Fade(1f, 0f, fadeDuration);
            running = false;
            RestoreWorld();
            onComplete?.Invoke(false);
            yield break;
        }

        while (!load.isDone) yield return null;
        // Let scene startup finish before replacing authored room drops.
        yield return null; yield return null;
        if (sceneName == "Forest" || sceneName == "Market") RoomDropCache.Commit(dropTransition, SceneManager.GetActiveScene());
        yield return Fade(1f, 0f, fadeDuration);
        running = false;
        RestoreWorld();
        onComplete?.Invoke(true);
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

    private IEnumerator Fade(float from, float to, float duration)
    {
        float elapsed = 0f;
        float safeDuration = Mathf.Max(.01f, duration);
        while (elapsed < safeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetAlpha(Mathf.Lerp(from, to, elapsed / safeDuration));
            yield return null;
        }
        SetAlpha(to);
    }

    private void SetAlpha(float alpha)
    {
        if (fadeImage == null) return;
        Color color = fadeImage.color;
        color.a = alpha;
        fadeImage.color = color;
    }

    private void OnDestroy()
    {
        if (running) RestoreWorld();
        if (instance == this) instance = null;
    }
}
