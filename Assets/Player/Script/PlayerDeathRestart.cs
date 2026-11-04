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

    private void RestartScene() => DeathFadeTransition.Restart(fadeDuration, playerHealth);
}

internal sealed class DeathFadeTransition : MonoBehaviour
{
    private static DeathFadeTransition instance;
    private Image fadeImage;
    private bool running;

    public static void RevealCheckpoint()
    {
        if (instance == null) return;
        instance.SetAlpha(0f);
        instance.running = false;
    }

    public static void Restart(float fadeDuration, PlayerHealth player)
    {
        if (instance == null) Create();
        if (!instance.running) instance.StartCoroutine(instance.RestartRoutine(fadeDuration, player));
    }

    private static void Create()
    {
        GameObject root = new GameObject("Death Fade Transition", typeof(Canvas), typeof(CanvasScaler), typeof(DeathFadeTransition));
        instance = root.GetComponent<DeathFadeTransition>();
        Object.DontDestroyOnLoad(root);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
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

    private IEnumerator RestartRoutine(float fadeDuration, PlayerHealth player)
    {
        running = true;
        Camera.main?.GetComponent<PlayerCameraFollow2D>()?.BeginDeathFocus(player.transform);
        var animator = player.GetComponent<Animator>();
        int dead = Animator.StringToHash("Base Layer.Dead");
        if (animator != null && animator.HasState(0, dead))
        {
            animator.Play(dead, 0, 0f);
            yield return null;
            while (animator != null && animator.GetCurrentAnimatorStateInfo(0).fullPathHash == dead
                && animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f) yield return null;
        }
        yield return new WaitForSecondsRealtime(.5f);
        yield return Fade(0f, 1f, fadeDuration);
        RoomDropCache.Clear();
        StorySceneFlow.RestartAtCombat = true;
        yield return SceneManager.LoadSceneAsync("Forest");
        // The opening quest presentation takes over the black screen before revealing combat.
        while (running) yield return null;
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
