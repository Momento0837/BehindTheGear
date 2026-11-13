using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(10001)]
public sealed class InventoryCloakPreview : MonoBehaviour
{
    private PlayerCloakVisual player;
    private PlayerCloakLibrary library;
    private Image body, cloak;
    public void Bind(PlayerCloakVisual owner, PlayerCloakLibrary frames)
    {
        player = owner; library = frames;
        if (cloak != null) return;
        body = GetComponent<Image>();
        cloak = new GameObject("Equipped Cloak Preview", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
        cloak.transform.SetParent(transform, false);
        cloak.raycastTarget = false; cloak.preserveAspect = body.preserveAspect;
        cloak.rectTransform.anchorMin = Vector2.zero; cloak.rectTransform.anchorMax = Vector2.one;
        cloak.rectTransform.offsetMin = cloak.rectTransform.offsetMax = Vector2.zero;
        cloak.enabled = false;
    }
    private void LateUpdate()
    {
        if (cloak == null) return;
        cloak.enabled = player != null && player.Equipped && body.enabled;
        if (!cloak.enabled) return;
        cloak.sprite = library.Frame(body.sprite); cloak.color = body.color;
        if (body.sprite == null) return;
        float height = body.rectTransform.rect.height;
        if (body.preserveAspect) height = Mathf.Min(height, body.rectTransform.rect.width * body.sprite.rect.height / body.sprite.rect.width);
        cloak.rectTransform.anchoredPosition = new Vector2(0, player.OffsetY * height / body.sprite.bounds.size.y);
    }
}
