using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Fades to black before a scene load and reveals the new scene afterward.</summary>
public sealed class SceneFadeTransition : MonoBehaviour
{
    private static SceneFadeTransition instance;
    private Image fadeImage;
    private bool running;

    public static void LoadScene(string sceneName, float fadeDuration = .8f)
    {
        if (instance == null) Create();
        if (!instance.running) instance.StartCoroutine(instance.LoadRoutine(sceneName, fadeDuration));
    }

    private static void Create()
    {
        GameObject root = new("Scene Fade Transition", typeof(Canvas), typeof(CanvasScaler), typeof(SceneFadeTransition));
        instance = root.GetComponent<SceneFadeTransition>();
        DontDestroyOnLoad(root);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 32000;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);

        GameObject panel = new("Black Fade", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(root.transform, false);
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        instance.fadeImage = panel.GetComponent<Image>();
        instance.fadeImage.color = Color.clear;
        instance.fadeImage.raycastTarget = false;
    }

    private IEnumerator LoadRoutine(string sceneName, float fadeDuration)
    {
        running = true;
        yield return Fade(0f, 1f, fadeDuration);

        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);
        if (load == null)
        {
            Debug.LogError($"[Scene Fade] Could not load scene '{sceneName}'. Add it to Build Settings.");
            yield return Fade(1f, 0f, fadeDuration);
            running = false;
            yield break;
        }

        while (!load.isDone) yield return null;
        yield return Fade(1f, 0f, fadeDuration);
        running = false;
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
        Color color = fadeImage.color;
        color.a = alpha;
        fadeImage.color = color;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
