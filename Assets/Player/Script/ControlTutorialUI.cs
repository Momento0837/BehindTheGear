using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class ControlTutorialUI : MonoBehaviour
{
    private Image image;
    private Image backdrop;
    private RectTransform rect;
    private ControlHintAnimation clip;
    private PlayerMovement2D player;
    private GameObject portal;
    private bool portalUsed;
    private float started;
    public bool IsPortalHintVisible { get; private set; }
    public GameObject PresentationRoot { get; private set; }
    private readonly Key[] movementKeys = { Key.W, Key.A, Key.S, Key.D, Key.Space };

    public bool HandlesPortal(GameObject target) => portal == target && !portalUsed;
    public void Begin(PlayerMovement2D target, bool portalOnly = false)
    {
        player = target;
        var root = new GameObject("Control Tutorial", typeof(Canvas), typeof(CanvasScaler));
        PresentationRoot = root;
        root.transform.SetParent(transform, false);
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        root.GetComponent<Canvas>().sortingOrder = 32000;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        var background = new GameObject("Tutorial Background", typeof(RectTransform), typeof(Image));
        background.transform.SetParent(root.transform, false);
        backdrop = background.GetComponent<Image>();
        backdrop.color = new Color(0, 0, 0, .2f);
        backdrop.raycastTarget = false;
        backdrop.enabled = false;
        var visual = new GameObject("Animated Keys", typeof(RectTransform), typeof(Image));
        visual.transform.SetParent(root.transform, false);
        image = visual.GetComponent<Image>(); image.raycastTarget = false;
        var shadowShader = Resources.Load<Shader>("Effects/TutorialHintShadow");
        if (shadowShader != null)
            visual.AddComponent<MenuImageSoftEffect>().Configure(shadowShader, 0f, 30f, .5f);
        rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, .5f);
        rect.pivot = new Vector2(0f, .5f);
        image.enabled = false;
        StartCoroutine(portalOnly ? RunPortal() : Run());
    }
    public void ArmPortal(GameObject target) => portal = target;
    public void CompletePortal() => portalUsed = true;
    private void Update()
    {
        if (image != null && image.enabled && clip != null) image.sprite = clip.Frame(Time.unscaledTime - started);
    }
    private bool CanReadInput => player != null && player.enabled && !SceneFadeTransition.IsRunning
        && !(player.GetComponent<PlayerControlLock>()?.IsLocked ?? false);
    private void LateUpdate()
    {
        if (backdrop == null || image == null) return;
        backdrop.enabled = image.enabled;
        var background = backdrop.rectTransform;
        background.anchorMin = rect.anchorMin; background.anchorMax = rect.anchorMax;
        background.pivot = rect.pivot;
        background.sizeDelta = rect.sizeDelta;
        background.anchoredPosition = rect.anchoredPosition;
    }
    private void Show(string name)
    {
        clip = ControlHintAnimation.Load(name);
        float width = name == "WASDSpace" ? 158.4f : name == "MouseL" ? 240f : 59.4f;
        rect.sizeDelta = clip.Size * (width / clip.Size.x);
        started = Time.unscaledTime;
        image.sprite = clip.Frame(0); image.enabled = true;
        rect.anchoredPosition = new Vector2(-rect.sizeDelta.x - 40f, 0f);
    }
    private IEnumerator Slide(bool entering)
    {
        float from = rect.anchoredPosition.x, to = entering ? 24f : -rect.sizeDelta.x - 40f;
        float duration = entering ? .9f : .45f;
        for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
        {
            rect.anchoredPosition = new Vector2(Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, t / duration)), 0f);
            yield return null;
        }
        rect.anchoredPosition = new Vector2(to, 0f);
        if (entering)
        {
            // Restart once fully on screen, so no frames are missed during the entrance.
            started = Time.unscaledTime;
            image.sprite = clip.Frame(0);
        }
        else image.enabled = false;
    }
    private IEnumerator FinishVisibleCycle()
    {
        while (Time.unscaledTime - started < clip.Duration) yield return null;
    }
    private IEnumerator Run()
    {
        float movementStartX = player != null ? player.transform.position.x : 0f;
        Show("WASDSpace");
        // Start counting as soon as the hint begins entering the screen.
        var entry = StartCoroutine(Slide(true));
        int mask = 0, count = 0;
        while (count < 3)
        {
            if (CanReadInput && Mathf.Abs(player.transform.position.x - movementStartX) >= 5f)
                break;
            if (CanReadInput && Keyboard.current != null)
                for (int i = 0; i < movementKeys.Length; i++)
                    if ((mask & (1 << i)) == 0 && Keyboard.current[movementKeys[i]].wasPressedThisFrame)
                    { mask |= 1 << i; count++; }
            yield return null;
        }
        yield return entry;
        // Movement keys may dismiss the hint without waiting for a full GIF cycle.
        yield return Slide(false);
        Show("MouseL");
        yield return Slide(true);
        // Render the fully entered hint before accepting input. A click held during
        // the entrance must be released, then pressed again to dismiss this hint.
        yield return null;
        while (Mouse.current != null && Mouse.current.leftButton.isPressed) yield return null;
        while (!CanReadInput || Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) yield return null;
        yield return FinishVisibleCycle();
        yield return Slide(false);
        yield return RunPortal();
    }
    private IEnumerator RunPortal()
    {
        while (!portalUsed && (portal == null || !portal.activeInHierarchy || player == null
            || Mathf.Abs(player.transform.position.x - portal.GetComponent<Collider2D>().bounds.ClosestPoint(player.transform.position).x) > 2.5f)) yield return null;
        if (portalUsed) yield break;
        IsPortalHintVisible = true;
        Show("F"); var entry = StartCoroutine(Slide(true));
        while (!portalUsed) yield return null;
        yield return entry;
        yield return FinishVisibleCycle();
        yield return Slide(false);
        IsPortalHintVisible = false;
    }
}
