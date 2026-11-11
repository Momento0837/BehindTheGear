using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class ControlTutorialUI : MonoBehaviour
{
    private Image image;
    private RectTransform rect;
    private ControlHintAnimation clip;
    private PlayerMovement2D player;
    private GameObject portal;
    private bool portalUsed;
    private float started;
    private readonly Key[] movementKeys = { Key.W, Key.A, Key.S, Key.D, Key.Space };

    public void Begin(PlayerMovement2D target)
    {
        player = target;
        var root = new GameObject("Control Tutorial", typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        root.GetComponent<Canvas>().sortingOrder = 32000;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        var visual = new GameObject("Animated Keys", typeof(RectTransform), typeof(Image));
        visual.transform.SetParent(root.transform, false);
        image = visual.GetComponent<Image>(); image.raycastTarget = false;
        var shadowShader = Resources.Load<Shader>("Effects/TutorialHintShadow");
        if (shadowShader != null)
            visual.AddComponent<MenuImageSoftEffect>().Configure(shadowShader, 0f, 30f, .5f);
        rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
        rect.pivot = new Vector2(.5f, 1);
        image.enabled = false;
        StartCoroutine(Run());
    }
    public void ArmPortal(GameObject target) => portal = target;
    public void CompletePortal() => portalUsed = true;
    private void Update()
    {
        if (image != null && image.enabled && clip != null) image.sprite = clip.Frame(Time.unscaledTime - started);
    }
    private bool CanReadInput => player != null && player.enabled && !SceneFadeTransition.IsRunning
        && !(player.GetComponent<PlayerControlLock>()?.IsLocked ?? false);
    private void Show(string name)
    {
        clip = ControlHintAnimation.Load(name);
        float width = name == "WASDSpace" ? 132f : name == "MouseL" ? 240f : 29.7f;
        rect.sizeDelta = clip.Size * (width / clip.Size.x);
        started = Time.unscaledTime;
        image.sprite = clip.Frame(0); image.enabled = true;
        rect.anchoredPosition = new Vector2(0, rect.sizeDelta.y + 10);
    }
    private IEnumerator Slide(bool entering)
    {
        float from = rect.anchoredPosition.y, to = entering ? -24 : rect.sizeDelta.y + 10;
        float duration = entering ? .9f : .45f;
        for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
        {
            rect.anchoredPosition = new Vector2(0, Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, t / duration)));
            yield return null;
        }
        rect.anchoredPosition = new Vector2(0, to);
        if (!entering) image.enabled = false;
    }
    private IEnumerator Run()
    {
        Show("WASDSpace");
        // Start counting as soon as the hint begins entering the screen.
        var entry = StartCoroutine(Slide(true));
        int mask = 0, count = 0;
        while (count < 2)
        {
            if (CanReadInput && Keyboard.current != null)
                for (int i = 0; i < movementKeys.Length; i++)
                    if ((mask & (1 << i)) == 0 && Keyboard.current[movementKeys[i]].wasPressedThisFrame)
                    { mask |= 1 << i; count++; }
            yield return null;
        }
        yield return entry;
        yield return Slide(false);
        Show("MouseL");
        yield return Slide(true);
        // Render the fully entered hint before accepting input. A click held during
        // the entrance must be released, then pressed again to dismiss this hint.
        yield return null;
        while (Mouse.current != null && Mouse.current.leftButton.isPressed) yield return null;
        while (!CanReadInput || Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) yield return null;
        yield return Slide(false);
        while (!portalUsed && (portal == null || !portal.activeInHierarchy || player == null
            || Mathf.Abs(player.transform.position.x - portal.GetComponent<Collider2D>().bounds.ClosestPoint(player.transform.position).x) > 2.5f)) yield return null;
        if (portalUsed) yield break;
        Show("F"); entry = StartCoroutine(Slide(true));
        while (!portalUsed) yield return null;
        yield return entry;
        yield return Slide(false);
    }
}
