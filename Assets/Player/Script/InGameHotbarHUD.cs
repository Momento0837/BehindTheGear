using UnityEngine;
using UnityEngine.UI;

/// <summary>Uses the authored Slot01..03 images and cooldown children without changing layout.</summary>
[DisallowMultipleComponent]
public sealed class InGameHotbarHUD : MonoBehaviour
{
    [SerializeField] private Image[] slotImages = new Image[3];
    [SerializeField] private Image[] cooldownImages = new Image[3];
    [SerializeField] private Shader itemFlashShader;
    [SerializeField] private Material itemFlashMaterial;
    private readonly Material[] flashMaterials = new Material[3];
    private readonly Material[] originalMaterials = new Material[3];
    private readonly Color[] originalColors = new Color[3];
    private readonly float[] flashStartedAt = { float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity };
    private static readonly int FlashAmount = Shader.PropertyToID("_FlashAmount");
    private PlayerInventory inventory;
    private float nextSearch;
    private bool initialized;

    private void Awake() => InitializeImages();

    private void InitializeImages()
    {
        if (initialized) return;
        initialized = true;
        if (slotImages == null || slotImages.Length != 3) System.Array.Resize(ref slotImages, 3);
        if (cooldownImages == null || cooldownImages.Length != 3) System.Array.Resize(ref cooldownImages, 3);
        if (itemFlashShader == null) itemFlashShader = Shader.Find("BehindTheGear/UI/HotbarItemFlash");
        for (int i = 0; i < 3; i++)
        {
            Transform slot = transform.Find($"HotBar_Frame/Slot{i + 1:00}");
            if (slotImages[i] == null && slot != null) slotImages[i] = slot.GetComponent<Image>();
            if (cooldownImages[i] == null && slot != null)
                cooldownImages[i] = slot.Find($"Slot{i + 1:00}_Cool")?.GetComponent<Image>();
            if (slotImages[i] != null)
            {
                originalMaterials[i] = slotImages[i].material;
                originalColors[i] = slotImages[i].color;
                if (itemFlashMaterial != null || itemFlashShader != null)
                {
                    flashMaterials[i] = itemFlashMaterial != null ? new Material(itemFlashMaterial) : new Material(itemFlashShader);
                    flashMaterials[i].name = $"Hotbar Item Flash {i + 1}";
                    slotImages[i].material = flashMaterials[i];
                }
                slotImages[i].raycastTarget = false;
            }
            if (cooldownImages[i] == null) continue;
            cooldownImages[i].type = Image.Type.Filled;
            cooldownImages[i].fillMethod = Image.FillMethod.Vertical;
            cooldownImages[i].fillOrigin = (int)Image.OriginVertical.Bottom;
            cooldownImages[i].fillAmount = 0f;
            cooldownImages[i].raycastTarget = false;
        }
    }

    private void LateUpdate()
    {
        if (inventory == null && Time.unscaledTime >= nextSearch)
        {
            nextSearch = Time.unscaledTime + .25f;
            Transform target = PlayerTargetRegistry.Current;
            var candidate = target != null ? target.GetComponent<PlayerInventory>() : null;
            if (candidate == null) candidate = FindFirstObjectByType<PlayerInventory>();
            if (candidate != null) Bind(candidate);
        }
        Refresh();
    }

    public void Bind(PlayerInventory source)
    {
        InitializeImages();
        if (inventory != null)
        {
            inventory.QuickSlotAssigned -= Flash;
            inventory.QuickSlotUsed -= Flash;
        }
        inventory = source;
        if (inventory != null)
        {
            inventory.QuickSlotAssigned += Flash;
            inventory.QuickSlotUsed += Flash;
        }
        Refresh();
    }

    public void Refresh()
    {
        InitializeImages();
        for (int i = 0; i < 3; i++)
        {
            var item = inventory != null ? inventory.GetQuickSlot(i) : null;
            if (slotImages[i] != null)
            {
                slotImages[i].sprite = item != null ? item.Icon : null;
                slotImages[i].enabled = true;
                Color color = originalColors[i];
                // The authored empty slots have zero alpha; populated items must be visible.
                color.a = item != null ? 1f : 0f;
                slotImages[i].color = color;
            }
            if (cooldownImages[i] != null)
                cooldownImages[i].fillAmount = inventory != null ? inventory.GetQuickSlotCooldownFill(i) : 0f;
            if (flashMaterials[i] != null)
            {
                float progress = Mathf.Clamp01((Time.unscaledTime - flashStartedAt[i]) / .3f);
                float strength = item != null ? .5f * (1f - Mathf.SmoothStep(0f, 1f, progress)) : 0f;
                flashMaterials[i].SetFloat(FlashAmount, strength);
                Material rendered = slotImages[i] != null ? slotImages[i].materialForRendering : null;
                if (rendered != null && rendered != flashMaterials[i] && rendered.HasProperty(FlashAmount))
                    rendered.SetFloat(FlashAmount, strength);
            }
        }
    }

    private void Flash(int index)
    {
        if (index < 0 || index >= 3) return;
        flashStartedAt[index] = Time.unscaledTime;
        Refresh();
    }

    private void OnDisable()
    {
        if (inventory != null)
        {
            inventory.QuickSlotAssigned -= Flash;
            inventory.QuickSlotUsed -= Flash;
        }
        inventory = null;
    }

    private void OnDestroy()
    {
        for (int i = 0; i < 3; i++)
        {
            if (slotImages[i] != null) slotImages[i].material = originalMaterials[i];
            if (flashMaterials[i] == null) continue;
            if (Application.isPlaying) Destroy(flashMaterials[i]);
            else DestroyImmediate(flashMaterials[i]);
        }
    }
}
