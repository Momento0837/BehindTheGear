using UnityEngine;
using UnityEngine.UI;

/// <summary>Draws an inventory shortcut above a nonblocking tutorial dimmer.</summary>
public sealed class InventoryOpeningHint : MonoBehaviour
{
    private InventoryUI inventory;
    private Image source, icon, border, dim;
    private Canvas canvas;
    private GameObject overlay;
    private float elapsed, fadeElapsed, fadeFrom;
    private bool dismissing;
    private readonly Vector3[] corners = new Vector3[4];

    public void Begin(InventoryUI owner)
    {
        Cleanup();
        inventory = owner;
        if (owner.IsOpen) return;
        foreach (var image in FindObjectsByType<Image>(FindObjectsSortMode.None))
            if (image.name == "Inventory_Icon") { source = image; break; }
        if (source == null && owner.ShortcutButton != null) source = owner.ShortcutButton.GetComponent<Image>();
        var style = Resources.Load<InventoryOpeningHintStyle>("UI/InventoryOpeningHintStyle");
        if (source == null || style == null || style.border == null)
        {
            Debug.LogWarning("[Inventory Hint] Missing shortcut image or White_Border reference.", this);
            return;
        }
        overlay = new GameObject("Inventory Opening Tutorial", typeof(Canvas), typeof(GraphicRaycaster));
        overlay.transform.SetParent(transform, false);
        canvas = overlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32010;
        dim = MakeImage("Tutorial Dim");
        var rect = dim.rectTransform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        dim.color = Color.clear;
        icon = MakeImage("Highlighted Inventory Icon");
        icon.sprite = source.sprite;
        icon.type = source.type;
        icon.preserveAspect = source.preserveAspect;
        icon.raycastTarget = true;
        icon.gameObject.AddComponent<Button>().onClick.AddListener(owner.OpenInventory);
        border = MakeImage("White Border Pulse");
        border.sprite = style.border;
        border.type = Image.Type.Sliced;
        border.enabled = false;
        elapsed = fadeElapsed = 0;
        dismissing = false;
        AlignIcon();
    }

    private Image MakeImage(string name)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(Image));
        root.transform.SetParent(overlay.transform, false);
        var image = root.GetComponent<Image>();
        image.raycastTarget = false;
        image.rectTransform.pivot = new Vector2(.5f, .5f);
        return image;
    }

    public void Dismiss()
    {
        if (overlay == null || dismissing) return;
        dismissing = true;
        fadeElapsed = 0;
        fadeFrom = dim.color.a;
        border.enabled = false;
        icon.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (overlay == null) return;
        if (inventory == null || !inventory.isActiveAndEnabled || inventory.IsOpen || source == null || !source.gameObject.activeInHierarchy) Dismiss();
        if (dismissing)
        {
            fadeElapsed += Time.unscaledDeltaTime;
            dim.color = new Color(0, 0, 0, Mathf.Lerp(fadeFrom, 0, Mathf.SmoothStep(0, 1, fadeElapsed / .5f)));
            if (fadeElapsed >= .5f) Cleanup();
            return;
        }
        elapsed += Time.unscaledDeltaTime;
        dim.color = new Color(0, 0, 0, .3f * Mathf.SmoothStep(0, 1, elapsed / .5f));
        AlignIcon();
        TutorialHighlightPulse.Apply(border, elapsed);
    }

    private void AlignIcon()
    {
        var sourceCanvas = source.canvas;
        Camera camera = sourceCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : sourceCanvas.worldCamera;
        source.rectTransform.GetWorldCorners(corners);
        Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
        Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
        var root = (RectTransform)canvas.transform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, (min + max) * .5f, null, out var position);
        icon.rectTransform.anchoredPosition = border.rectTransform.anchoredPosition = position;
        icon.rectTransform.sizeDelta = border.rectTransform.sizeDelta = (max - min) / canvas.scaleFactor;
        icon.color = source.color;
    }

    private void Cleanup()
    {
        if (overlay != null) { overlay.SetActive(false); Destroy(overlay); }
        overlay = null;
        source = null;
    }
    private void OnDisable() => Cleanup();
    private void OnDestroy() => Cleanup();
}
