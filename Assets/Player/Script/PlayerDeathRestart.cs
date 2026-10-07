using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[RequireComponent(typeof(PlayerHealth))]
public sealed class PlayerDeathRestart : MonoBehaviour
{
    [SerializeField, Min(.01f)] private float fadeDuration = 1.5f;

    private PlayerHealth playerHealth;

    private void Awake() => playerHealth = GetComponent<PlayerHealth>();

    private void OnEnable() => playerHealth.Died += RestartScene;

    private void OnDisable()
    {
        if (playerHealth != null) playerHealth.Died -= RestartScene;
    }

    private void RestartScene() => DeathFadeTransition.Restart(fadeDuration);
}

internal sealed class DeathFadeTransition : MonoBehaviour
{
    private static DeathFadeTransition instance;
    private Image fadeImage;
    private bool running;

    public static void Restart(float fadeDuration)
    {
        if (instance == null) Create();
        if (!instance.running) instance.StartCoroutine(instance.RestartRoutine(fadeDuration));
    }

    private static void Create()
    {
        GameObject root = new GameObject("Death Fade Transition", typeof(Canvas), typeof(CanvasScaler), typeof(DeathFadeTransition));
        instance = root.GetComponent<DeathFadeTransition>();
        Object.DontDestroyOnLoad(root);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);

        GameObject panel = new GameObject("Black Fade", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(root.transform, false);
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        instance.fadeImage = panel.GetComponent<Image>();
        instance.fadeImage.color = new Color(0f, 0f, 0f, 0f);
        instance.fadeImage.raycastTarget = false;
    }

    private IEnumerator RestartRoutine(float fadeDuration)
    {
        running = true;
        yield return Fade(0f, 1f, fadeDuration);
        yield return SceneManager.LoadSceneAsync(SceneManager.GetActiveScene().buildIndex);
        yield return Fade(1f, 0f, fadeDuration);
        running = false;
    }

    private IEnumerator Fade(float from, float to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetAlpha(Mathf.Lerp(from, to, elapsed / duration));
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
}
