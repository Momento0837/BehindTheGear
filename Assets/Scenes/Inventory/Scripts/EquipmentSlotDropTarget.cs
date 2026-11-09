using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Drop receiver and reversible, centre-based equipment-slot emphasis.</summary>
public sealed class EquipmentSlotDropTarget : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
    private InventoryUI owner;
    private int index;
    private RectTransform rect;
    private Vector3 scale;
    private Vector3 position;
    private Image frame;
    private Material originalMaterial;
    private Material highlightMaterial;
    private float strength;
    private bool emphasized;
    public void Bind(InventoryUI ui, int slotIndex)
    {
        owner = ui;
        index = slotIndex;
        rect = (RectTransform)transform;
        scale = rect.localScale;
        position = rect.localPosition;
        frame = GetComponent<Image>();
        if (frame != null)
        {
            frame.raycastTarget = true;
            originalMaterial = frame.material;
            var shader = Shader.Find("BehindTheGear/UI/HotbarItemFlash");
            if (shader != null) highlightMaterial = new Material(shader);
        }
    }
    public void SetEmphasized(bool value) => emphasized = value;
    public void OnPointerEnter(PointerEventData data) => owner?.SetEquipmentHovered(index, true);
    public void OnPointerExit(PointerEventData data) => owner?.SetEquipmentHovered(index, false);
    public void OnDrop(PointerEventData data)
    {
        if (data.button == PointerEventData.InputButton.Left && owner != null
            && data.pointerDrag != null && data.pointerDrag.GetComponent<InventorySlotUI>() != null)
            owner.DropEquipment(index);
    }
    private void LateUpdate()
    {
        if (rect == null) return;
        strength = Mathf.MoveTowards(strength, emphasized ? 1f : 0f, Time.unscaledDeltaTime / .12f);
        float factor = 1f + .05f * strength;
        rect.localScale = scale * factor;
        // Compensate non-centred authored pivots without modifying the prefab layout.
        Vector3 centre = Vector3.Scale((Vector3)rect.rect.center, scale);
        rect.localPosition = position + rect.localRotation * (centre * (1f - factor));
        if (frame == null || highlightMaterial == null) return;
        frame.material = strength > 0f ? highlightMaterial : originalMaterial;
        highlightMaterial.SetFloat("_FlashAmount", .16f * strength);
        var rendered = frame.materialForRendering;
        if (strength > 0f && rendered != null && rendered.HasProperty("_FlashAmount"))
            rendered.SetFloat("_FlashAmount", .16f * strength);
    }
    private void OnDisable()
    {
        strength = 0f;
        emphasized = false;
        if (rect != null) { rect.localScale = scale; rect.localPosition = position; }
        if (frame != null) frame.material = originalMaterial;
    }
    private void OnDestroy() { if (highlightMaterial != null) Destroy(highlightMaterial); }
}
