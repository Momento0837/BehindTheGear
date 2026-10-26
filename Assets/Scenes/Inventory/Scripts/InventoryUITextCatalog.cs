using System;
using UnityEngine;

/// <summary>Display-only inventory text loaded from Resources/Inventory/inventory_ui_text.json.</summary>
public sealed class InventoryUITextCatalog
{
    [Serializable] public sealed class ItemText { public string id; public string name; public string description; }
    [Serializable] public sealed class NamedText { public string id; public string name; public string description; }
    [Serializable] public sealed class UiText { public string id; public string text; }

    public ItemText[] items = Array.Empty<ItemText>();
    public NamedText[] stats = Array.Empty<NamedText>();
    public NamedText[] equipmentSlots = Array.Empty<NamedText>();
    public NamedText[] categories = Array.Empty<NamedText>();
    public UiText[] ui = Array.Empty<UiText>();

    public static InventoryUITextCatalog Load(TextAsset overrideAsset)
    {
        TextAsset source = overrideAsset != null
            ? overrideAsset
            : Resources.Load<TextAsset>("Inventory/inventory_ui_text");

        if (source == null)
        {
            Debug.LogWarning("[InventoryUI] Display text JSON was not found at Resources/Inventory/inventory_ui_text.json.");
            return new InventoryUITextCatalog();
        }

        try
        {
            return JsonUtility.FromJson<InventoryUITextCatalog>(source.text) ?? new InventoryUITextCatalog();
        }
        catch (Exception exception)
        {
            Debug.LogError($"[InventoryUI] Display text JSON could not be parsed: {exception.Message}", source);
            return new InventoryUITextCatalog();
        }
    }

    public string ItemName(InventoryItemDefinition item) => FindItem(item, true, item != null ? item.DisplayName : string.Empty);
    public string ItemDescription(InventoryItemDefinition item) => FindItem(item, false, item != null ? item.Description : string.Empty);
    public string StatName(PersonalStatType type, string fallback) => FindNamed(stats, type.ToString(), true, fallback);
    public string StatDescription(PersonalStatType type, string fallback = "") => FindNamed(stats, type.ToString(), false, fallback);
    public string EquipmentSlotName(InventoryItemDefinition.EquipmentSlot slot, string fallback) => FindNamed(equipmentSlots, slot.ToString(), true, fallback);
    public string CategoryName(InventoryItemDefinition.ItemCategory category, string fallback) => FindNamed(categories, category.ToString(), true, fallback);
    public string CategoryName(string id, string fallback) => FindNamed(categories, id, true, fallback);
    public string Ui(string id, string fallback)
    {
        if (ui != null)
            foreach (UiText entry in ui)
                if (entry != null && string.Equals(entry.id, id, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(entry.text))
                    return entry.text;
        return fallback;
    }

    private string FindItem(InventoryItemDefinition item, bool readName, string fallback)
    {
        if (item != null && items != null)
            foreach (ItemText entry in items)
                if (entry != null && string.Equals(entry.id, item.ItemId, StringComparison.OrdinalIgnoreCase))
                {
                    string value = readName ? entry.name : entry.description;
                    return string.IsNullOrEmpty(value) ? fallback : value;
                }
        return fallback;
    }

    private static string FindNamed(NamedText[] entries, string id, bool readName, string fallback)
    {
        if (entries != null)
            foreach (NamedText entry in entries)
                if (entry != null && string.Equals(entry.id, id, StringComparison.OrdinalIgnoreCase))
                {
                    string value = readName ? entry.name : entry.description;
                    return string.IsNullOrEmpty(value) ? fallback : value;
                }
        return fallback;
    }
}
