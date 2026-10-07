using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Behind The Gear/Inventory Item", fileName = "New Item")]
public sealed class InventoryItemDefinition : ScriptableObject, IItemStatModifierProvider
{
    public enum FallbackIcon { Potion, Crystal, Gear }
    public enum ItemCategory { Misc, Consumable, Equipment, Quest }
    public enum EquipmentSlot { None, Weapon, Armor, Accessory }
    public enum ConsumableEffect { None, RestoreHealth, RestoreMana, RestoreHealthAndMana }

    [SerializeField] private string itemId = "item_id";
    [SerializeField] private string displayName = "Item";
    [SerializeField, TextArea] private string description;
    [SerializeField] private Sprite icon;
    [SerializeField] private Color tint = Color.white;
    [SerializeField] private FallbackIcon fallbackIcon;
    [SerializeField] private ItemCategory category = ItemCategory.Misc;
    [SerializeField, Range(1, 50)] private int maxStack = 50;
    [SerializeField] private EquipmentSlot equipmentSlot;
    [SerializeField] private ItemStatModifier[] statModifiers = new ItemStatModifier[0];
    [SerializeField] private ConsumableEffect consumableEffect;
    [SerializeField, Min(0)] private int consumablePower = 25;
    private Sprite generatedIcon;

    public string ItemId => itemId;
    public string DisplayName => displayName;
    public string Description => description;
    public ItemCategory Category => category;
    public EquipmentSlot Slot => equipmentSlot;
    public IReadOnlyList<ItemStatModifier> StatModifiers => statModifiers;
    public bool HasStatModifiers => statModifiers != null && statModifiers.Length > 0;

    public int GetStatIncrease(PersonalStatType statType)
    {
        if (statModifiers == null) return 0;

        int total = 0;
        for (int i = 0; i < statModifiers.Length; i++)
        {
            if (statModifiers[i].StatType == statType)
                total += statModifiers[i].Increase;
        }

        return total;
    }
    public ConsumableEffect Effect => consumableEffect;
    public int ConsumablePower => consumablePower;
    public bool IsQuestItem => category == ItemCategory.Quest;
    public bool IsEquipment => category == ItemCategory.Equipment;
    public bool IsConsumable => category == ItemCategory.Consumable;
    public int MaxStack => IsEquipment ? 1 : Mathf.Clamp(maxStack, 1, 50);
    public Sprite Icon => icon != null ? icon : generatedIcon != null ? generatedIcon : generatedIcon = CreateIcon();

    public bool Use(GameObject user)
    {
        if (!IsConsumable) return false;
        string targetName = user != null ? user.name : "Unknown";
        switch (consumableEffect)
        {
            case ConsumableEffect.RestoreHealth:
                Debug.Log($"[Inventory] {targetName} restored {consumablePower} health with {DisplayName}.", this);
                return true;
            case ConsumableEffect.RestoreMana:
                Debug.Log($"[Inventory] {targetName} restored {consumablePower} mana with {DisplayName}.", this);
                return true;
            case ConsumableEffect.RestoreHealthAndMana:
                Debug.Log($"[Inventory] {targetName} restored {consumablePower} health and mana with {DisplayName}.", this);
                return true;
            default:
                Debug.Log($"[Inventory] {targetName} used {DisplayName}. Add a concrete effect on the item asset when ready.", this);
                return true;
        }
    }

    public string GetCategoryLabel()
    {
        return category switch
        {
            ItemCategory.Consumable => "소비",
            ItemCategory.Equipment => "장비",
            ItemCategory.Quest => "퀘스트",
            _ => "잡화"
        };
    }

    // Small code-drawn placeholders; assign an imported Sprite to replace them.
    private Sprite CreateIcon()
    {
        string[] pixels = fallbackIcon switch
        {
            FallbackIcon.Crystal => new[] { ".....OO.....", "....OHCO....", "...OHHCCO...", "..OHHHCCCO..", ".OHHHHCCCCO.", "OHHHHHCCCCCO", "OHHHHCCCCCCO", ".OHHCCCCCCO.", "..OHCCCCCO..", "...OCCCCO...", "....OCCO....", ".....OO....." },
            FallbackIcon.Gear => new[] { "....OOOO....", "....OCCO....", ".OOOCCCCOOO.", ".OCCCCCCCCO.", "OOCCOOOOCCOO", "OCCHO..OCCCO", "OCCHO..OCCCO", "OOCCOOOOCCOO", ".OCCCCCCCCO.", ".OOOCCCCOOO.", "....OCCO....", "....OOOO...." },
            _ => new[] { "....OOOO....", "....OCCO....", "....OHHO....", "....OHHO....", "...OHHHHO...", "..OHHHHHHO..", ".OHHCCCCCCO.", ".OHCCCCCCCO.", ".OHCCCCCCCO.", ".OCCCCCCCCO.", "..OCCCCCCO..", "...OOOOOO..." }
        };
        Texture2D texture = new(12, 12, TextureFormat.RGBA32, false) { name = displayName + " Icon", filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < 12; y++)
            for (int x = 0; x < 12; x++)
                texture.SetPixel(x, 11 - y, pixels[y][x] switch
                {
                    'O' => new Color(.23f, .17f, .15f),
                    'H' => Color.Lerp(tint, Color.white, .7f),
                    'C' => tint,
                    _ => Color.clear
                });
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, 12, 12), Vector2.one * .5f, 20f);
    }

    private void OnValidate()
    {
        maxStack = IsEquipment ? 1 : Mathf.Clamp(maxStack, 1, 50);
    }

    private void OnDisable()
    {
        if (generatedIcon == null) return;
        Texture2D texture = generatedIcon.texture;
        if (Application.isPlaying)
        {
            Destroy(generatedIcon);
            Destroy(texture);
        }
        else
        {
            DestroyImmediate(generatedIcon);
            DestroyImmediate(texture);
        }
        generatedIcon = null;
    }
}
