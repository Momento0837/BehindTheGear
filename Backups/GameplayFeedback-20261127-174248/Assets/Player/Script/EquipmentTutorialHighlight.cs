using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public sealed class EquipmentTutorialHighlight : MonoBehaviour
{
    InventoryUI owner;
    PlayerInventory inventory;
    InventoryItemDefinition item;
    GameObject dim;
    Image border, mouse;
    ControlHintAnimation mouseClip;
    float animationStarted;
    readonly List<GameObject> layers = new();
    readonly List<Component> added = new();
    Transform target;
    public void Begin(InventoryUI ui, InventoryItemDefinition gear)
    { Cleanup(); owner = ui; inventory = ui.GetComponent<PlayerInventory>(); item = gear; enabled = true; }
    void LateUpdate()
    {
        if (item == null || inventory == null || inventory.GetEquipped(item.Slot) == item) { Cleanup(); enabled = false; return; }
        if (!owner.IsOpen || owner.IsOpening) { Cleanup(); return; }
        var source = owner.FindTutorialItemImage(item);
        if (source == null) { Cleanup(); return; }
        if (target != source.transform)
        {
            Cleanup(); target = source.transform;
            int order = owner.SceneCanvas.sortingOrder + 2;
            dim = new GameObject("Equipment Tutorial Dim", typeof(RectTransform), typeof(Canvas), typeof(Image));
            dim.transform.SetParent(owner.SceneCanvas.transform, false);
            var rect = (RectTransform)dim.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var c = dim.GetComponent<Canvas>(); c.overrideSorting = true; c.sortingOrder = order;
            var image = dim.GetComponent<Image>(); image.color = new Color(0, 0, 0, .45f); image.raycastTarget = false;
            Raise(source.GetComponentInParent<InventorySlotUI>().gameObject, order + 1);
            var socket = owner.FindTutorialSocket(item); if (socket != null) Raise(socket.gameObject, order + 1);
            border = Image("Cloak White Border", target);
            border.sprite = Resources.Load<InventoryOpeningHintStyle>("UI/InventoryOpeningHintStyle")?.border;
            border.type = UnityEngine.UI.Image.Type.Sliced;
            mouseClip = ControlHintAnimation.Load("Inventory_Tutorial");
            mouse = Image("Inventory Equipment Tutorial", target); mouse.preserveAspect = true;
            mouse.maskable = false;
            mouse.rectTransform.anchorMin = mouse.rectTransform.anchorMax = new Vector2(.5f, 0);
            mouse.rectTransform.pivot = mouseClip.Pivot;
            float height = source.rectTransform.rect.height * .65f * .7f;
            mouse.rectTransform.sizeDelta = new Vector2(height * mouseClip.Size.x / mouseClip.Size.y, height);
            mouse.rectTransform.anchoredPosition = Vector2.zero;
            animationStarted = Time.unscaledTime;
        }
        if (border != null)
        {
            float pulse = .5f + .5f * Mathf.Sin(Time.unscaledTime * 4f);
            border.color = new Color(1, 1, 1, .4f + .6f * pulse);
            border.transform.localScale = Vector3.one * Mathf.Lerp(1, 1.08f, pulse);
            mouse.sprite = mouseClip.Frame(Time.unscaledTime - animationStarted);
        }
    }
    void Raise(GameObject go, int order)
    {
        // Slot objects have no authored nested canvas; leave any custom canvas untouched.
        if (go.GetComponent<Canvas>() != null) return;
        var canvas = go.AddComponent<Canvas>(); canvas.overrideSorting = true; canvas.sortingOrder = order; added.Add(canvas);
        added.Add(go.AddComponent<GraphicRaycaster>());
    }
    Image Image(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image)); layers.Add(go); go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>(); image.raycastTarget = false;
        var rect = image.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        return image;
    }
    public void Cleanup()
    {
        if (dim != null) { dim.SetActive(false); Destroy(dim); } dim = null;
        foreach (var layer in layers) if (layer != null) { layer.SetActive(false); Destroy(layer); } layers.Clear();
        for (int i = added.Count - 1; i >= 0; i--) if (added[i] != null) Destroy(added[i]); added.Clear(); target = null;
    }
    void OnDisable() => Cleanup();
}
